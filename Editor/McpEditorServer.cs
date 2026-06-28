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
	public static string CurrentFocus { get; set; } = "all";

	public static IEnumerable<Dictionary<string, object>> ApplyFocusFilter( IEnumerable<Dictionary<string, object>> tools )
	{
		var focus = (CurrentFocus ?? "all").ToLowerInvariant();
		if ( focus == "all" ) return tools;

		return tools.Where( t =>
		{
			var name = ( (string)t["name"] ).ToLowerInvariant();
			var group = ( (string)t["group"] ?? "" ).ToLowerInvariant();

			if ( name == "list_tools" || name == "sbox_mcp_clients" || name == "sbox_mcp_bridge_status" ||
				 name == "get_game_state" || name == "sbox_replay_history" || name == "sbox_replay_analytics" ||
				 name == "sbox_undo" || name == "sbox_redo" || name == "sbox_undo_history" ||
				 name == "sbox_batch" || name == "sbox_set_context_focus" )
			{
				return true;
			}

			if ( focus == "ui" || focus == "widget" )
			{
				return name.Contains( "_ui_" ) || group == "ui" || group == "widget";
			}

			if ( focus == "code" || focus == "script" )
			{
				return name.Contains( "_file" ) || name.Contains( "_script" ) || name.Contains( "_project" ) ||
					   name.Contains( "_code" ) || name.Contains( "_errors" ) || group == "code" || group == "script";
			}

			if ( focus == "scene" || focus == "object" )
			{
				return name.Contains( "_scene" ) || name.Contains( "_gameobject" ) || name.Contains( "_object" ) ||
					   name.Contains( "_transform" ) || name.Contains( "_component" ) || name.Contains( "_prefab" ) ||
					   name.Contains( "_asset" ) || name.Contains( "_spatial" ) || name.Contains( "_screenshot" ) ||
					   group == "scene" || group == "asset" || group == "core";
			}

			if ( focus == "physics" )
			{
				return name.Contains( "_physics" ) || name.Contains( "_impulse" ) || name.Contains( "_raycast" ) ||
					   name.Contains( "_force" ) || name.Contains( "_torque" ) || name.Contains( "_gravity" ) ||
					   group == "physics";
			}

			return true;
		} );
	}

	public static void TriggerToolsChanged()
	{
		_ = BroadcastEventAsync( "notifications/tools/list_changed", "{}" );
	}

	private static readonly JsonSerializerOptions IndentedJsonOpts = new() { WriteIndented = true };
	private const int DefaultPort = 29016;
	private const string DefaultApiKey = "sbox-ai-2026";
	internal static int _port = DefaultPort;
	internal static string _apiKey = DefaultApiKey;
	private static TcpListener _listener;
	private static CancellationTokenSource _cts;
	internal class SseSession
	{
		public NetworkStream Stream { get; }
		public CancellationTokenSource Cts { get; }
		public System.Threading.SemaphoreSlim WriteLock { get; }
		public string RemoteEndPoint { get; }
		public DateTime ConnectedAt { get; }
		public string LogLevel { get; set; }
		public CancellationTokenSource ToolCts { get; set; } = new();
		public Task CurrentToolTask { get; set; }
		public McpBridge.Middleware.SlidingWindowRateLimiter RateLimiter { get; }
		public long RequestCount;

		public SseSession( NetworkStream stream, CancellationTokenSource cts, System.Threading.SemaphoreSlim writeLock, string remoteEndPoint, DateTime connectedAt, string logLevel = "info" )
		{
			Stream = stream;
			Cts = cts;
			WriteLock = writeLock;
			RemoteEndPoint = remoteEndPoint;
			ConnectedAt = connectedAt;
			LogLevel = logLevel;
			RateLimiter = new McpBridge.Middleware.SlidingWindowRateLimiter( DefaultRateLimit, 60000 );
		}
	}
	private static readonly byte[] _headerEndPattern = new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };
	internal static readonly ConcurrentDictionary<string, SseSession> _sessions = new();
	internal record ToolDef( Func<JsonElement, Task<object>> Handler, string Description, string Group = "Editor", object InputSchema = null, object Annotations = null );
	internal static readonly ConcurrentDictionary<string, ToolDef> _tools = new();
	private static long _sseEventId;
	private static CancellationTokenSource _statePollCts;
	private static string _lastStateSnapshot = "";
	private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _subscriptions = new();
	private static string _lastPhase = "", _lastDay = "", _lastAlarm = "";
	private static readonly ConcurrentDictionary<string, string> _resourceHashes = new();
	private static string _lastResourceListHash = "";
	internal static Func<McpContext, Task> _pipeline;
	private static volatile string _defaultLogLevel = "info";
	internal static readonly DateTime _startTime = DateTime.UtcNow;

	// ── Tool result cache (TTL-based) ─────────────────────────────────────
	internal record CacheEntry( object Result, DateTime ExpiresAt );
	internal static readonly ConcurrentDictionary<string, CacheEntry> _toolCache = new();
	private static readonly HashSet<string> _cacheableTools = new()
	{
		"sbox_list_component_types", "sbox_get_scene_hierarchy", "sbox_scene_list"
	};
	private const double DefaultCacheTtlSeconds = 2.0;

	// ── Property watch registry ───────────────────────────────────────────
	internal record WatchEntry( string GameObjectId, string ComponentType, string PropertyName );
	internal static readonly ConcurrentDictionary<string, (WatchEntry Watch, string LastValue)> _watchedProperties = new();

	// ── Per-session rate limit (requests / minute) ────────────────────────
	private const int DefaultRateLimit = 120;

	public static bool IsRunning => _listener != null;
	public static int Port => _port;
	public static string ApiKey => _apiKey;
	public static int SessionCount => _sessions.Count;
	public static IEnumerable<string> ActiveSessions => _sessions.Values.Select( s => $"{s.RemoteEndPoint} (Connected: {s.ConnectedAt.ToLocalTime():HH:mm:ss})" );

	private static readonly ConcurrentQueue<string> _logQueue = new();
	public static void AddServerLog( string msg )
	{
		_logQueue.Enqueue( $"[{DateTime.Now:HH:mm:ss}] {msg}" );
		while ( _logQueue.Count > 100 ) _logQueue.TryDequeue( out _ );
	}
	public static IEnumerable<string> GetServerLogs() => _logQueue.ToArray();

	// ── Resource Explorer API ──────────────────────────────────────────────
	public static List<(string uri, string name, string mimeType, string description, bool isTemplate)> GetResourceDefinitions()
	{
		return new()
		{
			("sbox://scene/state",   "Game State",     "application/json", "Current phase, day, economy, alarm",             false),
			("sbox://entities",      "Scene Entities", "application/json", "All GameObjects with positions",                 false),
			("sbox://prefabs",       "Prefabs",        "application/json", "All available .prefab files",                   false),
			("sbox://materials",     "Materials",      "application/json", "All available .vmat files",                     false),
			("sbox://textures",      "Textures",       "application/json", "All available .vtex files",                     false),
			("sbox://models",        "Models",         "application/json", "All available .vmdl files",                     false),
			("sbox://sounds",        "Sounds",         "application/json", "All available sound files",                     false),
			("sbox://maps",          "Maps",           "application/json", "All available .sbox map files",                 false),
			("sbox://console/logs",  "Console Logs",   "application/json", "Recent engine log entries",                     false),
			("sbox://file/{path}",   "File Preview",   "application/json", "File metadata. Replace {path} with file path.", true),
		};
	}

	public static string ReadResourceContent( string uri )
	{
		try
		{
			return uri switch
			{
				"sbox://scene/state"  => JsonSerializer.Serialize( GetSceneState(),    IndentedJsonOpts ),
				"sbox://entities"     => JsonSerializer.Serialize( GetSceneEntities(), IndentedJsonOpts ),
				"sbox://prefabs"      => JsonSerializer.Serialize( ListAssetsByExt( ".prefab" ),                             IndentedJsonOpts ),
				"sbox://materials"    => JsonSerializer.Serialize( ListAssetsByExt( ".vmat" ),                               IndentedJsonOpts ),
				"sbox://textures"     => JsonSerializer.Serialize( ListAssetsByExt( ".vtex" ),                               IndentedJsonOpts ),
				"sbox://models"       => JsonSerializer.Serialize( ListAssetsByExt( ".vmdl" ),                               IndentedJsonOpts ),
				"sbox://sounds"       => JsonSerializer.Serialize( ListAssetsByExt( ".vsnd", ".vsndevts", ".wav", ".mp3" ),  IndentedJsonOpts ),
				"sbox://maps"         => JsonSerializer.Serialize( ListAssetsByExt( ".sbox" ),                               IndentedJsonOpts ),
				"sbox://console/logs" => JsonSerializer.Serialize( McpLogBridge.GetRecent( 100 ),                            IndentedJsonOpts ),
				_ when uri.StartsWith( "sbox://file/" ) => ReadFilePreviewContent( uri.Substring( "sbox://file/".Length ) ),
				_ => $"// Unknown resource URI: {uri}"
			};
		}
		catch ( Exception ex )
		{
			return $"// Error reading resource: {ex.Message}";
		}
	}

	private static string ReadFilePreviewContent( string path )
	{
		try
		{
			var absPath = path;
			if ( !System.IO.Path.IsPathRooted( path ) )
			{
				absPath = System.IO.Path.Combine( FileSystem.Mounted.GetFullPath( "." ), path.Replace( "/", System.IO.Path.DirectorySeparatorChar.ToString() ) );
			}

			if ( !System.IO.File.Exists( absPath ) )
				return $"// File not found: {path}";

			var fileInfo = new System.IO.FileInfo( absPath );
			var fileLen = fileInfo.Length;
			var ext = fileInfo.Extension.ToLower();

			var binaryExts = new HashSet<string>
			{
				".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".vtex", ".vmdl", ".vsnd", ".wav", ".mp3",
				".dll", ".pdb", ".exe", ".fbx", ".obj", ".glb", ".gltf", ".zip", ".tar", ".gz", ".rar", ".7z"
			};

			if ( binaryExts.Contains( ext ) )
			{
				return $"// Binary File: {path}\n// Extension: {ext}\n// Size: {fileLen:N0} bytes\n\n[Binary content preview is not available]";
			}

			if ( fileLen > 2 * 1024 * 1024 )
			{
				var previewLines = new List<string>();
				using ( var stream = System.IO.File.OpenRead( absPath ) )
				using ( var reader = new System.IO.StreamReader( stream, Encoding.UTF8 ) )
				{
					string line;
					while ( previewLines.Count < 100 && (line = reader.ReadLine()) != null )
					{
						previewLines.Add( line );
					}
				}
				var preview = string.Join( "\n", previewLines );
				return $"// Large File: {path} ({fileLen:N0} bytes - preview limited to first 100 lines)\n\n{preview}";
			}

			var text = System.IO.File.ReadAllText( absPath );
			var lines = text.Split( '\n' );
			var textPreview = string.Join( "\n", lines.Take( 100 ) );
			return $"// File: {path}  ({lines.Length} lines, {text.Length} bytes)\n\n{textPreview}";
		}
		catch ( Exception ex )
		{
			return $"// Cannot read file: {ex.Message}";
		}
	}

	// ── Traffic & Replay Inspector API ────────────────────────────────────
	public static List<McpBridge.ReplayRecord> GetReplayHistory( int count = 50 ) => McpBridge.McpReplay.GetHistory( count );
	public static void ClearReplayHistory() => McpBridge.McpReplay.Clear();
	public static bool ExportReplayScript( string path ) => McpBridge.McpReplay.ExportAsScript( path );
	public static Dictionary<string, object> GetReplayAnalytics() => McpBridge.McpReplay.GetAnalytics();

	public static async Task<object> ExecuteRegisteredTool( string name, string jsonArgs )
	{
		if ( !_tools.TryGetValue( name, out var toolDef ) )
		{
			var resStr = await McpToolBridge.RouteToolRequest( name, jsonArgs );
			if ( resStr != null ) return resStr;
			throw new Exception( $"Tool '{name}' not found." );
		}
		
		using var doc = JsonDocument.Parse( string.IsNullOrEmpty( jsonArgs ) ? "{}" : jsonArgs );
		return await toolDef.Handler( doc.RootElement );
	}

	public static async Task<Dictionary<string, (string description, string group, string schema)>> GetToolDescriptionsAsync()
	{
		var dict = new Dictionary<string, (string description, string group, string schema)>();
		foreach ( var t in _tools )
		{
			dict[t.Key] = (t.Value.Description ?? "", t.Value.Group ?? "Editor", t.Value.InputSchema != null ? JsonSerializer.Serialize( t.Value.InputSchema ) : "{}");
		}

		try
		{
			var gameToolsJson = await McpToolBridge.ListAllGameTools();
			if ( !string.IsNullOrEmpty( gameToolsJson ) )
			{
				using var gameDoc = JsonDocument.Parse( gameToolsJson );
				foreach ( var gt in gameDoc.RootElement.EnumerateArray() )
				{
					var name = gt.GetProperty( "name" ).GetString();
					if ( !string.IsNullOrEmpty( name ) )
					{
						var desc = gt.TryGetProperty( "description", out var d ) ? d.GetString() ?? "" : "";
						var group = gt.TryGetProperty( "group", out var g ) ? g.GetString() ?? "" : "Game";
						var schema = gt.TryGetProperty( "inputSchema", out var sch ) ? JsonSerializer.Serialize( sch ) : "{}";
						dict[name] = (desc, group, schema);
					}
				}
			}
		}
		catch { }

		return dict;
	}

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

	public static void RegisterTool( string name, string description, Func<JsonElement, object> handler, object inputSchema = null, string group = "Editor", object annotations = null, bool runOnMainThread = true )
	{
		Func<JsonElement, Task<object>> wrappedHandler;
		if ( runOnMainThread )
		{
			wrappedHandler = async args =>
			{
				await GameTask.MainThread();
				return handler( args );
			};
		}
		else
		{
			wrappedHandler = args => Task.FromResult( handler( args ) );
		}

		_tools[name] = new ToolDef( wrappedHandler, description, group, inputSchema, annotations );
		McpToolBridge.RegisterGlobalToolName( name );
		Log.Info( $"[MCP] Tool registered: {name} \u2014 {description} (mainThread: {runOnMainThread})" );
	}

	public static void RegisterToolAsync( string name, string description, Func<JsonElement, Task<object>> handler, object inputSchema = null, string group = "Editor", object annotations = null, bool runOnMainThread = true )
	{
		Func<JsonElement, Task<object>> wrappedHandler;
		if ( runOnMainThread )
		{
			wrappedHandler = async args =>
			{
				await GameTask.MainThread();
				return await handler( args );
			};
		}
		else
		{
			wrappedHandler = handler;
		}

		_tools[name] = new ToolDef( wrappedHandler, description, group, inputSchema, annotations );
		McpToolBridge.RegisterGlobalToolName( name );
		Log.Info( $"[MCP] Async tool registered: {name} \u2014 {description} (mainThread: {runOnMainThread})" );
	}

	public static async Task BroadcastLogAsync( string level, string logger, string message, JsonElement? data = null, string sourceSessionId = null )
	{
		AddServerLog( $"[{level.ToUpper()}] {logger}: {message}" );
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
		SchemaCompiler.Compile();
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
		McpLogBridge.OnLogMessage -= HandleLogMessage;
		McpLogBridge.OnLogMessage += HandleLogMessage;
		McpToolBridge.OnGameEvent -= HandleGameEvent;
		McpToolBridge.OnGameEvent += HandleGameEvent;
		McpToolBridge.OnToolsChanged += () => _ = BroadcastEventAsync( "notifications/tools/list_changed", "{}" );
		McpToolBridge.OnProgress += ( progress, total, message ) =>
		{
			var token = McpToolBridge.CurrentProgressToken;
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
		McpLogBridge.OnLogMessage -= HandleLogMessage;
		McpToolBridge.OnGameEvent -= HandleGameEvent;
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

	private static void HandleLogMessage( string level, string source, string message )
	{
		_ = BroadcastLogAsync( level, source, message );
	}

	private static void HandleGameEvent( string eventType, string dataJson )
	{
		_ = BroadcastEventAsync( "event", dataJson );
	}

	private static void RegisterBuiltinTools()
	{
		var builder = new PipelineBuilder();
		builder.Use<LogMiddleware>();
		builder.Use<RateLimitMiddleware>();
		_pipeline = builder.BuildContext( HandleMessageCoreAsync );

		McpBaseTools.Register();
		McpSceneTools.Register();
		McpCodeTools.Register();
	}

	// ── Property watch polling (called from state poll loop) ──────────────
	private static async Task PollWatchedPropertiesAsync()
	{
		if ( _watchedProperties.IsEmpty ) return;
		await GameTask.MainThread();
		var scene = Game.ActiveScene;
		if ( scene == null ) return;

		foreach ( var kv in _watchedProperties.ToArray() )
		{
			try
			{
				var w = kv.Value.Watch;
				if ( !Guid.TryParse( w.GameObjectId, out var guid ) ) continue;
				var go = scene.Directory.FindByGuid( guid );
				if ( !go.IsValid() ) continue;

				// Find component via TypeLibrary
				Component foundComp = null;
				foreach ( var c in go.Components.GetAll<Component>() )
				{
					if ( c.GetType().Name == w.ComponentType ) { foundComp = c; break; }
				}
				if ( foundComp == null ) continue;

				var typeDesc = TypeLibrary.GetType( foundComp.GetType() );
				var propDesc = typeDesc?.Properties.FirstOrDefault( p => p.Name == w.PropertyName );
				if ( propDesc == null ) continue;

				var currentValue = propDesc.GetValue( foundComp )?.ToString() ?? "null";
				var lastValue    = kv.Value.LastValue;

				if ( currentValue != lastValue )
				{
					_watchedProperties[kv.Key] = (w, currentValue);
					await BroadcastEventAsync( "notifications/property/changed", JsonSerializer.Serialize( new
					{
						watchId   = kv.Key,
						gameObjectId = w.GameObjectId,
						component = w.ComponentType,
						property  = w.PropertyName,
						oldValue  = lastValue,
						newValue  = currentValue,
						timestamp = DateTime.UtcNow
					} ) );
				}
			}
			catch { }
		}
	}

	// ── Cache helper ──────────────────────────────────────────────────────
	private static bool TryGetCached( string toolName, string argsKey, out object result )
	{
		result = null;
		if ( !_cacheableTools.Contains( toolName ) ) return false;
		var key = $"{toolName}::{argsKey}";
		if ( _toolCache.TryGetValue( key, out var entry ) && DateTime.UtcNow < entry.ExpiresAt )
		{
			result = entry.Result;
			return true;
		}
		return false;
	}

	private static void SetCache( string toolName, string argsKey, object result )
	{
		if ( !_cacheableTools.Contains( toolName ) ) return;
		var key = $"{toolName}::{argsKey}";
		_toolCache[key] = new CacheEntry( result, DateTime.UtcNow.AddSeconds( DefaultCacheTtlSeconds ) );
	}

	// ── (original closing brace for RegisterTools was here — now replaced by real closing brace above) ──

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
			catch ( SocketException e ) when ( e.SocketErrorCode == SocketError.AddressAlreadyInUse )
			{
				Log.Error( $"[MCP] Port {_port} is already in use by another application or instance! Stop any other editors or MCP servers." );
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
					for ( int i = 0; i < read; i++ )
					{
						headerBuf.Add( buffer[i] );
					}
					headerEnd = SearchBytes( headerBuf, _headerEndPattern );
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

	private static int SearchBytes( List<byte> data, byte[] pattern )
	{
		for ( int i = 0; i <= data.Count - pattern.Length; i++ )
		{
			var match = true;
			for ( int j = 0; j < pattern.Length; j++ )
			{
				if ( data[i + j] != pattern[j] ) { match = false; break; }
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
		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource( cts.Token, _cts.Token );
		var linkedToken = linkedCts.Token;

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
			while ( !linkedToken.IsCancellationRequested )
			{
				try
				{
					await GameTask.Delay( 15000, linkedToken );
					if ( linkedToken.IsCancellationRequested ) break;
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
			System.Threading.Interlocked.Increment( ref existingSession.RequestCount );

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
					sess.LogLevel = newLevel;
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

				var filtered = ApplyFocusFilter( allTools ).AsEnumerable();
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
				McpToolBridge.CurrentProgressToken = progressToken;

				// Get session cancellation token
				var toolCts = CancellationTokenSource.CreateLinkedTokenSource( _cts.Token );
				if ( _sessions.TryGetValue( sid, out var sess ) )
				{
					toolCts = CancellationTokenSource.CreateLinkedTokenSource( _cts.Token, sess.ToolCts.Token );
				}

				if ( _tools.TryGetValue( toolName, out var toolDef ) )
				{
					var validationError = Validation.ValidateArguments( args, toolDef.InputSchema );
					if ( validationError != null )
					{
						ctx.Response = id.InvalidParams( validationError );
						if ( sess != null )
							sess.CurrentToolTask = null;
						return;
					}

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
				var args = doc.RootElement.TryGetProperty( "params", out var p ) ? p : default;
				var validationError = Validation.ValidateArguments( args, toolDef.InputSchema );
				if ( validationError != null )
				{
					ctx.Response = id.InvalidParams( validationError );
					return;
				}

				var sw = Stopwatch.StartNew();
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
		
		var gm = GetDynamicComponent( "BlackFridayGameManager" );
		var quota = GetDynamicComponent( "QuotaManager" );
		var alarm = GetDynamicComponent( "AlarmSystem" );
		
		object phaseData = null;
		if ( gm != null )
		{
			var day = GetPropValue<int>( gm, "CurrentDay" );
			var phaseStr = GetPropValue<string>( gm, "CurrentPhase" );
			var timeRemaining = Math.Round( GetPropValue<double>( gm, "PhaseTimeRemaining" ), 1 );
			var progress = Math.Round( InvokeDoubleMethod( gm, "GetPhaseProgress" ), 2 );
			phaseData = new { day, phase = phaseStr, timeRemaining, progress };
		}
		
		object economyData = null;
		if ( quota != null )
		{
			var personalCash = Math.Round( GetPropValue<double>( quota, "MyPersonalCash" ), 1 );
			var personalQuota = Math.Round( GetPropValue<double>( quota, "PersonalQuota" ), 1 );
			var poolCurrent = Math.Round( GetPropValue<double>( quota, "SharedPoolCurrent" ), 1 );
			var poolTarget = Math.Round( GetPropValue<double>( quota, "SharedPoolTarget" ), 1 );
			economyData = new { personalCash, personalQuota, poolCurrent, poolTarget };
		}
		
		object alarmData = null;
		if ( alarm != null )
		{
			var level = InvokeStringMethod( alarm, "GetAlarmLevelName" );
			var progress = Math.Round( GetPropValue<double>( alarm, "AlarmProgress" ), 1 );
			alarmData = new { level, progress };
		}
		
		return new
		{
			phase = phaseData,
			economy = economyData,
			alarm = alarmData
		};
	}

	internal static object GetDynamicComponent( string typeName )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return null;
		
		var typeDesc = McpBridge.Tools.AssetTools.FindComponentType( typeName );
		if ( typeDesc == null ) return null;
		
		foreach ( var go in scene.GetAllObjects( true ) )
		{
			var comp = go.Components.Get( typeDesc.TargetType );
			if ( comp.IsValid() )
			{
				return comp;
			}
		}
		return null;
	}

	internal static T GetPropValue<T>( object obj, string propName, T defaultValue = default )
	{
		if ( obj == null ) return defaultValue;
		var prop = obj.GetType().GetProperty( propName );
		if ( prop != null )
		{
			try
			{
				var val = prop.GetValue( obj );
				if ( val != null )
				{
					if ( typeof(T) == typeof(string) )
					{
						return (T)(object)val.ToString();
					}
					return (T)Convert.ChangeType( val, typeof(T) );
				}
			}
			catch { }
		}
		return defaultValue;
	}

	internal static double InvokeDoubleMethod( object obj, string methodName, double defaultValue = 0.0 )
	{
		if ( obj == null ) return defaultValue;
		var method = obj.GetType().GetMethod( methodName, Array.Empty<Type>() );
		if ( method != null )
		{
			try
			{
				var val = method.Invoke( obj, null );
				if ( val != null )
				{
					return Convert.ToDouble( val );
				}
			}
			catch { }
		}
		return defaultValue;
	}

	internal static string InvokeStringMethod( object obj, string methodName, string defaultValue = "" )
	{
		if ( obj == null ) return defaultValue;
		var method = obj.GetType().GetMethod( methodName, Array.Empty<Type>() );
		if ( method != null )
		{
			try
			{
				return method.Invoke( obj, null )?.ToString() ?? defaultValue;
			}
			catch { }
		}
		return defaultValue;
	}

	internal static List<string> ListAssetsByExt( params string[] exts )
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

				// Poll watched properties
				await PollWatchedPropertiesAsync();

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
			try { await GameTask.Delay( 30000, _cts.Token ); }
			catch { break; }
			if ( _cts.IsCancellationRequested ) break;

			foreach ( var kv in _sessions.ToArray() )
			{
				if ( kv.Value.Cts.IsCancellationRequested )
					_sessions.TryRemove( kv.Key, out _ );
			}
		}
	}

	internal static object BuildSceneNode( GameObject go )
	{
		var components = new List<string>();
		foreach ( var c in go.Components.GetAll<Component>() )
		{
			try { components.Add( c.GetType().Name ); } catch { }
		}

		var children = new List<object>();
		foreach ( var child in go.Children )
		{
			if ( child.IsValid() )
			{
				children.Add( BuildSceneNode( child ) );
			}
		}

		return new
		{
			id = go.Id.ToString(),
			name = go.Name ?? "",
			position = new { x = go.WorldPosition.x, y = go.WorldPosition.y, z = go.WorldPosition.z },
			components,
			children
		};
	}

	internal static object ParseBuildMessage( string line, string type )
	{
		try
		{
			var colonIdx = line.IndexOf( "): " );
			if ( colonIdx == -1 ) return null;
			
			var filePart = line.Substring( 0, colonIdx + 1 );
			var restPart = line.Substring( colonIdx + 3 );
			
			var openParen = filePart.LastIndexOf( '(' );
			if ( openParen == -1 ) return null;
			
			var filePath = filePart.Substring( 0, openParen ).Trim();
			var posStr = filePart.Substring( openParen + 1, filePart.Length - openParen - 2 );
			
			int lineNum = 1;
			int colNum = 1;
			var posParts = posStr.Split( ',' );
			if ( posParts.Length >= 1 ) int.TryParse( posParts[0], out lineNum );
			if ( posParts.Length >= 2 ) int.TryParse( posParts[1], out colNum );
			
			var typePrefix = $"{type} ";
			if ( restPart.StartsWith( typePrefix, StringComparison.OrdinalIgnoreCase ) )
			{
				restPart = restPart.Substring( typePrefix.Length );
			}
			
			var code = "";
			var message = restPart;
			
			var codeColon = restPart.IndexOf( ':' );
			if ( codeColon != -1 )
			{
				code = restPart.Substring( 0, codeColon ).Trim();
				message = restPart.Substring( codeColon + 1 ).Trim();
			}
			
			var projectBracket = message.LastIndexOf( '[' );
			if ( projectBracket != -1 )
			{
				message = message.Substring( 0, projectBracket ).Trim();
			}
			
			return new
			{
				type,
				file = filePath,
				line = lineNum,
				column = colNum,
				code,
				message
			};
		}
		catch
		{
			return new { type, raw = line };
		}
	}
}
