using McpBridge;
using McpBridge.Extensions;
using McpBridge.Middleware;
using McpBridge.Execution;
using Sandbox;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Editor;

public static class McpEditorServer
{
	private const int DefaultPort = 29016;
	private const string DefaultApiKey = "sbox-ai-2026";
	private static int _port = DefaultPort;
	private static string _apiKey = DefaultApiKey;
	private static TcpListener _listener;
	private static CancellationTokenSource _cts;
	private record SseSession( NetworkStream Stream, CancellationTokenSource Cts, System.Threading.SemaphoreSlim WriteLock, string RemoteEndPoint, DateTime ConnectedAt, long RequestCount = 0, string LogLevel = "info" )
	{
		public CancellationTokenSource ToolCts { get; set; } = new();
		public Task CurrentToolTask { get; set; }
		public McpBridge.Middleware.SlidingWindowRateLimiter RateLimiter { get; } = new( 60 );
	}
	private static readonly ConcurrentDictionary<string, SseSession> _sessions = new();
	private record ToolDef( Func<JsonElement, Task<object>> Handler, string Description, string Group = "Editor", object InputSchema = null, object Annotations = null );
	private static readonly ConcurrentDictionary<string, ToolDef> _tools = new();
	private static long _sseEventId;
	private static CancellationTokenSource _statePollCts;
	private static string _lastStateSnapshot = "";
	private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _subscriptions = new();
	private static string _lastPhase = "", _lastDay = "", _lastAlarm = "";
	private static readonly ConcurrentDictionary<string, string> _resourceHashes = new();
	private static string _lastResourceListHash = "";
	private static Func<McpContext, Task> _pipeline;
	private static volatile string _defaultLogLevel = "info";
	private static readonly DateTime _startTime = DateTime.UtcNow;

	public static void ReportProgress( double progress, double? total = null, string message = null )
	{
		McpToolBridge.ReportProgress( progress, total, message );
	}

	private static string GetSessionLogLevel( string sid )
	{
		if ( !string.IsNullOrEmpty( sid ) && _sessions.TryGetValue( sid, out var session ) )
			return session.LogLevel;
		return _defaultLogLevel;
	}

	public static void RegisterTool( string name, string description, Func<JsonElement, object> handler, object inputSchema = null, string group = "Editor", object annotations = null )
	{
		_tools[name] = new ToolDef( args => Task.FromResult( handler( args ) ), description, group, inputSchema, annotations );
		McpToolBridge.RegisterGlobalToolName( name );
		Log.Info( $"[MCP] Tool registered: {name} \u2014 {description}" );
	}

	public static void RegisterToolAsync( string name, string description, Func<JsonElement, Task<object>> handler, object inputSchema = null, string group = "Editor", object annotations = null )
	{
		_tools[name] = new ToolDef( handler, description, group, inputSchema, annotations );
		McpToolBridge.RegisterGlobalToolName( name );
		Log.Info( $"[MCP] Async tool registered: {name} \u2014 {description}" );
	}

	public static async Task BroadcastLogAsync( string level, string logger, string message, JsonElement? data = null, string sourceSessionId = null )
	{
		var eid = Interlocked.Increment( ref _sseEventId );
		var logEntry = new Dictionary<string, object>
		{
			["level"] = level,
			["logger"] = logger,
			["message"] = message
		};
		if ( data.HasValue ) logEntry["data"] = data.Value;
		var payload = JsonSerializer.Serialize( new { jsonrpc = "2.0", method = "notifications/message", @params = logEntry } );
		var msg = $"id: {eid}\nevent: message\ndata: {payload}\n\n";
		var buf = Encoding.UTF8.GetBytes( msg );
		foreach ( var sid in _sessions.Keys.ToArray() )
		{
			if ( !_sessions.TryGetValue( sid, out var session ) ) continue;
			if ( !ShouldLog( level, session.LogLevel ) ) continue;
			try
			{
				await session.WriteLock.WaitAsync();
				try { await session.Stream.WriteAsync( buf, 0, buf.Length ); await session.Stream.FlushAsync(); }
				finally { session.WriteLock.Release(); }
			}
			catch { _sessions.TryRemove( sid, out _ ); }
		}
	}

	private static bool ShouldLog( string level, string sessionLogLevel )
	{
		var levels = new[] { "debug", "info", "notice", "warning", "error", "critical" };
		var currentIdx = Array.IndexOf( levels, sessionLogLevel );
		var msgIdx = Array.IndexOf( levels, level );
		return currentIdx >= 0 && msgIdx >= currentIdx;
	}

	public static async Task BroadcastEventAsync( string eventType, string data, string resourceUri = null )
	{
		var isMcpNotification = eventType.StartsWith( "notifications/" );
		var payload = isMcpNotification
			? JsonSerializer.Serialize( new { jsonrpc = "2.0", method = eventType, @params = JsonDocument.Parse( data ?? "{}" ).RootElement } )
			: data;
		var eid = Interlocked.Increment( ref _sseEventId );
		var msg = $"id: {eid}\nevent: {(isMcpNotification ? "message" : eventType)}\ndata: {payload}\n\n";
		var buf = Encoding.UTF8.GetBytes( msg );
		var targets = resourceUri != null && _subscriptions.TryGetValue( resourceUri, out var subs )
			? subs.Keys.Where( s => _sessions.ContainsKey( s ) ).ToArray()
			: _sessions.Keys.ToArray();
		foreach ( var sid in targets )
		{
			if ( !_sessions.TryGetValue( sid, out var session ) ) continue;
			try
			{
				await session.WriteLock.WaitAsync();
				try { await session.Stream.WriteAsync( buf, 0, buf.Length ); await session.Stream.FlushAsync(); }
				finally { session.WriteLock.Release(); }
			}
			catch { _sessions.TryRemove( sid, out _ ); }
		}
	}

	public static void Start( int? port = null, string apiKey = null )
	{
		Stop();
		try
		{
			var cfg = McpConfig.Load();
			_port = port ?? cfg.Port;
			_apiKey = apiKey ?? cfg.ApiKey;
		}
		catch
		{
			_port = port ?? DefaultPort;
			_apiKey = apiKey ?? DefaultApiKey;
		}
		RegisterBuiltinTools();
		McpToolBridge.OnToolsChanged += () => _ = BroadcastEventAsync( "notifications/tools/list_changed", "{}" );
		McpToolBridge.OnProgress += ( progress, total, message ) =>
		{
			var token = McpToolBridge.CurrentProgressToken.Value;
			if ( string.IsNullOrEmpty( token ) ) return;
			var data = new Dictionary<string, object> { ["progressToken"] = token, ["progress"] = progress };
			if ( total.HasValue ) data["total"] = total.Value;
			if ( message != null ) data["message"] = message;
			_ = BroadcastEventAsync( "notifications/progress", JsonSerializer.Serialize( data ) );
		};
		_cts = new CancellationTokenSource();
		_ = AcceptLoopAsync();
		_statePollCts = new CancellationTokenSource();
		_ = PollStateAsync( _statePollCts.Token );
		_ = CleanupSessionsAsync();
	}

	public static void Stop()
	{
		_cts?.Cancel();
		_statePollCts?.Cancel();
		try { _listener?.Stop(); } catch { }
		_listener = null;
		foreach ( var kv in _sessions.ToArray() )
		{
			try { kv.Value.Cts.Cancel(); } catch { }
			try { kv.Value.Stream.Close(); } catch { }
		}
		_sessions.Clear();
	}

	private static void RegisterBuiltinTools()
	{
		var builder = new PipelineBuilder();
		builder.Use<LogMiddleware>();
		builder.Use<RateLimitMiddleware>();
		_pipeline = builder.BuildContext( HandleMessageCoreAsync );

		RegisterToolAsync( "list_tools", "List all available tools (editor + game) with optional pagination and filtering", async args =>
		{
			McpBridge.McpBridgeAutoInit.EnsureCreated();
			var page = args.TryGetProperty( "page", out var pn ) ? Math.Max( 1, pn.GetInt32() ) : 1;
			var perPage = args.TryGetProperty( "perPage", out var pp ) ? Math.Clamp( pp.GetInt32(), 1, 500 ) : 100;
			var query = args.TryGetProperty( "query", out var q ) ? q.GetString() ?? "" : "";
			var groupFilter = args.TryGetProperty( "group", out var g ) ? g.GetString() ?? "" : "";

			var all = new List<Dictionary<string, object>>();
			foreach ( var t in _tools )
			{
				if ( !string.IsNullOrEmpty( query ) && t.Key.IndexOf( query, StringComparison.OrdinalIgnoreCase ) < 0 && (t.Value.Description?.IndexOf( query, StringComparison.OrdinalIgnoreCase ) ?? -1) < 0 )
					continue;
				if ( !string.IsNullOrEmpty( groupFilter ) && (t.Value.Group ?? "") != groupFilter )
					continue;
				all.Add( new Dictionary<string, object> { ["name"] = t.Key, ["group"] = t.Value.Group ?? "Editor", ["description"] = t.Value.Description ?? "", ["annotations"] = t.Value.Annotations ?? new { } } );
			}
			var gameToolsJson = await McpToolBridge.ListAllGameTools();
			if ( !string.IsNullOrEmpty( gameToolsJson ) )
			{
				using var gameDoc = JsonDocument.Parse( gameToolsJson );
				foreach ( var gt in gameDoc.RootElement.EnumerateArray() )
				{
					var name = gt.GetProperty( "name" ).GetString();
					if ( !string.IsNullOrEmpty( query ) && name.IndexOf( query, StringComparison.OrdinalIgnoreCase ) < 0 )
						continue;
					var group = gt.TryGetProperty( "group", out var grp ) ? grp.GetString() ?? "" : "";
					if ( !string.IsNullOrEmpty( groupFilter ) && group != groupFilter )
						continue;
					var entry = new Dictionary<string, object> { ["name"] = name, ["group"] = group, ["description"] = gt.TryGetProperty( "description", out var d ) ? d.GetString() ?? "" : "" };
					if ( gt.TryGetProperty( "annotations", out var ann ) && ann.ValueKind == JsonValueKind.Object )
						entry["annotations"] = ann;
					all.Add( entry );
				}
			}
			var total = all.Count;
			var paged = all.Skip( (page - 1) * perPage ).Take( perPage ).ToList();
			return new { total, page, perPage, tools = paged };
		}, new { type = "object", properties = new { page = new { type = "number", description = "Page number (1-based)" }, perPage = new { type = "number", description = "Results per page (max 500)" }, query = new { type = "string", description = "Search query" }, group = new { type = "string", description = "Filter by group" } }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true } );
		RegisterTool( "list_objects", "List GameObjects in the scene with optional pagination", args =>
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			var page = args.TryGetProperty( "page", out var pn ) ? Math.Max( 1, pn.GetInt32() ) : 1;
			var perPage = args.TryGetProperty( "perPage", out var pp ) ? Math.Clamp( pp.GetInt32(), 1, 500 ) : 50;
			var all = scene.GetAllObjects( true ).ToList();
			var total = all.Count;
			var paged = all.Skip( (page - 1) * perPage ).Take( perPage ).Select( g => new { g.Name, g.Id } ).ToList();
			return new { total, page, perPage, objects = paged };
		}, new { type = "object", properties = new { page = new { type = "number", description = "Page number (1-based)" }, perPage = new { type = "number", description = "Results per page (max 500)" } }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true } );
		RegisterTool( "create_object", "Create a new GameObject", args =>
		{
			var name = args.GetProperty( "name" ).GetString();
			var pos = args.TryGetProperty( "position", out var p )
				? JsonSerializer.Deserialize<Vector3>( p.GetRawText() )
				: Vector3.Zero;
			var go = new GameObject( true, name );
			go.WorldPosition = pos;
			return new { id = go.Id.ToString(), name };
		}, new { type = "object", properties = new { name = new { type = "string" }, position = new { type = "string", description = "x,y,z" } }, required = new[] { "name" } }, annotations: new { destructiveHint = true } );

		RegisterTool( "sbox_create_gameobject", "Create a new GameObject in the scene", args =>
		{
			var name = args.GetProperty( "name" ).GetString();
			var x = args.TryGetProperty( "x", out var xp ) ? xp.GetSingle() : 0f;
			var y = args.TryGetProperty( "y", out var yp ) ? yp.GetSingle() : 0f;
			var z = args.TryGetProperty( "z", out var zp ) ? zp.GetSingle() : 0f;
			var go = new GameObject( true, name );
			go.WorldPosition = new Vector3( x, y, z );
			return new { id = go.Id.ToString(), name, position = new { x, y, z } };
		}, new { type = "object", properties = new { name = new { type = "string", description = "Display name" }, x = new { type = "number", description = "X position" }, y = new { type = "number", description = "Y position" }, z = new { type = "number", description = "Z position" } }, required = new[] { "name" } }, annotations: new { destructiveHint = true } );

		RegisterTool( "sbox_delete_gameobject", "Delete a GameObject from the scene by GUID", args =>
		{
			var idStr = args.GetProperty( "id" ).GetString();
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			if ( !Guid.TryParse( idStr, out var guid ) ) return "Invalid GUID format";
			var go = scene.Directory.FindByGuid( guid );
			if ( go == null ) return "GameObject not found";
			var name = go.Name;
			go.Destroy();
			return new { deleted = true, id = idStr, name };
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject" } }, required = new[] { "id" } }, annotations: new { destructiveHint = true } );

		RegisterTool( "sbox_set_transform", "Set position/rotation/scale of a GameObject", args =>
		{
			var idStr = args.GetProperty( "id" ).GetString();
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			if ( !Guid.TryParse( idStr, out var guid ) ) return "Invalid GUID format";
			var go = scene.Directory.FindByGuid( guid );
			if ( go == null ) return "GameObject not found";

			if ( args.TryGetProperty( "x", out var xp ) && args.TryGetProperty( "y", out var yp ) )
			{
				var z = args.TryGetProperty( "z", out var zp ) ? zp.GetSingle() : 0f;
				var pos = new Vector3( xp.GetSingle(), yp.GetSingle(), z );
				go.WorldPosition = pos;
			}

			if ( args.TryGetProperty( "pitch", out var pitch ) && args.TryGetProperty( "yaw", out var yaw ) )
			{
				var roll = args.TryGetProperty( "roll", out var r ) ? r.GetSingle() : 0f;
				go.WorldRotation = Rotation.From( pitch.GetSingle(), yaw.GetSingle(), roll );
			}

			if ( args.TryGetProperty( "scale", out var scale ) )
			{
				go.WorldScale = scale.GetSingle();
			}

			return new { id = idStr, name = go.Name, position = new { x = go.WorldPosition.x, y = go.WorldPosition.y, z = go.WorldPosition.z } };
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject" }, x = new { type = "number", description = "New X position" }, y = new { type = "number", description = "New Y position" }, z = new { type = "number", description = "New Z position" }, pitch = new { type = "number" }, yaw = new { type = "number" }, roll = new { type = "number" }, scale = new { type = "number" } }, required = new[] { "id" } }, annotations: new { destructiveHint = true } );

		RegisterTool( "sbox_add_component", "Add a component to a GameObject by GUID", args =>
		{
			var idStr = args.GetProperty( "id" ).GetString();
			var typeName = args.GetProperty( "type" ).GetString();
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			if ( !Guid.TryParse( idStr, out var guid ) ) return "Invalid GUID format";
			var go = scene.Directory.FindByGuid( guid );
			if ( go == null ) return "GameObject not found";

			var compTypes = TypeLibrary.GetTypes<Component>();
			var typeDesc = compTypes.FirstOrDefault( t => string.Equals( t.Name, typeName, StringComparison.OrdinalIgnoreCase ) );
			if ( typeDesc == null )
				return $"Component type '{typeName}' not found";

			var comp = go.Components.Create( typeDesc );
			return new { added = true, id = idStr, name = go.Name, componentType = typeDesc.Name, componentId = comp.Id.ToString() };
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject" }, type = new { type = "string", description = "Component type name (e.g. Sandbox.ModelComponent)" } }, required = new[] { "id", "type" } }, annotations: new { destructiveHint = true } );

		RegisterTool( "sbox_mcp_clients", "List all connected SSE clients and their stats", _ =>
		{
			var now = DateTime.UtcNow;
			var clients = _sessions.Select( kv => new
			{
				sessionId = kv.Key,
				connectedForSec = Math.Round( (now - kv.Value.ConnectedAt).TotalSeconds, 1 ),
				remoteEndPoint = kv.Value.RemoteEndPoint,
				requestCount = kv.Value.RequestCount,
				logLevel = kv.Value.LogLevel
			} ).ToList();
			return new { count = clients.Count, clients };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true } );

		RegisterToolAsync( "sbox_http_get", "Make an HTTP GET request. Returns status code and body.", async args =>
		{
			try
			{
				var url = args.GetProperty( "url" ).GetString();
				using var client = new System.Net.Http.HttpClient();
				client.Timeout = TimeSpan.FromSeconds( 15 );
				if ( args.TryGetProperty( "headers", out var h ) )
				{
					foreach ( var header in h.EnumerateObject() )
						client.DefaultRequestHeaders.TryAddWithoutValidation( header.Name, header.Value.GetString() );
				}
				var resp = await client.GetAsync( url );
				var body = await resp.Content.ReadAsStringAsync();
				return new { success = true, statusCode = (int)resp.StatusCode, contentType = resp.Content.Headers.ContentType?.ToString(), bodyLength = body.Length, body = body.Length < 10000 ? body : body.Substring( 0, 10000 ) + "..." };
			}
			catch ( Exception e ) { return new { error = e.Message }; }
		}, new { type = "object", properties = new { url = new { type = "string", description = "Target URL" }, headers = new { type = "string", description = "Optional JSON object of headers" } }, required = new[] { "url" } }, annotations: new { readOnlyHint = true, openWorldWarning = true } );

		RegisterTool( "sbox_mcp_bridge_status", "Show connected game bridges and their health", _ =>
		{
			var bridges = McpToolBridge.GetBridgeStatus();
			return new { count = bridges.Length, bridges = bridges.Select( b => new { b.name, errorCount = b.errors, version = b.version, toolCount = b.toolCount, capabilities = b.capabilities } ) };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true } );

		RegisterTool( "get_game_state", "Get current day/phase/time", _ =>
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			var gm = scene.GetAllComponents<BlackFridayGameManager>().FirstOrDefault();
			if ( gm == null ) return "No game manager found";
			return new { day = gm.CurrentDay, phase = gm.CurrentPhase.ToString(), timeLeft = gm.PhaseTimeRemaining };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true } );

		RegisterToolAsync( "sbox_http_post", "Make an HTTP POST request with JSON body.", async args =>
		{
			try
			{
				var url = args.GetProperty( "url" ).GetString();
				var body = args.TryGetProperty( "body", out var b ) ? b.GetString() ?? "{}" : "{}";
				using var client = new System.Net.Http.HttpClient();
				client.Timeout = TimeSpan.FromSeconds( 15 );
				if ( args.TryGetProperty( "headers", out var h ) )
				{
					foreach ( var header in h.EnumerateObject() )
						client.DefaultRequestHeaders.TryAddWithoutValidation( header.Name, header.Value.GetString() );
				}
				var content = new System.Net.Http.StringContent( body, Encoding.UTF8, "application/json" );
				var resp = await client.PostAsync( url, content );
				var respBody = await resp.Content.ReadAsStringAsync();
				return new { success = true, statusCode = (int)resp.StatusCode, contentType = resp.Content.Headers.ContentType?.ToString(), bodyLength = respBody.Length, body = respBody.Length <= 10000 ? respBody : respBody.Substring( 0, 10000 ) + "..." };
			}
			catch ( Exception e ) { return new { error = e.Message }; }
		}, new { type = "object", properties = new { url = new { type = "string", description = "Target URL" }, body = new { type = "string", description = "JSON body" }, headers = new { type = "string", description = "Optional JSON object of headers" } }, required = new[] { "url" } }, annotations: new { destructiveHint = true, openWorldWarning = true } );

		RegisterTool( "sbox_log_query", "Query recent logs with optional level and text filtering", args =>
		{
			var count = args.TryGetProperty( "count", out var c ) ? Math.Clamp( c.GetInt32(), 1, 500 ) : 50;
			var minLevel = args.TryGetProperty( "minLevel", out var l ) ? l.GetString() : null;
			var search = args.TryGetProperty( "search", out var s ) ? s.GetString() : null;
			var entries = McpLogBridge.GetRecent( count, minLevel, search );
			return new { count = entries.Count, entries = entries.Select( e => new { e.Level, e.Message, e.Time } ) };
		}, new { type = "object", properties = new { count = new { type = "number", description = "Max entries (1-500)" }, minLevel = new { type = "string", description = "Minimum level: debug/info/notice/warning/error/critical" }, search = new { type = "string", description = "Text search filter" } }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true } );

		RegisterTool( "sbox_log_clear", "Clear all buffered logs", _ =>
		{
			var before = McpLogBridge.Count;
			McpLogBridge.Clear();
			return new { cleared = true, removedCount = before };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { destructiveHint = true } );

		RegisterTool( "sbox_server_info", "Get MCP server configuration and status", _ =>
		{
			return new
			{
				port = _port,
				sessions = _sessions.Count,
				editorTools = _tools.Count,
				uptimeMin = Math.Round( (DateTime.UtcNow - _startTime).TotalMinutes, 1 ),
				bridges = McpToolBridge.GetBridgeStatus().Select( b => new { b.name, version = b.version, toolCount = b.toolCount } )
			};
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true } );

		RegisterTool( "sbox_update_config", "Update MCP config settings at runtime (apiKey, etc.). Port changes require restart.", args =>
		{
			var cfg = McpConfig.Load();
			var changes = new List<string>();

			if ( args.TryGetProperty( "apiKey", out var key ) )
			{
				var newKey = key.GetString() ?? "";
				if ( newKey.Length >= 8 )
				{
					_apiKey = newKey;
					cfg.ApiKey = newKey;
					changes.Add( "apiKey" );
				}
			}

			if ( changes.Count > 0 )
			{
				McpConfig.Save( cfg );
				_ = BroadcastLogAsync( "info", "server", $"Config updated: {string.Join( ", ", changes )}" );
			}

			return new { updated = changes, currentApiKey = _apiKey[..Math.Min( 4, _apiKey.Length )] + "..." };
		}, new { type = "object", properties = new { apiKey = new { type = "string", description = "New API key (min 8 chars)" } }, required = Array.Empty<string>() }, annotations: new { destructiveHint = true } );
	}

	// ── Accept loop ────────────────────────────────────────────────

	private static async Task AcceptLoopAsync()
	{
		const int maxRetries = 5;
		var retryCount = 0;

		while ( retryCount < maxRetries && !_cts.IsCancellationRequested )
		{
			try
			{
				_listener = new TcpListener( IPAddress.Loopback, _port );
				_listener.Start();
				Log.Info( $"[MCP] Editor server on tcp://localhost:{_port}/sse" );

				while ( !_cts.IsCancellationRequested )
				{
					try
					{
						var client = await _listener.AcceptTcpClientAsync();
						_ = HandleClientAsync( client );
					}
					catch ( ObjectDisposedException ) { break; }
					catch ( InvalidOperationException ) { break; }
					catch ( SocketException ) { if ( _cts.IsCancellationRequested ) break; }
				}
				return;
			}
			catch ( SocketException e ) when ( e.SocketErrorCode == SocketError.AccessDenied )
			{
				Log.Error( $"[MCP] Port {_port} access denied" );
				return;
			}
			catch ( Exception e )
			{
				retryCount++;
				Log.Error( $"[MCP] Accept loop crashed (retry {retryCount}/{maxRetries}): {e.GetType().Name} {e.Message}" );
				try { _listener?.Stop(); } catch { }
				_listener = null;
				if ( retryCount < maxRetries )
				{
					await GameTask.Delay( 3000 );
				}
			}
		}
	}

	// ── Per-client handler ─────────────────────────────────────────

	private static async Task HandleClientAsync( TcpClient client )
	{
		using ( client )
		{
			var stream = client.GetStream();
			var buffer = new byte[4096];
			var headerBuf = new List<byte>();
			var headerEnd = -1;

			try
			{
				while ( true )
				{
					var read = await stream.ReadAsync( buffer, 0, buffer.Length );
					if ( read == 0 ) return;
					headerBuf.AddRange( buffer.AsSpan( 0, read ).ToArray() );
					headerEnd = SearchBytes( headerBuf, "\r\n\r\n" );
					if ( headerEnd >= 0 ) break;
				}

				var raw = Encoding.UTF8.GetString( headerBuf.ToArray() );
				var lines = raw.Split( "\r\n" );
				var requestLine = lines[0].Split( ' ' );
				if ( requestLine.Length < 2 ) return;

				var method = requestLine[0];
				var path = requestLine[1];
				var headers = ParseHeaders( lines );

				Log.Info( $"[MCP] {method} {path}" );

				if ( path == "/health" )
				{
					var bridges = McpToolBridge.GetBridgeStatus();
					var health = JsonSerializer.Serialize( new
					{
						status = "ok",
						uptime = Math.Round( (DateTime.UtcNow - _startTime).TotalMinutes, 1 ),
						sessions = _sessions.Count,
						editorTools = _tools.Count,
						gameBridges = bridges.Select( b => new { b.name, errorCount = b.errors, version = b.version, toolCount = b.toolCount, capabilities = b.capabilities } )
					} );
					await WriteResponseAsync( stream, 200, "application/json", health );
					return;
				}

				var key = headers.GetValueOrDefault( "x-api-key" )?.Trim();
				if ( key != _apiKey )
				{
					await WriteResponseAsync( stream, 401, "text/plain", "Unauthorized" );
					return;
				}

				if ( path == "/sse" && method == "GET" )
				{
					var remoteEp = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
					await HandleSseAsync( stream, remoteEp );
					return;
				}

				if ( method == "POST" && (path == "/mcp" || path == "/jsonrpc") )
				{
					if ( !headers.TryGetValue( "Content-Type", out var ct ) || !ct.StartsWith( "application/json", StringComparison.OrdinalIgnoreCase ) )
					{
						await WriteResponseAsync( stream, 415, "application/json", "{\"error\":\"Content-Type must be application/json\"}" );
						return;
					}
					var body = await ReadBodyAsync( stream, headerBuf, headerEnd, headers, buffer );
					if ( body == null ) return;
					await HandleStreamableHttpAsync( stream, body );
					return;
				}

				if ( path.StartsWith( "/messages" ) && method == "POST" )
				{
					if ( !headers.TryGetValue( "Content-Type", out var ct ) || !ct.StartsWith( "application/json", StringComparison.OrdinalIgnoreCase ) )
					{
						await WriteResponseAsync( stream, 415, "application/json", "{\"error\":\"Content-Type must be application/json\"}" );
						return;
					}
					var body = await ReadBodyAsync( stream, headerBuf, headerEnd, headers, buffer );
					if ( body == null ) return;
					var sid = ParseQueryString( path ).GetValueOrDefault( "sessionId" ) ?? "default";
					await HandleMessageAsync( stream, sid, body );
					return;
				}

				if ( method == "OPTIONS" )
				{
					await WriteOptionsResponseAsync( stream );
					return;
				}

				await WriteResponseAsync( stream, 404, "text/plain", "Not Found" );
			}
			catch ( Exception e )
			{
				Log.Error( $"[MCP] Handler: {e.GetType().Name} {e.Message}" );
			}
		}
	}

	private static int SearchBytes( List<byte> data, string pattern )
	{
		var pat = Encoding.UTF8.GetBytes( pattern );
		for ( int i = 0; i <= data.Count - pat.Length; i++ )
		{
			var match = true;
			for ( int j = 0; j < pat.Length; j++ )
			{
				if ( data[i + j] != pat[j] ) { match = false; break; }
			}
			if ( match ) return i;
		}
		return -1;
	}

	// ── Response helpers ───────────────────────────────────────────

	private static async Task WriteResponseAsync( NetworkStream stream, int status, string contentType, string body )
	{
		var statusText = status switch { 200 => "OK", 202 => "Accepted", 204 => "No Content", 401 => "Unauthorized", 404 => "Not Found", _ => "" };
		var sb = new StringBuilder();
		sb.Append( $"HTTP/1.1 {status} {statusText}\r\n" );
		sb.Append( $"Content-Type: {contentType}\r\n" );
		sb.Append( "Access-Control-Allow-Origin: *\r\n" );
		if ( body != null )
		{
			var buf = Encoding.UTF8.GetBytes( body );
			sb.Append( $"Content-Length: {buf.Length}\r\n\r\n" );
			var header = Encoding.UTF8.GetBytes( sb.ToString() );
			await stream.WriteAsync( header, 0, header.Length );
			await stream.WriteAsync( buf, 0, buf.Length );
		}
		else
		{
			sb.Append( "Content-Length: 0\r\n\r\n" );
			var header = Encoding.UTF8.GetBytes( sb.ToString() );
			await stream.WriteAsync( header, 0, header.Length );
		}
		await stream.FlushAsync();
	}

	private static async Task WriteOptionsResponseAsync( NetworkStream stream )
	{
		var sb = new StringBuilder();
		sb.Append( "HTTP/1.1 204 No Content\r\n" );
		sb.Append( "Access-Control-Allow-Origin: *\r\n" );
		sb.Append( "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" );
		sb.Append( "Access-Control-Allow-Headers: Content-Type, x-api-key\r\n" );
		sb.Append( "Content-Length: 0\r\n\r\n" );
		var header = Encoding.UTF8.GetBytes( sb.ToString() );
		await stream.WriteAsync( header, 0, header.Length );
		await stream.FlushAsync();
	}

	private static async Task<string> ReadBodyAsync( NetworkStream stream, List<byte> headerBuf, int headerEnd, Dictionary<string, string> headers, byte[] buffer )
	{
		var bodyStart = headerEnd + 4;
		var bodyBytes = new List<byte>();
		if ( bodyStart < headerBuf.Count )
			bodyBytes.AddRange( headerBuf.Skip( bodyStart ) );

		if ( headers.TryGetValue( "Content-Length", out var cl ) && int.TryParse( cl, out var contentLen ) && contentLen > 0 )
		{
			while ( bodyBytes.Count < contentLen )
			{
				var read = await stream.ReadAsync( buffer, 0, Math.Min( buffer.Length, contentLen - bodyBytes.Count ) );
				if ( read == 0 ) return null;
				bodyBytes.AddRange( buffer.AsSpan( 0, read ).ToArray() );
			}
		}
		return Encoding.UTF8.GetString( bodyBytes.ToArray() );
	}

	private static async Task HandleStreamableHttpAsync( NetworkStream stream, string body )
	{
		await GameTask.MainThread();

		if ( body == null || body.Length == 0 )
		{
			await WriteResponseAsync( stream, 400, "application/json", "{\"error\":\"Empty request body\"}" );
			return;
		}

		if ( _pipeline == null )
		{
			await WriteResponseAsync( stream, 503, "application/json", "{\"error\":\"Server not started\"}" );
			return;
		}

		// Batch JSON-RPC support: if body starts with '[', process each request individually
		var trimmed = body.TrimStart();
		if ( trimmed.StartsWith( "[" ) )
		{
			try
			{
				using var doc = JsonDocument.Parse( body );
				var results = new List<string>();
				foreach ( var item in doc.RootElement.EnumerateArray() )
				{
					var singleBody = item.GetRawText();
					var ctx = new McpContext { Body = singleBody, SessionId = "http" };
					try
					{
						if ( item.TryGetProperty( "id", out var idProp ) )
							ctx.Id = idProp.GetInt32();
					}
					catch { }
					await _pipeline( ctx );
					results.Add( ctx.Response ?? "{}" );
				}
				var batchResponse = "[" + string.Join( ",", results ) + "]";
				await WriteResponseAsync( stream, 200, "application/json", batchResponse );
			}
			catch ( Exception e )
			{
				await WriteResponseAsync( stream, 400, "application/json", $"{{\"error\":\"Batch parse failed: {e.Message}\"}}" );
			}
			return;
		}

		var ctx2 = new McpContext { Body = body, SessionId = "http" };

		// Parse id from body for middleware error responses
		try
		{
			using var doc = JsonDocument.Parse( body );
			if ( doc.RootElement.TryGetProperty( "id", out var idProp ) )
				ctx2.Id = idProp.GetInt32();
		}
		catch ( Exception e )
		{
			Log.Warning( $"[MCP] Streamable HTTP id parse failed: {e.GetType().Name} {e.Message}" );
		}

		await _pipeline( ctx2 );

		if ( ctx2.Response == null || ctx2.Response == "" )
		{
			await WriteResponseAsync( stream, 202, "application/json", "{}" );
			return;
		}

		await WriteResponseAsync( stream, 200, "application/json", ctx2.Response );
	}

	// ── SSE handler ────────────────────────────────────────────────

	private static async Task HandleSseAsync( NetworkStream stream, string remoteEndPoint )
	{
		var sid = Guid.NewGuid().ToString();
		var cts = new CancellationTokenSource();

		try
		{
			var sb = new StringBuilder();
			sb.Append( "HTTP/1.1 200 OK\r\n" );
			sb.Append( "Content-Type: text/event-stream\r\n" );
			sb.Append( "Cache-Control: no-cache\r\n" );
			sb.Append( "Connection: keep-alive\r\n" );
			sb.Append( "X-Accel-Buffering: no\r\n" );
			sb.Append( "Access-Control-Allow-Origin: *\r\n\r\n" );
			var header = Encoding.UTF8.GetBytes( sb.ToString() );
			await stream.WriteAsync( header, 0, header.Length );
			await stream.FlushAsync();

			var endpoint = $"event: endpoint\ndata: http://localhost:{_port}/messages?sessionId={sid}\n\n";
			var epBuf = Encoding.UTF8.GetBytes( endpoint );
			await stream.WriteAsync( epBuf, 0, epBuf.Length );
			await stream.FlushAsync();

			// Only add to sessions after initial handshake is complete
			_sessions[sid] = new SseSession( stream, cts, new System.Threading.SemaphoreSlim( 1, 1 ), remoteEndPoint, DateTime.UtcNow );

			var eventId = 0L;
			while ( !cts.IsCancellationRequested && !_cts.IsCancellationRequested )
			{
				try
				{
					await GameTask.Delay( 15000 );
					if ( cts.IsCancellationRequested || _cts.IsCancellationRequested ) break;
					var ping = $"id: {++eventId}\nevent: ping\ndata: {DateTime.UtcNow.Ticks}\n\n";
					var buf = Encoding.UTF8.GetBytes( ping );
					await stream.WriteAsync( buf, 0, buf.Length );
					await stream.FlushAsync();
				}
				catch ( OperationCanceledException ) { break; }
			}
		}
		catch ( OperationCanceledException ) { }
		catch ( Exception e )
		{
			Log.Error( $"[MCP] SSE: {e.GetType().Name} {e.Message}" );
		}
		finally
		{
			_sessions.TryRemove( sid, out _ );
		}
	}

	// ── Message handler via pipeline ───────────────────────────────

	private static async Task HandleMessageAsync( NetworkStream stream, string sid, string body )
	{
		// Track request count for SSE sessions
		if ( _sessions.TryGetValue( sid, out var existingSession ) )
			_sessions[sid] = existingSession with { RequestCount = existingSession.RequestCount + 1 };

		await GameTask.MainThread();

		var ctx = new McpContext { Body = body, SessionId = sid };
		// Inject per-session rate limiter
		if ( _sessions.TryGetValue( sid, out var sessForRateLimit ) )
			ctx.Items["rateLimiter"] = sessForRateLimit.RateLimiter;
		if ( _pipeline == null )
		{
			Log.Warning( "[MCP] Message before server started" );
			await WriteResponseAsync( stream, 503, "application/json", "{\"error\":\"Server not started\"}" );
			return;
		}
		await _pipeline( ctx );

		if ( ctx.Response == null ) return;
		var skipSse = ctx.Items.ContainsKey( "skipSse" );

		if ( !skipSse && _sessions.TryGetValue( sid, out var entry ) )
		{
			try
			{
				var eid = Interlocked.Increment( ref _sseEventId );
				var data = $"id: {eid}\nevent: message\ndata: {ctx.Response}\n\n";
				var buf = Encoding.UTF8.GetBytes( data );
				await entry.WriteLock.WaitAsync();
				try { await entry.Stream.WriteAsync( buf, 0, buf.Length ); await entry.Stream.FlushAsync(); }
				finally { entry.WriteLock.Release(); }
			}
			catch
			{
				_sessions.TryRemove( sid, out _ );
			}
		}

		// Acknowledge POST
		await WriteResponseAsync( stream, 202, "application/json", "{}" );
	}

	private static async Task HandleMessageCoreAsync( McpContext ctx )
	{
		var body = ctx.Body;
		var sid = ctx.SessionId;
		int? id = null;
		string method = null;
		try
		{
			using var doc = JsonDocument.Parse( body );
			id = doc.RootElement.TryGetProperty( "id", out var idProp ) ? idProp.GetInt32() : (int?)null;
			method = doc.RootElement.GetProperty( "method" ).GetString();
			ctx.Id = id;
			ctx.Method = method;

			// MCP protocol methods
			if ( method == "initialize" )
			{
				ctx.Response = id.ToOk( JsonSerializer.Serialize( new
				{
					protocolVersion = "2024-11-05",
					capabilities = new
					{
						tools = new { listChanged = true },
						resources = new { subscribe = true, listChanged = true, templates = new { } },
						prompts = new { },
						logging = new { },
						roots = new { },
						notifications = new { progress = true }
					},
					serverInfo = new { name = "sbox-mcp", version = "1.0.0" }
				} ) );
			}
			else if ( method == "notifications/initialized" )
			{
				ctx.Response = "";
				ctx.Items["skipSse"] = "true";
				return;
			}
			else if ( method == "notifications/cancelled" )
			{
				ctx.Response = "";
				ctx.Items["skipSse"] = "true";
				if ( _sessions.TryGetValue( sid, out var sess ) )
				{
					try { sess.ToolCts?.Cancel(); } catch { }
					sess.ToolCts = new CancellationTokenSource();
				}
				return;
			}
			else if ( method == "ping" )
			{
				ctx.Response = id.ToOk( "\"pong\"" );
			}
			else if ( method == "logging/setLevel" )
			{
				var p = doc.RootElement.GetProperty( "params" );
				var newLevel = p.TryGetProperty( "level", out var l ) ? l.GetString() ?? "info" : "info";
				if ( ctx.SessionId != null && ctx.SessionId != "http" && _sessions.TryGetValue( ctx.SessionId, out var sess ) )
				{
					var oldLevel = sess.LogLevel;
					_sessions[ctx.SessionId] = sess with { LogLevel = newLevel };
					_ = BroadcastLogAsync( "info", "server", $"Log level changed: {oldLevel} -> {newLevel}", sourceSessionId: ctx.SessionId );
				}
				else
				{
					var oldLevel = _defaultLogLevel;
					_defaultLogLevel = newLevel;
					_ = BroadcastLogAsync( "info", "server", $"Default log level changed: {oldLevel} -> {newLevel}" );
				}
				ctx.Response = id.ToOk( "{}" );
			}
			else if ( method == "logging/getLevel" )
			{
				var level = GetSessionLogLevel( ctx.SessionId );
				ctx.Response = id.ToOk( JsonSerializer.Serialize( new { level } ) );
			}
			else if ( method == "logging/listLevels" )
			{
				ctx.Response = id.ToOk( JsonSerializer.Serialize( new { levels = new[] { "debug", "info", "notice", "warning", "error", "critical" } } ) );
			}
			else if ( method == "resources/list" )
			{
				var resources = new List<object>
				{
					new { uri = "sbox://scene/state", name = "Game State", mimeType = "application/json", description = "Current phase, day, economy, alarm" },
					new { uri = "sbox://entities", name = "Scene Entities", mimeType = "application/json", description = "All GameObjects in the active scene with positions" },
					new { uri = "sbox://prefabs", name = "Prefabs", mimeType = "application/json", description = "All available prefab files" },
					new { uri = "sbox://materials", name = "Materials", mimeType = "application/json", description = "All available material files" },
					new { uri = "sbox://textures", name = "Textures", mimeType = "application/json", description = "All available texture files" },
					new { uri = "sbox://models", name = "Models", mimeType = "application/json", description = "All available model files" },
					new { uri = "sbox://sounds", name = "Sounds", mimeType = "application/json", description = "All available sound files" },
					new { uri = "sbox://maps", name = "Maps", mimeType = "application/json", description = "All available map files" },
					new { uri = "sbox://console/logs", name = "Console Logs", mimeType = "application/json", description = "Recent engine log entries" },
					new { uri = "sbox://image/{path}", name = "Image Preview", mimeType = "image/*", description = "Base64-encoded image. Use sbox://image/path/to/file.png" },
					new { uri = "sbox://file/{path}", name = "File Metadata", mimeType = "application/json", description = "File size/line count. Use sbox://file/path/to/file.ext" }
				};
				ctx.Response = id.ToOk( JsonSerializer.Serialize( new { resources } ) );
			}
			else if ( method == "resources/templates" || method == "resources/templates/list" )
			{
				var templates = new List<object>
				{
					new { uriTemplate = "sbox://image/{path}", name = "Image Preview", mimeType = "image/*", description = "Base64-encoded image at the given path" },
					new { uriTemplate = "sbox://file/{path}", name = "File Metadata", mimeType = "application/json", description = "File size/line count at the given path" }
				};
				ctx.Response = id.ToOk( JsonSerializer.Serialize( new { templates } ) );
			}
			else if ( method == "resources/read" )
			{
				var uri = doc.RootElement.GetProperty( "params" ).GetProperty( "uri" ).GetString();
				ctx.Response = uri switch
				{
					"sbox://scene/state" => id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( GetSceneState() ) } } } ) ),
					"sbox://entities" => id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( GetSceneEntities() ) } } } ) ),
					"sbox://prefabs" => id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( ListAssetsByExt( ".prefab" ) ) } } } ) ),
					"sbox://materials" => id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( ListAssetsByExt( ".vmat" ) ) } } } ) ),
					"sbox://textures" => id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( ListAssetsByExt( ".vtex" ) ) } } } ) ),
					"sbox://models" => id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( ListAssetsByExt( ".vmdl" ) ) } } } ) ),
					"sbox://sounds" => id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( ListAssetsByExt( ".vsnd", ".vsndevts", ".wav", ".mp3" ) ) } } } ) ),
					"sbox://maps" => id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( ListAssetsByExt( ".sbox" ) ) } } } ) ),
					"sbox://console/logs" => id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( McpLogBridge.GetRecent( 200 ) ) } } } ) ),
					string s when s.StartsWith( "sbox://image/" ) => GetImageResource( uri, id ),
					string s when s.StartsWith( "sbox://file/" ) => GetFilePreview( uri, id, "file" ),
					_ => id.ResourceNotFound( uri )
				};
			}
			else if ( method == "resources/subscribe" )
			{
				var uri = doc.RootElement.GetProperty( "params" ).GetProperty( "uri" ).GetString();
				_subscriptions.AddOrUpdate( uri, _ => new ConcurrentDictionary<string, byte>( new[] { new KeyValuePair<string, byte>( sid, 0 ) } ), ( _, set ) => { set.TryAdd( sid, 0 ); return set; } );
				ctx.Response = id.ToOk( "{}" );
			}
			else if ( method == "resources/unsubscribe" )
			{
				var uri = doc.RootElement.GetProperty( "params" ).GetProperty( "uri" ).GetString();
				if ( _subscriptions.TryGetValue( uri, out var set ) )
				{
					set.TryRemove( sid, out _ );
					if ( set.IsEmpty ) _subscriptions.TryRemove( uri, out _ );
				}
				ctx.Response = id.ToOk( "{}" );
			}
			else if ( method == "roots/list" )
			{
				var roots = new List<object>();
				try
				{
					var cwd = Environment.CurrentDirectory.Replace( '\\', '/' );
					roots.Add( new { uri = $"file:///{cwd.TrimStart( '/' )}", name = "Project Root" } );
				}
				catch ( Exception e )
				{
					Log.Warning( $"[MCP] roots/list failed: {e.GetType().Name} {e.Message}" );
				}
				ctx.Response = id.ToOk( JsonSerializer.Serialize( new { roots } ) );
			}
			else if ( method == "prompts/list" )
			{
				var prompts = new List<object>
				{
					new { name = "game_status", description = "Summarize the current game state (phase, day, economy, alarm)", arguments = new[] { new { name = "detail", description = "Level: basic or full", type = "string" } } },
					new { name = "scene_overview", description = "List all GameObjects and components in the scene", arguments = new[] { new { name = "max_objects", description = "Max objects to list", type = "number" } } },
					new { name = "economy_report", description = "Detailed economy and quota status", arguments = new object[0] }
				};
				ctx.Response = id.ToOk( JsonSerializer.Serialize( new { prompts } ) );
			}
			else if ( method == "prompts/get" )
			{
				var p = doc.RootElement.GetProperty( "params" );
				var promptName = p.GetProperty( "name" ).GetString();
				var detail = "basic";
				if ( p.TryGetProperty( "arguments", out var args ) )
				{
					if ( args.TryGetProperty( "detail", out var d ) ) detail = d.GetString() ?? "basic";
				}
				ctx.Response = promptName switch
				{
					"game_status" => id.ToOk( JsonSerializer.Serialize( new
					{
						messages = new[]
						{
							new { role = "user", content = new { type = "text", text = detail == "full" ? "Give me a complete status of the game including phase, day, economy, alarm level, and any active threats." : "What is the current game status?" } }
						}
					} ) ),
					"scene_overview" => id.ToOk( JsonSerializer.Serialize( new
					{
						messages = new[]
						{
							new { role = "user", content = new { type = "text", text = "What GameObjects are in the current scene? List their names, types, and any active components." } }
						}
					} ) ),
					"economy_report" => id.ToOk( JsonSerializer.Serialize( new
					{
						messages = new[]
						{
							new { role = "user", content = new { type = "text", text = "Give me a detailed economy report: personal cash, quotas, shared pool progress, and any black market opportunities." } }
						}
					} ) ),
					_ => id.InvalidParams( $"Prompt not found: {promptName}" )
				};
			}
			else if ( method == "tools/list" || method == "list_tools" )
			{
				McpBridge.McpBridgeAutoInit.EnsureCreated();
				var allTools = new List<Dictionary<string, object>>();
				foreach ( var t in _tools )
				{
					var entry = new Dictionary<string, object>
					{
						["name"] = t.Key,
						["description"] = t.Value.Description,
						["group"] = t.Value.Group ?? "Editor",
						["inputSchema"] = t.Value.InputSchema ?? new { type = "object", properties = new Dictionary<string, object>() }
					};
					if ( t.Value.Annotations != null )
						entry["annotations"] = t.Value.Annotations;
					allTools.Add( entry );
				}
				var gameToolsJson = await McpToolBridge.ListAllGameTools();
				if ( !string.IsNullOrEmpty( gameToolsJson ) )
				{
					using var gameDoc = JsonDocument.Parse( gameToolsJson );
					foreach ( var gt in gameDoc.RootElement.EnumerateArray() )
					{
						var entry = new Dictionary<string, object>
						{
							["name"] = gt.GetProperty( "name" ).GetString(),
							["description"] = gt.GetProperty( "description" ).GetString(),
							["group"] = gt.TryGetProperty( "group", out var g ) ? g.GetString() ?? "" : "",
							["inputSchema"] = gt.TryGetProperty( "inputSchema", out var s ) ? JsonSerializer.Deserialize<object>( s.GetRawText() ) : new { type = "object", properties = new Dictionary<string, object>() }
						};
						if ( gt.TryGetProperty( "annotations", out var ann ) && ann.ValueKind == JsonValueKind.Object )
							entry["annotations"] = ann;
						allTools.Add( entry );
					}
				}

				var query = "";
				var groupFilter = "";
				int page = 1, perPage = 100;
				if ( doc.RootElement.TryGetProperty( "params", out var lp ) )
				{
					if ( lp.TryGetProperty( "query", out var q ) ) query = q.GetString() ?? "";
					if ( lp.TryGetProperty( "group", out var g ) ) groupFilter = g.GetString() ?? "";
					if ( lp.TryGetProperty( "page", out var pn ) ) page = Math.Max( 1, pn.GetInt32() );
					if ( lp.TryGetProperty( "perPage", out var pp ) ) perPage = Math.Clamp( pp.GetInt32(), 1, 500 );
				}

				var filtered = allTools.AsEnumerable();
				if ( !string.IsNullOrEmpty( query ) )
				{
					var q = query;
					filtered = filtered.Where( t =>
						( (string)t["name"] ).IndexOf( q, StringComparison.OrdinalIgnoreCase ) >= 0 ||
						( (string)t["description"] ).IndexOf( q, StringComparison.OrdinalIgnoreCase ) >= 0 );
				}
				if ( !string.IsNullOrEmpty( groupFilter ) )
				{
					var g = groupFilter;
					filtered = filtered.Where( t => (string)t["group"] == g );
				}

				var total = filtered.Count();
				var paged = filtered.Skip( (page - 1) * perPage ).Take( perPage ).ToList();
				ctx.Response = id.ToOk( JsonSerializer.Serialize( new { tools = paged, total, page, perPage } ) );
			}
			else if ( method == "tools/call" )
			{
				var p = doc.RootElement.GetProperty( "params" );
				var toolName = p.GetProperty( "name" ).GetString();
				var args = p.TryGetProperty( "arguments", out var a ) ? a : default;

				// Extract progress token from params._meta.progressToken
				var progressToken = "";
				if ( p.TryGetProperty( "_meta", out var meta ) && meta.TryGetProperty( "progressToken", out var pt ) )
					progressToken = pt.GetString() ?? "";
				McpToolBridge.CurrentProgressToken.Value = progressToken;

				// Get session cancellation token
				var toolCts = CancellationTokenSource.CreateLinkedTokenSource( _cts.Token );
				if ( _sessions.TryGetValue( sid, out var sess ) )
				{
					toolCts = CancellationTokenSource.CreateLinkedTokenSource( _cts.Token, sess.ToolCts.Token );
				}

				if ( _tools.TryGetValue( toolName, out var toolDef ) )
				{
					var sw = Stopwatch.StartNew();
					try
					{
						var execTask = toolDef.Handler( args );
						if ( sess != null )
							sess.CurrentToolTask = execTask;
						var resultObj = await execTask;
						sw.Stop();
						if ( !string.IsNullOrEmpty( progressToken ) )
							ReportProgress( 100, 100, "Complete" );
						ctx.Response = id.ToOk( JsonSerializer.Serialize( new { content = new[] { new { type = "text", text = JsonSerializer.Serialize( resultObj ) } }, _meta = new { durationMs = Math.Round( sw.Elapsed.TotalMilliseconds, 1 ), toolName } } ) );
					}
					catch ( Exception ex )
					{
						sw.Stop();
						ctx.Response = id.ToolError( toolName, ex.Message );
					}
					finally
					{
						if ( sess != null )
							sess.CurrentToolTask = null;
					}
				}
				else
				{
					var argsJson = args.ValueKind == JsonValueKind.Undefined ? "{}" : args.GetRawText();
					if ( !string.IsNullOrEmpty( progressToken ) )
						ReportProgress( 0, 1, "Routing to bridge..." );
					var bridgeTask = McpToolBridge.RouteToolRequest( toolName, argsJson );
					if ( sess != null )
						sess.CurrentToolTask = bridgeTask;
					var bridgeResponse = await bridgeTask;
					if ( bridgeResponse != null )
					{
						try
						{
							using var brDoc = JsonDocument.Parse( bridgeResponse );
							var innerResult = brDoc.RootElement.GetProperty( "result" ).GetRawText();
							ctx.Response = id.ToOk( innerResult );
						}
						catch
						{
							ctx.Response = id.ToOk( JsonSerializer.Serialize( new { content = new[] { new { type = "text", text = bridgeResponse } } } ) );
						}
					}
					else
						ctx.Response = id.MethodNotFound( toolName );
					if ( !string.IsNullOrEmpty( progressToken ) )
						ReportProgress( 100, 100, "Complete" );
					if ( sess != null )
						sess.CurrentToolTask = null;
				}
			}
			else if ( _tools.TryGetValue( method, out var toolDef ) )
			{
				var sw = Stopwatch.StartNew();
				var args = doc.RootElement.TryGetProperty( "params", out var p ) ? p : default;
				try
				{
					var resultObj = await toolDef.Handler( args );
					sw.Stop();
					ctx.Response = id.ToOk( JsonSerializer.Serialize( new { content = new[] { new { type = "text", text = JsonSerializer.Serialize( resultObj ) } }, _meta = new { durationMs = Math.Round( sw.Elapsed.TotalMilliseconds, 1 ), toolName = method } } ) );
				}
				catch ( Exception ex )
				{
					sw.Stop();
					ctx.Response = id.ToolError( method, ex.Message );
				}
			}
			else if ( method == "completions/complete" )
			{
				var p = doc.RootElement.GetProperty( "params" );
				var arg = p.GetProperty( "argument" );
				var argName = arg.GetProperty( "name" ).GetString();
				var argValue = arg.TryGetProperty( "value", out var v ) ? v.GetString() ?? "" : "";
				var ref_ = p.TryGetProperty( "ref", out var r ) ? r.GetString() : "";

				var completions = new List<string>();
				if ( ref_ == "prefab" || argName.Contains( "prefab" ) || argName.Contains( "Prefab" ) )
				{
					try { completions.AddRange( FileSystem.Mounted.FindFile( ".", "*.prefab", true ).Select( f => f.Replace( '\\', '/' ) ).Where( f => f.StartsWith( argValue, StringComparison.OrdinalIgnoreCase ) ).Take( 10 ) ); } catch { }
				}
				else if ( ref_ == "tool" || argName == "name" || argName == "tool" )
				{
					completions.AddRange( _tools.Keys.Where( k => k.StartsWith( argValue, StringComparison.OrdinalIgnoreCase ) ).Take( 10 ) );
					try
					{
						var gameToolsJson = await McpToolBridge.ListAllGameTools();
						if ( !string.IsNullOrEmpty( gameToolsJson ) )
						{
							using var gameDoc = JsonDocument.Parse( gameToolsJson );
							foreach ( var gt in gameDoc.RootElement.EnumerateArray() )
							{
								var name = gt.GetProperty( "name" ).GetString();
								if ( name.StartsWith( argValue, StringComparison.OrdinalIgnoreCase ) && !_tools.ContainsKey( name ) )
									completions.Add( name );
							}
						}
					}
					catch { }
				}
				else if ( ref_ == "assetType" || argName.Contains( "asset" ) || argName.Contains( "type" ) )
				{
					completions.AddRange( new[] { "prefab", "model", "material", "sound", "texture", "animation", "all" }.Where( a => a.StartsWith( argValue, StringComparison.OrdinalIgnoreCase ) ) );
				}
				ctx.Response = id.ToOk( JsonSerializer.Serialize( new { completion = new { values = completions, total = completions.Count } } ) );
			}
			else
			{
				var args = doc.RootElement.TryGetProperty( "params", out var p ) ? p.GetRawText() : "{}";
				ctx.Response = await McpToolBridge.RouteToolRequest( method, args );
				if ( ctx.Response == null )
					ctx.Response = id.MethodNotFound( method );
			}
		}
		catch ( Exception e )
		{
			ctx.Response = id.InternalError( e.Message );
		}
	}

	// ── Helpers ────────────────────────────────────────────────────

	private static Dictionary<string, string> ParseHeaders( string[] lines )
	{
		var dict = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		for ( int i = 1; i < lines.Length; i++ )
		{
			var line = lines[i];
			if ( string.IsNullOrWhiteSpace( line ) ) break;
			var colon = line.IndexOf( ':' );
			if ( colon > 0 )
			{
				var key = line.Substring( 0, colon ).Trim();
				var val = line.Substring( colon + 1 ).Trim();
				dict[key] = val;
			}
		}
		return dict;
	}

	private static Dictionary<string, string> ParseQueryString( string path )
	{
		var dict = new Dictionary<string, string>();
		var qm = path.IndexOf( '?' );
		if ( qm < 0 ) return dict;
		var qs = path.Substring( qm + 1 );
		foreach ( var pair in qs.Split( '&' ) )
		{
			var eq = pair.IndexOf( '=' );
			if ( eq > 0 ) dict[pair.Substring( 0, eq )] = Uri.UnescapeDataString( pair.Substring( eq + 1 ) );
		}
		return dict;
	}

	private static object GetSceneState()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var gm = scene.GetAllComponents<BlackFridayGameManager>().FirstOrDefault();
		var quota = scene.GetAllComponents<QuotaManager>().FirstOrDefault();
		var alarm = scene.GetAllComponents<AlarmSystem>().FirstOrDefault();
		return new
		{
			phase = gm != null ? new { day = gm.CurrentDay, phase = gm.CurrentPhase.ToString(), timeRemaining = Math.Round( gm.PhaseTimeRemaining, 1 ), progress = Math.Round( gm.GetPhaseProgress(), 2 ) } : null,
			economy = quota != null ? new { personalCash = Math.Round( quota.MyPersonalCash, 1 ), personalQuota = Math.Round( quota.PersonalQuota, 1 ), poolCurrent = Math.Round( quota.SharedPoolCurrent, 1 ), poolTarget = Math.Round( quota.SharedPoolTarget, 1 ) } : null,
			alarm = alarm != null ? new { level = alarm.GetAlarmLevelName(), progress = Math.Round( alarm.AlarmProgress, 1 ) } : null
		};
	}

	private static List<string> ListAssetsByExt( params string[] exts )
	{
		var files = new List<string>();
		foreach ( var ext in exts )
		{
			try { foreach ( var f in FileSystem.Mounted.FindFile( ".", $"*{ext}", true ) ) files.Add( f.Replace( '\\', '/' ) ); } catch { }
		}
		return files;
	}

	private static string GetImageResource( string uri, int? id )
	{
		var path = uri.Replace( "sbox://image/", "" );
		try
		{
			if ( !FileSystem.Mounted.FileExists( path ) )
				return id.ResourceNotFound( path );

			var ext = System.IO.Path.GetExtension( path )?.ToLower();
			var mime = ext switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", ".bmp" => "image/bmp", _ => "application/octet-stream" };
			var bytes = FileSystem.Mounted.ReadAllBytes( path ).ToArray();
			var b64 = Convert.ToBase64String( bytes );
			return id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = mime, blob = $"data:{mime};base64,{b64}" } } } ) );
		}
		catch { return id.InvalidParams( $"Cannot read: {path}" ); }
	}

	private static string GetFilePreview( string uri, int? id, string prefix )
	{
		var path = uri.Replace( $"sbox://{prefix}/", "" );
		try
		{
			if ( !FileSystem.Mounted.FileExists( path ) )
				return id.ResourceNotFound( path );

			var text = FileSystem.Mounted.ReadAllText( path );
			var lines = text.Split( '\n' ).Length;
			var ext = System.IO.Path.GetExtension( path )?.ToLower();
			return id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( new { path, sizeBytes = text.Length, lineCount = lines, extension = ext } ) } } } ) );
		}
		catch { return id.InvalidParams( $"Cannot read: {path}" ); }
	}

	private static string GetResourceHash( string uri )
	{
		try
		{
			if ( uri == "sbox://prefabs" )
			{
				var files = FileSystem.Mounted.FindFile( ".", "*.prefab", true );
				return string.Join( "|", files );
			}
			if ( uri == "sbox://materials" )
			{
				var files = FileSystem.Mounted.FindFile( ".", "*.vmat", true );
				return string.Join( "|", files );
			}
			if ( uri == "sbox://sounds" )
			{
				var files = FileSystem.Mounted.FindFile( ".", "*.vsnd", true );
				return string.Join( "|", files );
			}
			if ( uri == "sbox://textures" )
			{
				var files = FileSystem.Mounted.FindFile( ".", "*.vtex", true );
				return string.Join( "|", files );
			}
			if ( uri == "sbox://models" )
			{
				var files = FileSystem.Mounted.FindFile( ".", "*.vmdl", true );
				return string.Join( "|", files );
			}
			if ( uri == "sbox://maps" )
			{
				var files = FileSystem.Mounted.FindFile( ".", "*.sbox", true );
				return string.Join( "|", files );
			}
			if ( uri == "sbox://entities" )
			{
				return GetSceneEntitiesHash();
			}
			if ( uri == "sbox://console/logs" )
			{
				return McpLogBridge.Count.ToString();
			}
			if ( uri.StartsWith( "sbox://image/" ) || uri.StartsWith( "sbox://file/" ) )
			{
				var path = uri.Replace( "sbox://image/", "" ).Replace( "sbox://file/", "" );
				return FileSystem.Mounted.FileExists( path ) ? path : null;
			}
		}
		catch { }
		return null;
	}

	private static string GetResourceListHash()
	{
		try
		{
			var parts = new List<string>();
			try { parts.Add( string.Join( "|", FileSystem.Mounted.FindFile( ".", "*.prefab", true ) ) ); } catch { parts.Add( "" ); }
			try { parts.Add( string.Join( "|", FileSystem.Mounted.FindFile( ".", "*.vmat", true ) ) ); } catch { parts.Add( "" ); }
			try { parts.Add( string.Join( "|", FileSystem.Mounted.FindFile( ".", "*.vsnd", true ) ) ); } catch { parts.Add( "" ); }
			try { parts.Add( string.Join( "|", FileSystem.Mounted.FindFile( ".", "*.vtex", true ) ) ); } catch { parts.Add( "" ); }
			try { parts.Add( string.Join( "|", FileSystem.Mounted.FindFile( ".", "*.vmdl", true ) ) ); } catch { parts.Add( "" ); }
			try { parts.Add( string.Join( "|", FileSystem.Mounted.FindFile( ".", "*.sbox", true ) ) ); } catch { parts.Add( "" ); }
			return string.Join( "||", parts );
		}
		catch { return null; }
	}

	private static object GetSceneEntities()
	{
		try
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };
			var entities = new List<object>();
			foreach ( var go in scene.GetAllObjects( true ) )
			{
				try
				{
					var components = new List<string>();
					foreach ( var c in go.Components.GetAll<Component>() )
					{
						try { components.Add( c.GetType().Name ); } catch { components.Add( "?" ); }
					}
					entities.Add( new { id = go.Id, name = go.Name ?? "", position = new { x = go.WorldPosition.x, y = go.WorldPosition.y, z = go.WorldPosition.z }, components, childCount = go.Children.Count() } );
				}
				catch { }
			}
			return new { count = entities.Count, entities };
		}
		catch ( Exception e ) { return new { error = e.Message }; }
	}

	private static string GetSceneEntitiesHash()
	{
		try
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return "";
			var ids = new List<string>();
			foreach ( var go in scene.GetAllObjects( true ) )
			{
				try
				{
					var compNames = new List<string>();
					foreach ( var c in go.Components.GetAll<Component>() )
					{
						try { compNames.Add( c.GetType().Name ); } catch { compNames.Add( "?" ); }
					}
					ids.Add( $"{go.Id}:{string.Join( ",", compNames )}" );
				}
				catch { }
			}
			return string.Join( "|", ids );
		}
		catch { return ""; }
	}

	private static async Task PollStateAsync( CancellationToken ct )
	{
		while ( !ct.IsCancellationRequested )
		{
			try
			{
				await GameTask.Delay( 5000 );
				if ( ct.IsCancellationRequested ) break;

				// Check all subscribed resources for changes
				foreach ( var uri in _subscriptions.Keys )
				{
					var hash = GetResourceHash( uri );
					if ( hash == null ) continue;
					if ( _resourceHashes.TryGetValue( uri, out var oldHash ) && oldHash == hash ) continue;
					_resourceHashes[uri] = hash;
					await BroadcastEventAsync( "notifications/resources/updated", JsonSerializer.Serialize( new { uri } ), uri );
				}

				// Check if the resource list itself changed (prefabs/materials/sounds added/removed)
				var listHash = GetResourceListHash();
				if ( listHash != null && listHash != _lastResourceListHash )
				{
					_lastResourceListHash = listHash;
					await BroadcastEventAsync( "notifications/resources/list_changed", "{}" );
				}

				// Legacy scene/state change + event detection (keeps old code working)
				var snapshot = await McpToolBridge.GetAnyStateSnapshot();
				if ( snapshot == null ) continue;
				if ( snapshot == _lastStateSnapshot ) continue;
				_lastStateSnapshot = snapshot;

				if ( !_subscriptions.ContainsKey( "sbox://scene/state" ) )
				{
					await BroadcastEventAsync( "notifications/resources/updated", JsonSerializer.Serialize( new { uri = "sbox://scene/state" } ), "sbox://scene/state" );
					await BroadcastEventAsync( "notification", snapshot, "sbox://scene/state" );
				}

				// Detect events from snapshot
				if ( !McpToolBridge.HasEventSubscribers( "phase_change" ) && !McpToolBridge.HasEventSubscribers( "day_change" ) && !McpToolBridge.HasEventSubscribers( "alarm" ) ) continue;
				try
				{
					using var doc = JsonDocument.Parse( snapshot );
					var root = doc.RootElement;

					var phase = root.TryGetProperty( "phase", out var p ) ? p.GetRawText() : "";
					var day = root.TryGetProperty( "day", out var d ) ? d.GetRawText() : "";
					var alarm = root.TryGetProperty( "alarmLevel", out var a ) ? a.GetRawText() : "";

					if ( _lastPhase != "" && phase != "" && phase != _lastPhase && McpToolBridge.HasEventSubscribers( "phase_change" ) )
						await BroadcastEventAsync( "event", JsonSerializer.Serialize( new { type = "phase_change", from = _lastPhase, to = phase } ) );
					if ( _lastDay != "" && day != "" && day != _lastDay && McpToolBridge.HasEventSubscribers( "day_change" ) )
						await BroadcastEventAsync( "event", JsonSerializer.Serialize( new { type = "day_change", from = _lastDay, to = day } ) );
					if ( _lastAlarm != "" && alarm != "" && alarm != _lastAlarm && McpToolBridge.HasEventSubscribers( "alarm" ) )
						await BroadcastEventAsync( "event", JsonSerializer.Serialize( new { type = "alarm", from = _lastAlarm, to = alarm } ) );

					_lastPhase = phase;
					_lastDay = day;
					_lastAlarm = alarm;
				}
				catch ( Exception e )
				{
					Log.Warning( $"[MCP] Event detection error: {e.GetType().Name} {e.Message}" );
				}
			}
			catch ( OperationCanceledException ) { break; }
			catch ( Exception e )
			{
				Log.Warning( $"[MCP] PollStateAsync error: {e.GetType().Name} {e.Message}" );
				await GameTask.Delay( 1000 );
			}
		}
	}

	private static async Task CleanupSessionsAsync()
	{
		while ( !_cts.IsCancellationRequested )
		{
			try { await GameTask.Delay( 30000 ); }
			catch { break; }
			if ( _cts.IsCancellationRequested ) break;

			foreach ( var kv in _sessions.ToArray() )
			{
				if ( kv.Value.Cts.IsCancellationRequested )
					_sessions.TryRemove( kv.Key, out _ );
			}
		}
	}
}
