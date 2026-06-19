using McpBridge;
using McpBridge.Execution;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text;

namespace Editor;

internal static class McpBaseTools
{
	private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds( 15 ) };

	private static void ApplyHeaders( System.Net.Http.HttpRequestMessage req, JsonElement headersElement )
	{
		var element = headersElement;
		if ( headersElement.ValueKind == JsonValueKind.String )
		{
			var str = headersElement.GetString();
			if ( !string.IsNullOrEmpty( str ) )
			{
				try
				{
					using var doc = JsonDocument.Parse( str );
					element = doc.RootElement.Clone();
				}
				catch { /* ignore invalid JSON */ }
			}
		}
		if ( element.ValueKind == JsonValueKind.Object )
		{
			foreach ( var header in element.EnumerateObject() )
			{
				req.Headers.TryAddWithoutValidation( header.Name, header.Value.GetString() );
			}
		}
	}

	internal static void Register()
	{
		McpEditorServer.RegisterToolAsync( "list_tools", "List all available tools (editor + game) with optional pagination and filtering", async args =>
		{
			McpBridge.McpBridgeAutoInit.EnsureCreated();
			var page = args.TryGetProperty( "page", out var pn ) ? Math.Max( 1, pn.GetInt32() ) : 1;
			var perPage = args.TryGetProperty( "perPage", out var pp ) ? Math.Clamp( pp.GetInt32(), 1, 500 ) : 100;
			var query = args.TryGetProperty( "query", out var q ) ? q.GetString() ?? "" : "";
			var groupFilter = args.TryGetProperty( "group", out var g ) ? g.GetString() ?? "" : "";

			var all = new List<Dictionary<string, object>>();
			foreach ( var t in McpEditorServer._tools )
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
		}, new { type = "object", properties = new { page = new { type = "number", description = "Page number (1-based)" }, perPage = new { type = "number", description = "Results per page (max 500)" }, query = new { type = "string", description = "Search query" }, group = new { type = "string", description = "Filter by group" } }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "sbox_mcp_clients", "List all connected SSE clients and their stats", _ =>
		{
			var now = DateTime.UtcNow;
			var clients = McpEditorServer._sessions.Select( kv => new
			{
				sessionId = kv.Key,
				connectedForSec = Math.Round( (now - kv.Value.ConnectedAt).TotalSeconds, 1 ),
				remoteEndPoint = kv.Value.RemoteEndPoint,
				requestCount = kv.Value.RequestCount,
				logLevel = kv.Value.LogLevel
			} ).ToList();
			return new { count = clients.Count, clients };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterToolAsync( "sbox_http_get", "Make an HTTP GET request. Returns status code and body.", async args =>
		{
			try
			{
				var url = args.GetProperty( "url" ).GetString();
				using var req = new System.Net.Http.HttpRequestMessage( System.Net.Http.HttpMethod.Get, url );
				if ( args.TryGetProperty( "headers", out var h ) )
				{
					ApplyHeaders( req, h );
				}
				var resp = await _httpClient.SendAsync( req );
				var body = await resp.Content.ReadAsStringAsync();
				return new { success = true, statusCode = (int)resp.StatusCode, contentType = resp.Content.Headers.ContentType?.ToString(), bodyLength = body.Length, body = body.Length < 10000 ? body : body.Substring( 0, 10000 ) + "..." };
			}
			catch ( Exception e ) { return new { error = e.Message }; }
		}, new { type = "object", properties = new { url = new { type = "string", description = "Target URL" }, headers = new { type = "string", description = "Optional JSON object of headers" } }, required = new[] { "url" } }, annotations: new { readOnlyHint = true, openWorldWarning = true }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "sbox_mcp_bridge_status", "Show connected game bridges and their health", _ =>
		{
			var bridges = McpToolBridge.GetBridgeStatus();
			return new { count = bridges.Length, bridges = bridges.Select( b => new { b.name, errorCount = b.errors, version = b.version, toolCount = b.toolCount, capabilities = b.capabilities } ) };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "get_game_state", "Get current day/phase/time", _ =>
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			var gm = McpEditorServer.GetDynamicComponent( "BlackFridayGameManager" );
			if ( gm == null ) return "No game manager found";
			var day = McpEditorServer.GetPropValue<int>( gm, "CurrentDay" );
			var phase = McpEditorServer.GetPropValue<string>( gm, "CurrentPhase" );
			var timeLeft = McpEditorServer.GetPropValue<float>( gm, "PhaseTimeRemaining" );
			return new { day, phase, timeLeft };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterToolAsync( "sbox_http_post", "Make an HTTP POST request with JSON body.", async args =>
		{
			try
			{
				var url = args.GetProperty( "url" ).GetString();
				var body = args.TryGetProperty( "body", out var b ) ? b.GetString() ?? "{}" : "{}";
				using var req = new System.Net.Http.HttpRequestMessage( System.Net.Http.HttpMethod.Post, url );
				if ( args.TryGetProperty( "headers", out var h ) )
				{
					ApplyHeaders( req, h );
				}
				req.Content = new System.Net.Http.StringContent( body, Encoding.UTF8, "application/json" );
				var resp = await _httpClient.SendAsync( req );
				var respBody = await resp.Content.ReadAsStringAsync();
				return new { success = true, statusCode = (int)resp.StatusCode, contentType = resp.Content.Headers.ContentType?.ToString(), bodyLength = respBody.Length, body = respBody.Length <= 10000 ? respBody : respBody.Substring( 0, 10000 ) + "..." };
			}
			catch ( Exception e ) { return new { error = e.Message }; }
		}, new { type = "object", properties = new { url = new { type = "string", description = "Target URL" }, body = new { type = "string", description = "JSON body" }, headers = new { type = "string", description = "Optional JSON object of headers" } }, required = new[] { "url" } }, annotations: new { destructiveHint = true, openWorldWarning = true }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "sbox_log_query", "Query recent logs with optional level and text filtering", args =>
		{
			var count = args.TryGetProperty( "count", out var c ) ? Math.Clamp( c.GetInt32(), 1, 500 ) : 50;
			var minLevel = args.TryGetProperty( "minLevel", out var l ) ? l.GetString() : null;
			var search = args.TryGetProperty( "search", out var s ) ? s.GetString() : null;
			var entries = McpLogBridge.GetRecent( count, minLevel, search );
			return new { count = entries.Count, entries = entries.Select( e => new { e.Level, e.Message, e.Time } ) };
		}, new { type = "object", properties = new { count = new { type = "number", description = "Max entries (1-500)" }, minLevel = new { type = "string", description = "Minimum level: debug/info/notice/warning/error/critical" }, search = new { type = "string", description = "Text search filter" } }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "sbox_log_clear", "Clear all buffered logs", _ =>
		{
			var before = McpLogBridge.Count;
			McpLogBridge.Clear();
			return new { cleared = true, removedCount = before };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { destructiveHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "sbox_server_info", "Get MCP server configuration and status", _ =>
		{
			return new
			{
				port = McpEditorServer.Port,
				sessions = McpEditorServer._sessions.Count,
				editorTools = McpEditorServer._tools.Count,
				uptimeMin = Math.Round( (DateTime.UtcNow - McpEditorServer._startTime).TotalMinutes, 1 ),
				bridges = McpToolBridge.GetBridgeStatus().Select( b => new { b.name, version = b.version, toolCount = b.toolCount } )
			};
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "sbox_update_config", "Update MCP config settings at runtime (apiKey, etc.). Port changes require restart.", args =>
		{
			var cfg = McpConfig.Load();
			var changes = new List<string>();

			if ( args.TryGetProperty( "apiKey", out var key ) )
			{
				var newKey = key.GetString() ?? "";
				if ( newKey.Length >= 8 )
				{
					McpEditorServer._apiKey = newKey;
					cfg.ApiKey = newKey;
					changes.Add( "apiKey" );
				}
			}

			if ( changes.Count > 0 )
			{
				McpConfig.Save( cfg );
				_ = McpEditorServer.BroadcastLogAsync( "info", "server", $"Config updated: {string.Join( ", ", changes )}" );
			}

			return new { updated = changes, currentApiKey = McpEditorServer.ApiKey[..Math.Min( 4, McpEditorServer.ApiKey.Length )] + "..." };
		}, new { type = "object", properties = new { apiKey = new { type = "string", description = "New API key (min 8 chars)" } }, required = Array.Empty<string>() }, annotations: new { destructiveHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "sbox_replay_history", "View recent tool execution history", _ =>
		{
			var history = McpReplay.GetHistory( 50 );
			return new { count = history.Count, history = history.Select( h => new { h.Method, h.DurationMs, h.Success, h.Timestamp } ) };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "sbox_replay_analytics", "Get aggregated analytics of tool usage", _ =>
		{
			return McpReplay.GetAnalytics();
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "sbox_undo", "Undo the last action", _ =>
		{
			return UndoRedoManager.Undo();
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_redo", "Redo the last undone action", _ =>
		{
			return UndoRedoManager.Redo();
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_undo_history", "View undo history", _ =>
		{
			var history = UndoRedoManager.GetHistory();
			return new { count = history.Count, history };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterToolAsync( "sbox_batch", "Execute multiple tool calls in sequence. Input: array of {method, params?}. Returns array of results.", async args =>
		{
			if ( args.ValueKind != JsonValueKind.Array )
				return new { error = "Input must be a JSON array of objects" };

			var results = new List<object>();
			foreach ( var item in args.EnumerateArray() )
			{
				if ( !item.TryGetProperty( "method", out var methodProp ) )
				{
					results.Add( new { error = "Missing 'method' property" } );
					continue;
				}

				var method = methodProp.GetString();
				var itemArgs = item.TryGetProperty( "params", out var pProp ) ? pProp : default;

				try
				{
					if ( McpEditorServer._tools.TryGetValue( method, out var toolDef ) )
					{
						var res = await toolDef.Handler( itemArgs );
						results.Add( new { method, success = true, result = res } );
					}
					else
					{
						var argsJson = itemArgs.ValueKind == JsonValueKind.Undefined ? "{}" : itemArgs.GetRawText();
						var bridgeResponse = await McpToolBridge.RouteToolRequest( method, argsJson );
						if ( bridgeResponse != null )
						{
							try
							{
								using var brDoc = JsonDocument.Parse( bridgeResponse );
								if ( brDoc.RootElement.TryGetProperty( "result", out var resVal ) )
								{
									results.Add( new { method, success = true, result = JsonSerializer.Deserialize<object>( resVal.GetRawText() ) } );
								}
								else
								{
									results.Add( new { method, success = true, result = bridgeResponse } );
								}
							}
							catch
							{
								results.Add( new { method, success = true, result = bridgeResponse } );
							}
						}
						else
						{
							results.Add( new { method, success = false, error = $"Method '{method}' not found" } );
						}
					}
				}
				catch ( Exception e )
				{
					results.Add( new { method, success = false, error = e.Message } );
				}
			}

			return results;
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { destructiveHint = true }, runOnMainThread: true );
	}
}
