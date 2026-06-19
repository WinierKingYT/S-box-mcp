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
	private record SseSession( NetworkStream Stream, CancellationTokenSource Cts, System.Threading.SemaphoreSlim WriteLock );
	private static readonly ConcurrentDictionary<string, SseSession> _sessions = new();
	private record ToolDef( Func<JsonElement, Task<object>> Handler, string Description, string Group = "Editor", object InputSchema = null );
	private static readonly ConcurrentDictionary<string, ToolDef> _tools = new();
	private static long _sseEventId;
	private static CancellationTokenSource _statePollCts;
	private static string _lastStateSnapshot = "";
	private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _subscriptions = new();
	private static string _lastPhase = "", _lastDay = "", _lastAlarm = "";
	private static Func<McpContext, Task> _pipeline;
	private static volatile string _logLevel = "info";
	private static readonly DateTime _startTime = DateTime.UtcNow;

	public static void RegisterTool( string name, string description, Func<JsonElement, object> handler, object inputSchema = null, string group = "Editor" )
	{
		_tools[name] = new ToolDef( args => Task.FromResult( handler( args ) ), description, group, inputSchema );
		McpToolBridge.RegisterGlobalToolName( name );
		Log.Info( $"[MCP] Tool registered: {name} \u2014 {description}" );
	}

	public static void RegisterToolAsync( string name, string description, Func<JsonElement, Task<object>> handler, object inputSchema = null, string group = "Editor" )
	{
		_tools[name] = new ToolDef( handler, description, group, inputSchema );
		McpToolBridge.RegisterGlobalToolName( name );
		Log.Info( $"[MCP] Async tool registered: {name} \u2014 {description}" );
	}

	public static async Task BroadcastLogAsync( string level, string logger, string message, JsonElement? data = null )
	{
		if ( !ShouldLog( level ) ) return;
		var eid = Interlocked.Increment( ref _sseEventId );
		var logEntry = new Dictionary<string, object>
		{
			["level"] = level,
			["logger"] = logger,
			["message"] = message
		};
		if ( data.HasValue ) logEntry["data"] = data.Value;
		var payload = JsonSerializer.Serialize( logEntry );
		var msg = $"id: {eid}\nevent: notifications/message\ndata: {payload}\n\n";
		var buf = Encoding.UTF8.GetBytes( msg );
		foreach ( var sid in _sessions.Keys.ToArray() )
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

	private static bool ShouldLog( string level )
	{
		var levels = new[] { "debug", "info", "notice", "warning", "error", "critical" };
		var currentIdx = Array.IndexOf( levels, _logLevel );
		var msgIdx = Array.IndexOf( levels, level );
		return currentIdx >= 0 && msgIdx >= currentIdx;
	}

	public static async Task BroadcastEventAsync( string eventType, string data, string resourceUri = null )
	{
		var eid = Interlocked.Increment( ref _sseEventId );
		var msg = $"id: {eid}\nevent: {eventType}\ndata: {data}\n\n";
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

		RegisterTool( "list_tools", "List all available tools", _ =>
		{
			return _tools.Select( t => new { name = t.Key, group = t.Value.Group, description = t.Value.Description } ).ToList();
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() } );
		RegisterTool( "list_objects", "List GameObjects in the scene (max 50)", _ =>
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			return scene.GetAllObjects( true ).Take( 50 ).Select( g => new { g.Name, g.Id } ).ToList();
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() } );
		RegisterTool( "create_object", "Create a new GameObject", args =>
		{
			var name = args.GetProperty( "name" ).GetString();
			var pos = args.TryGetProperty( "position", out var p )
				? JsonSerializer.Deserialize<Vector3>( p.GetRawText() )
				: Vector3.Zero;
			var go = new GameObject( true, name );
			go.WorldPosition = pos;
			return new { id = go.Id.ToString(), name };
		}, new { type = "object", properties = new { name = new { type = "string" }, position = new { type = "string", description = "x,y,z" } }, required = new[] { "name" } } );

		RegisterTool( "sbox_create_gameobject", "Create a new GameObject in the scene", args =>
		{
			var name = args.GetProperty( "name" ).GetString();
			var x = args.TryGetProperty( "x", out var xp ) ? xp.GetSingle() : 0f;
			var y = args.TryGetProperty( "y", out var yp ) ? yp.GetSingle() : 0f;
			var z = args.TryGetProperty( "z", out var zp ) ? zp.GetSingle() : 0f;
			var go = new GameObject( true, name );
			go.WorldPosition = new Vector3( x, y, z );
			return new { id = go.Id.ToString(), name, position = new { x, y, z } };
		}, new { type = "object", properties = new { name = new { type = "string", description = "Display name" }, x = new { type = "number", description = "X position" }, y = new { type = "number", description = "Y position" }, z = new { type = "number", description = "Z position" } }, required = new[] { "name" } } );

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
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject" } }, required = new[] { "id" } } );

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
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject" }, x = new { type = "number", description = "New X position" }, y = new { type = "number", description = "New Y position" }, z = new { type = "number", description = "New Z position" }, pitch = new { type = "number" }, yaw = new { type = "number" }, roll = new { type = "number" }, scale = new { type = "number" } }, required = new[] { "id" } } );

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
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject" }, type = new { type = "string", description = "Component type name (e.g. Sandbox.ModelComponent)" } }, required = new[] { "id", "type" } } );

		RegisterTool( "sbox_mcp_clients", "List all connected SSE clients and their stats", _ =>
		{
			var clients = _sessions.Select( kv => new { sessionId = kv.Key, connected = true, pendingRequests = 0 } ).ToList();
			return new { count = clients.Count, clients };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() } );

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
		}, new { type = "object", properties = new { url = new { type = "string", description = "Target URL" }, headers = new { type = "string", description = "Optional JSON object of headers" } }, required = new[] { "url" } } );

		RegisterTool( "sbox_mcp_bridge_status", "Show connected game bridges and their health", _ =>
		{
			var bridges = McpToolBridge.GetBridgeStatus();
			return new { count = bridges.Length, bridges = bridges.Select( b => new { b.name, errorCount = b.errors } ) };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() } );

		RegisterTool( "get_game_state", "Get current day/phase/time", _ =>
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			var gm = scene.GetAllComponents<BlackFridayGameManager>().FirstOrDefault();
			if ( gm == null ) return "No game manager found";
			return new { day = gm.CurrentDay, phase = gm.CurrentPhase.ToString(), timeLeft = gm.PhaseTimeRemaining };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() } );

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
		}, new { type = "object", properties = new { url = new { type = "string", description = "Target URL" }, body = new { type = "string", description = "JSON body" }, headers = new { type = "string", description = "Optional JSON object of headers" } }, required = new[] { "url" } } );
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
					headerEnd = SearchBytes( headerBuf.ToArray(), "\r\n\r\n" );
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
						gameBridges = bridges.Select( b => new { b.name, errorCount = b.errors } )
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
					await HandleSseAsync( stream );
					return;
				}

				if ( method == "POST" && (path == "/mcp" || path == "/jsonrpc") )
				{
					var body = await ReadBodyAsync( stream, headerBuf, headerEnd, headers, buffer );
					if ( body == null ) return;
					await HandleStreamableHttpAsync( stream, body );
					return;
				}

				if ( path.StartsWith( "/messages" ) && method == "POST" )
				{
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

	private static int SearchBytes( byte[] data, string pattern )
	{
		var pat = Encoding.UTF8.GetBytes( pattern );
		for ( int i = 0; i <= data.Length - pat.Length; i++ )
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

		var ctx = new McpContext { Body = body, SessionId = "http" };

		// Parse id from body for middleware error responses
		try
		{
			using var doc = JsonDocument.Parse( body );
			if ( doc.RootElement.TryGetProperty( "id", out var idProp ) )
				ctx.Id = idProp.GetInt32();
		}
		catch { }

		await _pipeline( ctx );

		if ( ctx.Response == null || ctx.Response == "" )
		{
			await WriteResponseAsync( stream, 202, "application/json", "{}" );
			return;
		}

		await WriteResponseAsync( stream, 200, "application/json", ctx.Response );
	}

	// ── SSE handler ────────────────────────────────────────────────

	private static async Task HandleSseAsync( NetworkStream stream )
	{
		var sid = Guid.NewGuid().ToString();
		var cts = new CancellationTokenSource();
		_sessions[sid] = new SseSession( stream, cts, new System.Threading.SemaphoreSlim( 1, 1 ) );

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
		await GameTask.MainThread();

		var ctx = new McpContext { Body = body, SessionId = sid };
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
						tools = new { },
						resources = new { templates = new { } },
						prompts = new { },
						logging = new { },
						roots = new { }
					},
					serverInfo = new { name = "sbox-mcp", version = "1.0.0" }
				} ) );
			}
			else if ( method == "notifications/initialized" || method == "notifications/cancelled" )
			{
				ctx.Response = "";
				ctx.Items["skipSse"] = "true";
				return;
			}
			else if ( method == "ping" )
			{
				ctx.Response = id.ToOk( "\"pong\"" );
			}
			else if ( method == "logging/setLevel" )
			{
				var p = doc.RootElement.GetProperty( "params" );
				_logLevel = p.TryGetProperty( "level", out var l ) ? l.GetString() ?? "info" : "info";
				ctx.Response = id.ToOk( "{}" );
			}
			else if ( method == "resources/list" )
			{
				var resources = new List<object>
				{
					new { uri = "sbox://scene/state", name = "Game State", mimeType = "application/json", description = "Current phase, day, economy, alarm" },
					new { uri = "sbox://prefabs", name = "Prefabs", mimeType = "application/json", description = "All available prefab files" },
					new { uri = "sbox://materials", name = "Materials", mimeType = "application/json", description = "All available material files" },
					new { uri = "sbox://sounds", name = "Sounds", mimeType = "application/json", description = "All available sound files" },
		new { uri = "sbox://console/logs", name = "Console Logs", mimeType = "application/json", description = "Recent engine log entries" },
		new { uri = "sbox://image/{path}", name = "Image Preview", mimeType = "image/*", description = "Base64-encoded image. Use sbox://image/path/to/file.png" },
		new { uri = "sbox://file/{path}", name = "File Metadata", mimeType = "application/json", description = "File size/line count. Use sbox://file/path/to/file.ext" }
				};
				ctx.Response = id.ToOk( JsonSerializer.Serialize( new { resources } ) );
			}
			else if ( method == "resources/templates" )
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
					"sbox://prefabs" => id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( ListAssetsByExt( ".prefab" ) ) } } } ) ),
					"sbox://materials" => id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( ListAssetsByExt( ".vmat" ) ) } } } ) ),
					"sbox://sounds" => id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( ListAssetsByExt( ".vsnd", ".vsndevts", ".wav", ".mp3" ) ) } } } ) ),
					"sbox://console/logs" => id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( McpLogBridge.GetRecent( 100 ) ) } } } ) ),
					string s when s.StartsWith( "sbox://image/" ) => GetImageResource( uri, id ),
					string s when s.StartsWith( "sbox://file/" ) => GetFilePreview( uri, id, "file" ),
					_ => id.ToError( -32602, $"Resource not found: {uri}" )
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
				catch { }
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
					_ => id.ToError( -32602, $"Prompt not found: {promptName}" )
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

				if ( _tools.TryGetValue( toolName, out var toolDef ) )
				{
					var sw = Stopwatch.StartNew();
					try
					{
						var resultObj = await toolDef.Handler( args );
						sw.Stop();
						ctx.Response = id.ToOk( JsonSerializer.Serialize( new { content = new[] { new { type = "text", text = JsonSerializer.Serialize( resultObj ) } }, _meta = new { durationMs = Math.Round( sw.Elapsed.TotalMilliseconds, 1 ), toolName } } ) );
					}
					catch ( Exception ex )
					{
						sw.Stop();
						ctx.Response = id.ToError( -32603, $"Tool '{toolName}' failed: {ex.Message}" );
					}
				}
				else
				{
					var argsJson = args.ValueKind == JsonValueKind.Undefined ? "{}" : args.GetRawText();
					var bridgeResponse = await McpToolBridge.RouteToolRequest( toolName, argsJson );
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
						ctx.Response = id.ToError( -32601, $"Tool '{toolName}' not found" );
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
					ctx.Response = id.ToError( -32603, $"Tool '{method}' failed: {ex.Message}" );
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
					ctx.Response = id.ToError( -32601, $"Method '{method}' not found" );
			}
		}
		catch ( Exception e )
		{
			ctx.Response = id.ToError( -32603, e.Message );
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
				return id.ToError( -32602, $"File not found: {path}" );

			var ext = System.IO.Path.GetExtension( path )?.ToLower();
			var mime = ext switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", ".bmp" => "image/bmp", _ => "application/octet-stream" };
			var bytes = FileSystem.Mounted.ReadAllBytes( path ).ToArray();
			var b64 = Convert.ToBase64String( bytes );
			return id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = mime, blob = $"data:{mime};base64,{b64}" } } } ) );
		}
		catch { return id.ToError( -32602, $"Cannot read: {path}" ); }
	}

	private static string GetFilePreview( string uri, int? id, string prefix )
	{
		var path = uri.Replace( $"sbox://{prefix}/", "" );
		try
		{
			if ( !FileSystem.Mounted.FileExists( path ) )
				return id.ToError( -32602, $"File not found: {path}" );

			var text = FileSystem.Mounted.ReadAllText( path );
			var lines = text.Split( '\n' ).Length;
			var ext = System.IO.Path.GetExtension( path )?.ToLower();
			return id.ToOk( JsonSerializer.Serialize( new { contents = new[] { new { uri, mimeType = "application/json", text = JsonSerializer.Serialize( new { path, sizeBytes = text.Length, lineCount = lines, extension = ext } ) } } } ) );
		}
		catch { return id.ToError( -32602, $"Cannot read: {path}" ); }
	}

	private static async Task PollStateAsync( CancellationToken ct )
	{
		while ( !ct.IsCancellationRequested )
		{
			try
			{
				await GameTask.Delay( 5000 );
				if ( ct.IsCancellationRequested ) break;

				var snapshot = await McpToolBridge.GetAnyStateSnapshot();
				if ( snapshot == null ) continue;
				if ( snapshot == _lastStateSnapshot ) continue;
				_lastStateSnapshot = snapshot;

				await BroadcastEventAsync( "notifications/resources/updated", JsonSerializer.Serialize( new { uri = "sbox://scene/state" } ), "sbox://scene/state" );
				await BroadcastEventAsync( "notification", snapshot, "sbox://scene/state" );

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
				catch { }
			}
			catch ( OperationCanceledException ) { break; }
			catch
			{
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
