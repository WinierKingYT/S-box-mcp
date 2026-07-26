using Sandbox;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Editor;

internal enum McpApprovalStatus
{
	Pending,
	Approved,
	Rejected,
	Cancelled,
	Expired,
	Stale,
	Executing,
	Succeeded,
	Failed,
	FailedPartial
}

internal sealed record McpQueuedOperation(
	string ToolName,
	JsonElement Arguments,
	string Preview );

internal sealed class McpApprovalRequest
{
	public string Id { get; init; }
	public string Summary { get; init; }
	public DateTimeOffset CreatedAt { get; init; }
	public DateTimeOffset ExpiresAt { get; init; }
	public McpApprovalStatus Status { get; set; }
	public IReadOnlyList<McpQueuedOperation> Operations { get; init; }
	public string SceneFingerprint { get; init; }
	public object Result { get; set; }
	public string Error { get; set; }
}

internal sealed record McpApprovalPanelItem(
	string Id,
	string Summary,
	string Status,
	DateTimeOffset ExpiresAt,
	string[] Preview );

internal sealed class McpApprovalService
{
	private readonly McpToolRegistry _registry;
	private readonly Func<McpConfig> _configProvider;
	private readonly McpActivityStore _activity;
	private readonly Func<string> _sceneFingerprintProvider;
	private readonly ConcurrentDictionary<string, McpApprovalRequest> _requests =
		new( StringComparer.Ordinal );

	public McpApprovalService(
		McpToolRegistry registry,
		Func<McpConfig> configProvider,
		McpActivityStore activity,
		Func<string> sceneFingerprintProvider = null )
	{
		_registry = registry;
		_configProvider = configProvider;
		_activity = activity;
		_sceneFingerprintProvider = sceneFingerprintProvider
			?? CaptureSceneFingerprint;
	}

	public bool RequiresApproval( McpToolDefinition tool )
	{
		var mode = _configProvider().ApprovalMode;
		if ( mode == "Disabled" ) return false;
		if ( tool.Impact == McpToolImpact.HighRisk ) return true;
		return mode == "AllMutations" && tool.Impact == McpToolImpact.SceneMutation;
	}

	public object SubmitSingle( McpToolDefinition tool, JsonElement arguments )
		=> Submit(
			tool.Description,
			new[] { QueueOperation( tool, arguments ) } );

	public object SubmitBatch( JsonElement arguments )
	{
		if ( arguments.ValueKind != JsonValueKind.Object
			|| !arguments.TryGetProperty( "operations", out var operationsElement )
			|| operationsElement.ValueKind != JsonValueKind.Array )
			throw new ArgumentException( "operations must be a JSON array" );
		var config = _configProvider();
		var count = operationsElement.GetArrayLength();
		if ( count == 0 || count > config.MaxBatchOperations )
			throw new ArgumentException(
				$"A batch must contain between 1 and {config.MaxBatchOperations} operations" );

		var operations = new List<McpQueuedOperation>();
		foreach ( var item in operationsElement.EnumerateArray() )
		{
			if ( item.ValueKind != JsonValueKind.Object
				|| !item.TryGetProperty( "tool", out var toolProperty )
				|| toolProperty.ValueKind != JsonValueKind.String )
				throw new ArgumentException( "Each operation requires a tool name" );
			var toolName = toolProperty.GetString();
			if ( !_registry.TryGet( toolName, out var tool )
				|| tool.Impact != McpToolImpact.SceneMutation
				|| !tool.SupportsBatch )
				throw new ArgumentException( $"Tool '{toolName}' cannot be used in a scene batch" );
			var operationArguments = item.TryGetProperty( "arguments", out var args )
				? args
				: EmptyArguments();
			operations.Add( QueueOperation( tool, operationArguments ) );
		}

		var summary = arguments.TryGetProperty( "summary", out var summaryProperty )
			? summaryProperty.GetString()
			: $"Batch of {operations.Count} scene changes";
		return Submit( summary, operations );
	}

	public object Get( string requestId )
	{
		ExpirePending();
		return _requests.TryGetValue( requestId, out var request )
			? ToView( request )
			: new { success = false, message = "Change request not found" };
	}

	public object Cancel( string requestId )
		=> ChangePendingStatus(
			requestId,
			McpApprovalStatus.Cancelled,
			"Cancelled by client" );

	public object Reject( string requestId )
		=> ChangePendingStatus(
			requestId,
			McpApprovalStatus.Rejected,
			"Rejected in editor" );

	public object Approve( string requestId )
	{
		ExpirePending();
		if ( !_requests.TryGetValue( requestId, out var request ) )
			return new { success = false, message = "Change request not found" };
		lock ( request )
		{
			if ( request.Status != McpApprovalStatus.Pending )
				return ToView( request );
			if ( request.Operations.Any( operation =>
					_registry.TryGet( operation.ToolName, out var definition )
					&& definition.Impact == McpToolImpact.SceneMutation )
				&& !string.Equals(
					request.SceneFingerprint,
					_sceneFingerprintProvider(),
					StringComparison.Ordinal ) )
			{
				request.Status = McpApprovalStatus.Stale;
				request.Error = "Scene changed after preview; submit the request again";
				_activity.Add( "approval", "stale", request.Summary );
				return ToView( request );
			}

			request.Status = McpApprovalStatus.Executing;
			var stopwatch = Stopwatch.StartNew();
			IDisposable batchScope = null;
			IDisposable sceneScope = null;
			var usedBatch = false;
			try
			{
				var sceneOperations = request.Operations.All( operation =>
					_registry.TryGet( operation.ToolName, out var definition )
					&& definition.Impact == McpToolImpact.SceneMutation );
				if ( sceneOperations )
				{
					sceneScope = SceneEditorSession.Scope();
					batchScope = SandboxMcpSceneUndoService.BeginBatch(
						request.Operations.Count == 1
							? request.Summary
							: $"Batch {request.Operations.Count} Changes" );
					usedBatch = true;
				}

				var results = new List<object>();
				foreach ( var operation in request.Operations )
				{
					if ( !_registry.TryGet( operation.ToolName, out var tool ) )
						throw new InvalidOperationException(
							$"Tool '{operation.ToolName}' is no longer registered" );
					results.Add( tool.Handler( operation.Arguments ) );
				}
				batchScope?.Dispose();
				batchScope = null;
				sceneScope?.Dispose();
				sceneScope = null;
				request.Status = McpApprovalStatus.Succeeded;
				request.Result = results;
				_activity.Add(
					"approval",
					"succeeded",
					request.Summary,
					stopwatch.ElapsedMilliseconds );
			}
			catch ( Exception exception )
			{
				batchScope?.Dispose();
				batchScope = null;
				sceneScope?.Dispose();
				sceneScope = null;
				request.Error = exception.GetBaseException().Message;
				if ( usedBatch )
				{
					var rollback = new SandboxMcpSceneUndoService().Undo();
					request.Status = rollback.Success
						? McpApprovalStatus.Failed
						: McpApprovalStatus.FailedPartial;
				}
				else
				{
					request.Status = McpApprovalStatus.Failed;
				}
				_activity.Add(
					"approval",
					request.Status.ToString().ToLowerInvariant(),
					request.Summary,
					stopwatch.ElapsedMilliseconds,
					exception );
			}
			return ToView( request );
		}
	}

	public IReadOnlyList<object> PendingSnapshot()
	{
		ExpirePending();
		return _requests.Values
			.Where( request => request.Status == McpApprovalStatus.Pending )
			.OrderBy( request => request.CreatedAt )
			.Select( ToView )
			.ToArray();
	}

	public IReadOnlyList<McpApprovalPanelItem> PendingItems()
	{
		ExpirePending();
		return _requests.Values
			.Where( request => request.Status == McpApprovalStatus.Pending )
			.OrderBy( request => request.CreatedAt )
			.Select( request => new McpApprovalPanelItem(
				request.Id,
				request.Summary,
				ToWireStatus( request.Status ),
				request.ExpiresAt,
				request.Operations.Select( operation =>
					operation.Preview ).ToArray() ) )
			.ToArray();
	}

	public int PendingCount
	{
		get
		{
			ExpirePending();
			return _requests.Values.Count( request =>
				request.Status == McpApprovalStatus.Pending );
		}
	}

	private object Submit(
		string summary,
		IReadOnlyList<McpQueuedOperation> operations )
	{
		ExpirePending();
		var config = _configProvider();
		if ( PendingCount >= config.MaxPendingApprovals )
			throw new InvalidOperationException( "The approval queue is full" );
		var now = DateTimeOffset.UtcNow;
		var request = new McpApprovalRequest
		{
			Id = Guid.NewGuid().ToString( "N" ),
			Summary = string.IsNullOrWhiteSpace( summary )
				? "MCP change request"
				: summary,
			CreatedAt = now,
			ExpiresAt = now.AddSeconds( config.ApprovalTimeoutSeconds ),
			Status = McpApprovalStatus.Pending,
			Operations = operations,
			SceneFingerprint = operations.Any( operation =>
				_registry.TryGet( operation.ToolName, out var definition )
				&& definition.Impact == McpToolImpact.SceneMutation )
					? _sceneFingerprintProvider()
					: ""
		};
		_requests[request.Id] = request;
		_activity.Add( "approval", "pending", request.Summary );
		return ToView( request );
	}

	private McpQueuedOperation QueueOperation(
		McpToolDefinition tool,
		JsonElement arguments )
	{
		var cloned = arguments.ValueKind == JsonValueKind.Undefined
			? EmptyArguments()
			: arguments.Clone();
		return new(
			tool.Name,
			cloned,
			McpPreviewBuilder.Build( tool.Name, cloned ) );
	}

	private object ChangePendingStatus(
		string requestId,
		McpApprovalStatus status,
		string message )
	{
		ExpirePending();
		if ( !_requests.TryGetValue( requestId, out var request ) )
			return new { success = false, message = "Change request not found" };
		lock ( request )
		{
			if ( request.Status == McpApprovalStatus.Pending )
			{
				request.Status = status;
				request.Error = message;
				_activity.Add(
					"approval",
					status.ToString().ToLowerInvariant(),
					request.Summary );
			}
			return ToView( request );
		}
	}

	private void ExpirePending()
	{
		var now = DateTimeOffset.UtcNow;
		foreach ( var request in _requests.Values )
		{
			lock ( request )
			{
				if ( request.Status == McpApprovalStatus.Pending
					&& request.ExpiresAt <= now )
				{
					request.Status = McpApprovalStatus.Expired;
					request.Error = "Approval request expired";
					_activity.Add( "approval", "expired", request.Summary );
				}
			}
		}
	}

	private static object ToView( McpApprovalRequest request )
		=> new
		{
			requestId = request.Id,
			status = ToWireStatus( request.Status ),
			summary = request.Summary,
			createdAt = request.CreatedAt,
			expiresAt = request.ExpiresAt,
			preview = request.Operations.Select( operation => new
			{
				tool = operation.ToolName,
				change = operation.Preview
			} ).ToArray(),
			result = request.Result,
			error = request.Error
		};

	private static string ToWireStatus( McpApprovalStatus status )
		=> status switch
		{
			McpApprovalStatus.Pending => "pending_approval",
			McpApprovalStatus.FailedPartial => "failed_partial",
			_ => status.ToString().ToLowerInvariant()
		};

	private static JsonElement EmptyArguments()
		=> JsonDocument.Parse( "{}" ).RootElement.Clone();

	private static string CaptureSceneFingerprint()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return "no-scene";
		var builder = new StringBuilder();
		foreach ( var gameObject in scene.GetAllObjects( true )
			.OrderBy( gameObject => gameObject.Id ) )
		{
			builder.Append( gameObject.Id ).Append( '|' )
				.Append( gameObject.Name ).Append( '|' )
				.Append( gameObject.Parent?.Id ).Append( '|' )
				.Append( gameObject.WorldPosition ).Append( '|' )
				.Append( gameObject.WorldRotation ).Append( '|' )
				.Append( gameObject.WorldScale ).Append( '|' )
				.Append( gameObject.IsPrefabInstance );
			foreach ( var component in gameObject.Components.GetAll()
				.OrderBy( component => component.Id ) )
				builder.Append( '|' ).Append( component.Id )
					.Append( ':' ).Append( component.GetType().FullName );
			builder.AppendLine();
		}
		return Convert.ToHexString(
			SHA256.HashData( Encoding.UTF8.GetBytes( builder.ToString() ) ) );
	}
}

internal static class McpPreviewBuilder
{
	public static string Build( string toolName, JsonElement arguments )
	{
		var objectId = GetString( arguments, "id" );
		var target = ResolveObject( objectId );
		var requested = DescribeRequestedValues( arguments );
		switch ( toolName )
		{
			case "create_object":
				return $"Before: no object. After: create '{GetString( arguments, "name" ) ?? "GameObject"}' ({requested}).";
			case "delete_object":
				return target == null
					? $"Delete object {objectId} (target currently missing)."
					: $"Before: '{target.Name}' with {CountDescendants( target )} descendants. After: deleted.";
			case "update_object_properties":
				return target == null
					? $"Update object {objectId} (target currently missing): {requested}."
					: $"Before: '{target.Name}' at {target.WorldPosition}. After: {requested}.";
			case "duplicate_object":
				return target == null
					? $"Duplicate object {objectId} (target currently missing)."
					: $"Before: one '{target.Name}' hierarchy. After: add a full duplicate beside it ({requested}).";
			case "set_parent":
				return target == null
					? $"Reparent object {objectId} (target currently missing)."
					: $"Before parent: {DescribeParent( target )}. After parent: {GetString( arguments, "parent_id" )}; {requested}.";
			case "add_component":
				return $"Before: {target?.Name ?? objectId} has its current components. After: add {GetString( arguments, "type" )} ({requested}).";
			case "remove_component":
				return $"Before: component {objectId} exists. After: remove {GetString( arguments, "type" ) ?? "selected component"}.";
			case "instantiate_prefab":
				return $"Before: no instance. After: instantiate {GetString( arguments, "path" )} ({requested}).";
			case "break_prefab":
				return $"Before: '{target?.Name ?? objectId}' is linked to a prefab. After: keep hierarchy and remove the prefab link.";
		}

		var fallbackTarget = GetString( arguments, "id" )
			?? GetString( arguments, "parent_id" )
			?? GetString( arguments, "path" );
		var fields = arguments.ValueKind == JsonValueKind.Object
			? string.Join( ", ", arguments.EnumerateObject()
				.Where( property => !IsSensitive( property.Name ) )
				.Select( property => property.Name ) )
			: "";
		return string.IsNullOrWhiteSpace( fallbackTarget )
			? $"{toolName}: {fields}"
			: $"{toolName} on {fallbackTarget}: {fields}";
	}

	private static string DescribeRequestedValues( JsonElement arguments )
	{
		if ( arguments.ValueKind != JsonValueKind.Object ) return "no arguments";
		var values = arguments.EnumerateObject()
			.Where( property => !IsSensitive( property.Name )
				&& property.Name is not "id" )
			.Select( property =>
			{
				var value = property.Value.GetRawText();
				if ( value.Length > 120 ) value = value[..120] + "…";
				return $"{property.Name}={value}";
			} )
			.ToArray();
		return values.Length == 0 ? "no additional values" : string.Join( ", ", values );
	}

	private static GameObject ResolveObject( string idText )
	{
		if ( !Guid.TryParse( idText, out var id ) ) return null;
		return Game.ActiveScene?.GetAllObjects( true )
			.FirstOrDefault( gameObject => gameObject.Id == id );
	}

	private static int CountDescendants( GameObject root )
		=> root.Children.Sum( child => 1 + CountDescendants( child ) );

	private static string DescribeParent( GameObject gameObject )
		=> gameObject.Parent == null || gameObject.Parent is Scene
			? "root"
			: $"{gameObject.Parent.Name} ({gameObject.Parent.Id})";

	private static string GetString( JsonElement arguments, string name )
		=> arguments.ValueKind == JsonValueKind.Object
			&& arguments.TryGetProperty( name, out var property )
			&& property.ValueKind == JsonValueKind.String
				? property.GetString()
				: null;

	private static bool IsSensitive( string name )
		=> name.Contains( "key", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "token", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "secret", StringComparison.OrdinalIgnoreCase );
}
