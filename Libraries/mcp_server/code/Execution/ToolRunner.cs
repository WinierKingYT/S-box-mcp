using McpBridge.Extensions;
using McpBridge.Middleware;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace McpBridge.Execution;

public class ToolRunner
{
	private readonly ToolRegistry _registry;
	private readonly SchemaGenerator _schema;
	private readonly RateLimiter _rateLimiter;
	private long _totalCalls;

	public ToolRunner( ToolRegistry registry, SchemaGenerator schema, RateLimiter rateLimiter )
	{
		_registry = registry;
		_schema = schema;
		_rateLimiter = rateLimiter;
	}

	public long TotalCalls => Interlocked.Read( ref _totalCalls );
	public ToolRegistry Registry => _registry;

	public object GetSchemaDefs() => _schema.GetDefs( _registry.GetToolsDict() );

	public async Task<(int? id, string response)?> ExecuteAsync( string json )
	{
		int? id = null;
		string toolName = null;
		try
		{
			using var doc = JsonDocument.Parse( json );
			id = doc.RootElement.TryGetProperty( "id", out var i ) ? i.GetInt32() : null;
			var rpcMethod = doc.RootElement.GetProperty( "method" ).GetString();

			// Extract progress token from _meta.progressToken
			if ( doc.RootElement.TryGetProperty( "_meta", out var metaEl ) && metaEl.TryGetProperty( "progressToken", out var ptEl ) )
				McpToolBridge.CurrentProgressToken = ptEl.GetString() ?? "";

			if ( rpcMethod != "initialize" && rpcMethod != "ping" && !rpcMethod.StartsWith( "notifications/" ) )
			{
				if ( !_rateLimiter.TryAcquire() )
					return (id, id.RateLimited());
			}

			if ( rpcMethod == "ping" )
				return (id, id.ToOk( "\"pong\"" ));

			if ( rpcMethod == "notifications/initialized" || rpcMethod == "notifications/cancelled" )
				return null;

			if ( rpcMethod == "initialize" )
			{
				var result = id.ToOk( JsonSerializer.Serialize( new
				{
					protocolVersion = "2024-11-05",
					capabilities = new { tools = new { }, resources = new { }, prompts = new { } },
					serverInfo = new { name = "sbox-mcp", version = "1.0.0" }
				} ) );
				return (id, result);
			}

			if ( rpcMethod == "tools/list" || rpcMethod == "list_tools" )
				return (id, id.ToOk( JsonSerializer.Serialize( GetSchemaDefs() ) ));

			toolName = null;
			JsonElement paramsElement;

			if ( rpcMethod == "tools/call" )
			{
				var p = doc.RootElement.GetProperty( "params" );
				toolName = p.GetProperty( "name" ).GetString();
				paramsElement = p.TryGetProperty( "arguments", out var a ) ? a : default;
			}
			else
			{
				toolName = rpcMethod;
				paramsElement = doc.RootElement.TryGetProperty( "params", out var pp ) ? pp : default;
			}

			if ( !_registry.TryGet( toolName, out var mdesc, out var instance ) )
				return (id, id.MethodNotFound( toolName ));

			var schema = SchemaGenerator.Generate( mdesc );
			var validationError = Validation.ValidateArguments( paramsElement, schema );
			if ( validationError != null )
				return (id, id.InvalidParams( validationError ));

			var args = new List<object>();
			foreach ( var p in mdesc.Parameters )
			{
				args.Add( paramsElement.TryGetProperty( p.Name, out var v )
					? JsonSerializer.Deserialize( v.GetRawText(), p.ParameterType, JsonRpcExtensions.SerializerOpts )
					: null );
			}

			if ( McpScene.Active == null && Game.ActiveScene == null )
				return (id, id.ConnectionClosed());

			var sw = Stopwatch.StartNew();
			var res = mdesc.Invoke( instance, args.ToArray() );

		if ( res is Task task )
		{
			const int toolTimeoutMs = 30000;
			if ( !task.IsCompleted )
			{
				var timeoutTcs = new TaskCompletionSource<object>();
				_ = DelayAndComplete( timeoutTcs, toolTimeoutMs );
				while ( !task.IsCompleted && !timeoutTcs.Task.IsCompleted )
				{
					await GameTask.Delay( 10 );
				}
			}
			if ( !task.IsCompleted )
			{
				Interlocked.Increment( ref _totalCalls );
				McpReplay.Record( toolName, json, "Timed out", sw.ElapsedMilliseconds, false );
				Log.Warning( $"[MCP] Tool '{toolName}' timed out after {toolTimeoutMs}ms" );
				return (id, id.TimeoutError( toolName ));
			}
			await task;
				var taskDesc = TypeLibrary.GetType( task.GetType() );
				var resultProp = taskDesc?.Properties.FirstOrDefault( p => p.Name == "Result" && p.CanRead );
				if ( resultProp != null )
					res = resultProp.GetValue( task );
				else
					res = null;
			}
			Interlocked.Increment( ref _totalCalls );
			var resText = JsonSerializer.Serialize( res, JsonRpcExtensions.SerializerOpts );
			McpReplay.Record( toolName, json, resText, sw.ElapsedMilliseconds, true );

			var response = id.ToOk( JsonSerializer.Serialize( new { content = new[] { new { type = "text", text = resText } }, _meta = new { durationMs = Math.Round( sw.Elapsed.TotalMilliseconds, 1 ), toolName } } ) );
			return (id, response);
		}
		catch ( Exception ex )
		{
			Interlocked.Increment( ref _totalCalls );
			McpReplay.Record( toolName ?? "unknown", json, ex.Message, 0, false );
			return (id, id.InternalError( ex.InnerException?.Message ?? ex.Message ?? "Unknown error" ));
		}
	}

	private static async Task DelayAndComplete( TaskCompletionSource<object> tcs, int ms )
	{
		try
		{
			await GameTask.Delay( ms );
			tcs.TrySetResult( null );
		}
		catch
		{
			tcs.TrySetResult( null );
		}
	}
}
