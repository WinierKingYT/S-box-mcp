using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Editor;

internal sealed class McpProtocolHandler
{
	private readonly McpToolRegistry _registry;
	private readonly McpToolExecutor _executor;
	private readonly McpRateLimiter _rateLimiter;
	private readonly IMcpLogger _logger;

	public McpProtocolHandler(
		McpToolRegistry registry,
		McpToolExecutor executor,
		McpRateLimiter rateLimiter,
		IMcpLogger logger )
	{
		_registry = registry;
		_executor = executor;
		_rateLimiter = rateLimiter;
		_logger = logger;
	}

	public async Task<string> ProcessAsync( string body )
	{
		McpRuntimeState.RecordRequest();
		object currentId = null;
		var hasId = false;

		try
		{
			using var document = JsonDocument.Parse( body );
			var root = document.RootElement;
			if ( root.ValueKind != JsonValueKind.Object )
				return McpJsonRpcSerializer.Error( null, -32600, "Invalid Request" );

			if ( root.TryGetProperty( "id", out var idProperty ) )
			{
				currentId = idProperty.Clone();
				hasId = true;
			}

			if ( !root.TryGetProperty( "method", out var methodProperty )
				|| methodProperty.ValueKind != JsonValueKind.String )
				return hasId
					? McpJsonRpcSerializer.Error( currentId, -32600, "Invalid Request" )
					: null;

			var method = methodProperty.GetString();
			if ( !_rateLimiter.TryAcquire() )
				return hasId
					? McpJsonRpcSerializer.Error(
						currentId,
						-32001,
						"Rate limited: too many requests" )
					: null;

			if ( method == "initialize" )
				return HandleInitialize( root, currentId );
			if ( method is "notifications/initialized" or "notifications/cancelled" )
				return null;
			if ( method == "ping" )
				return McpJsonRpcSerializer.Success( currentId, new { } );
			if ( method == "tools/list" )
				return McpJsonRpcSerializer.Success( currentId, new { tools = GetToolList() } );
			if ( method == "tools/call" )
				return await HandleToolCallAsync( root, currentId );

			// Backward compatibility with the original direct-call protocol.
			if ( method == "list_tools" )
				return McpJsonRpcSerializer.Success( currentId, GetToolList() );
			if ( _registry.TryGet( method, out var legacyTool ) )
			{
				var arguments = root.TryGetProperty( "params", out var parameters )
					? parameters
					: default;
				return McpJsonRpcSerializer.Success(
					currentId,
					await _executor.ExecuteAsync( method, legacyTool, arguments ) );
			}

			return hasId
				? McpJsonRpcSerializer.Error(
					currentId,
					-32601,
					$"Method not found: {method}" )
				: null;
		}
		catch ( JsonException exception )
		{
			_logger.Warning( $"[MCP] JSON Parse Error: {exception.Message}" );
			return McpJsonRpcSerializer.Error(
				currentId,
				-32700,
				$"Parse error: {exception.Message}" );
		}
		catch ( Exception exception )
		{
			_logger.Error( $"[MCP] Execution Error: {exception}" );
			return hasId
				? McpJsonRpcSerializer.Error(
					currentId,
					-32603,
					$"Internal error: {exception.GetBaseException().Message}" )
				: null;
		}
	}

	private static string HandleInitialize( JsonElement root, object currentId )
	{
		var requestedVersion = McpProtocolConstants.LatestVersion;
		if ( root.TryGetProperty( "params", out var parameters )
			&& parameters.ValueKind == JsonValueKind.Object
			&& parameters.TryGetProperty( "protocolVersion", out var versionProperty )
			&& versionProperty.ValueKind == JsonValueKind.String )
			requestedVersion = versionProperty.GetString();

		var selectedVersion = McpProtocolConstants.SupportedVersions.Contains( requestedVersion )
			? requestedVersion
			: McpProtocolConstants.LatestVersion;
		var clientName = "";
		var clientVersion = "";
		if ( root.TryGetProperty( "params", out var clientParameters )
			&& clientParameters.ValueKind == JsonValueKind.Object
			&& clientParameters.TryGetProperty( "clientInfo", out var clientInfo )
			&& clientInfo.ValueKind == JsonValueKind.Object )
		{
			clientName = ReadString( clientInfo, "name" );
			clientVersion = ReadString( clientInfo, "version" );
		}
		McpRuntimeState.RecordClient(
			clientName,
			clientVersion,
			selectedVersion,
			selectedVersion == requestedVersion ? "Compatible" : "Negotiated fallback" );
		return McpJsonRpcSerializer.Success( currentId, new
		{
			protocolVersion = selectedVersion,
			capabilities = new { tools = new { listChanged = false } },
			serverInfo = new
			{
				name = "sbox-mcp",
				title = "s&box MCP AI Bridge",
				version = McpRuntimeState.ServerVersion
			},
			instructions = "Scene mutations return pending_approval and execute only after approval in MCP Control Center."
		} );
	}

	private async Task<string> HandleToolCallAsync( JsonElement root, object currentId )
	{
		if ( !root.TryGetProperty( "params", out var parameters )
			|| parameters.ValueKind != JsonValueKind.Object
			|| !parameters.TryGetProperty( "name", out var nameProperty )
			|| nameProperty.ValueKind != JsonValueKind.String )
			return McpJsonRpcSerializer.Error(
				currentId,
				-32602,
				"tools/call requires params.name" );

		var toolName = nameProperty.GetString();
		if ( !_registry.TryGet( toolName, out var tool ) )
			return McpJsonRpcSerializer.Error(
				currentId,
				-32602,
				$"Unknown tool: {toolName}" );

		var arguments = parameters.TryGetProperty( "arguments", out var argumentProperty )
			? argumentProperty
			: default;
		try
		{
			var result = await _executor.ExecuteAsync( toolName, tool, arguments );
			var text = result is string stringResult
				? stringResult
				: JsonSerializer.Serialize( result );
			return McpJsonRpcSerializer.Success( currentId, new
			{
				content = new[] { new { type = "text", text } },
				structuredContent = new { result },
				isError = false
			} );
		}
		catch ( Exception exception )
		{
			return McpJsonRpcSerializer.Success( currentId, new
			{
				content = new[]
				{
					new
					{
						type = "text",
						text = exception.GetBaseException().Message
					}
				},
				isError = true
			} );
		}
	}

	private object GetToolList()
		=> _registry.Snapshot().Select( tool => new
		{
			name = tool.Name,
			description = tool.Description,
			inputSchema = tool.InputSchema,
			annotations = new
			{
				readOnlyHint = tool.Impact == McpToolImpact.ReadOnly,
				destructiveHint = tool.Impact is McpToolImpact.SceneMutation
					or McpToolImpact.HighRisk,
				idempotentHint = tool.Impact == McpToolImpact.ReadOnly,
				openWorldHint = tool.Impact == McpToolImpact.HighRisk
			},
			_meta = new System.Collections.Generic.Dictionary<string, object>
			{
				{ "sbox/impact", tool.Impact.ToString() },
				{ "sbox/supportsBatch", tool.SupportsBatch }
			}
		} ).ToList();

	private static string ReadString( JsonElement value, string name )
		=> value.TryGetProperty( name, out var property )
			&& property.ValueKind == JsonValueKind.String
				? property.GetString()
				: "";
}
