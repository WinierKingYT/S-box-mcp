using Sandbox;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Editor;

internal sealed class McpHttpServer
{
	private readonly Func<McpConfig> _configProvider;
	private readonly McpProtocolHandler _protocol;
	private readonly McpSseSessionManager _sessions;
	private HttpListener _listener;
	private CancellationTokenSource _shutdown;

	public McpHttpServer(
		Func<McpConfig> configProvider,
		McpProtocolHandler protocol,
		McpSseSessionManager sessions )
	{
		_configProvider = configProvider;
		_protocol = protocol;
		_sessions = sessions;
	}

	public bool IsRunning { get; private set; }
	public int ActiveSseSessions => _sessions.Count;

	public async Task RunAsync( int retriesLeft = 3 )
	{
		var port = _configProvider().Port;
		try
		{
			_shutdown = new CancellationTokenSource();
			_listener = new HttpListener();
			_listener.Prefixes.Add( $"http://localhost:{port}/" );
			_listener.Start();
			IsRunning = true;
			McpRuntimeState.SetServer( true, port );
			Log.Info(
				$"[MCP] Streamable HTTP: http://localhost:{port}/mcp (legacy SSE: /sse)" );

			while ( IsRunning && !_shutdown.IsCancellationRequested )
			{
				try
				{
					var context = await _listener.GetContextAsync();
					_ = HandleAsync( context, _shutdown.Token );
				}
				catch ( ObjectDisposedException )
				{
					break;
				}
				catch ( HttpListenerException ) when ( !IsRunning )
				{
					break;
				}
			}
		}
		catch ( HttpListenerException exception )
			when ( exception.ErrorCode == 5
				|| exception.Message.Contains( "access", StringComparison.OrdinalIgnoreCase ) )
		{
			Log.Error(
				$"[MCP] URL access denied for localhost:{port}. Reserve the URL for the current user only." );
		}
		catch ( HttpListenerException )
		{
			IsRunning = false;
			CloseListener();
			if ( retriesLeft > 0 && _shutdown?.IsCancellationRequested != true )
			{
				Log.Warning( $"[MCP] Port {port} busy, retrying ({retriesLeft})..." );
				await Task.Delay( 2000 );
				await RunAsync( retriesLeft - 1 );
			}
			else
			{
				Log.Error(
					$"[MCP] Port {port} busy after retries — close & reopen editor" );
			}
		}
		catch ( OperationCanceledException ) when ( _shutdown?.IsCancellationRequested == true )
		{
			// Expected during shutdown.
		}
		catch ( Exception exception )
		{
			Log.Error( $"[MCP] Error: {exception.Message}" );
		}
		finally
		{
			IsRunning = false;
			McpRuntimeState.SetServer( false, port );
		}
	}

	public void Stop()
	{
		IsRunning = false;
		try { _shutdown?.Cancel(); } catch { }
		_sessions.CloseAll();
		CloseListener();
	}

	private async Task HandleAsync(
		HttpListenerContext context,
		CancellationToken cancellationToken )
	{
		try
		{
			var request = context.Request;
			var response = context.Response;
			var originAllowed = ApplyCorsHeaders( request, response );

			if ( request.HttpMethod == "OPTIONS" )
			{
				response.StatusCode = originAllowed ? 204 : 403;
				response.Close();
				return;
			}

			var path = request.Url.AbsolutePath;
			var route = McpHttpRequestRouter.Resolve(
				request.HttpMethod,
				path,
				request.Headers["x-api-key"],
				_configProvider() );
			switch ( route )
			{
				case McpHttpRoute.Health:
					response.StatusCode = 200;
					response.Close();
					break;
				case McpHttpRoute.Unauthorized:
					response.StatusCode = 401;
					response.Close();
					break;
				case McpHttpRoute.StreamableMessage:
					McpRuntimeState.RecordProtocolHeader(
						request.Headers["MCP-Protocol-Version"] );
					if ( !McpProtocolVersionPolicy.IsAcceptedHeader(
						request.Headers["MCP-Protocol-Version"] ) )
					{
						await WriteProtocolVersionErrorAsync(
							response,
							request.Headers["MCP-Protocol-Version"] );
						break;
					}
					await HandleStreamableMessageAsync( context );
					break;
				case McpHttpRoute.StreamableMethodNotAllowed:
					response.StatusCode = 405;
					response.AddHeader( "Allow", "POST" );
					response.Close();
					break;
				case McpHttpRoute.LegacySse:
					await _sessions.OpenAsync( context, cancellationToken );
					break;
				case McpHttpRoute.LegacyMessage:
					await HandleLegacyMessageAsync( context, cancellationToken );
					break;
				default:
					response.StatusCode = 404;
					response.Close();
					break;
			}
		}
		catch ( Exception exception )
		{
			Log.Info(
				$"[MCP] HandleAsync threw: {exception.GetType().Name} {exception.Message}" );
			try
			{
				context.Response.StatusCode = 500;
				context.Response.Close();
			}
			catch
			{
				// Connection is already closed.
			}
		}
	}

	private bool ApplyCorsHeaders(
		HttpListenerRequest request,
		HttpListenerResponse response )
	{
		var origin = request.Headers["Origin"];
		var config = _configProvider();
		if ( !McpHttpSecurity.IsOriginAllowed( origin, config.CorsOrigins ) )
			return false;
		if ( string.IsNullOrWhiteSpace( origin ) )
			return true;

		response.AddHeader(
			"Access-Control-Allow-Origin",
			config.CorsOrigins.Contains( "*" ) ? "*" : origin );
		response.AddHeader( "Vary", "Origin" );
		response.AddHeader( "Access-Control-Allow-Methods", "GET, POST, OPTIONS" );
		response.AddHeader(
			"Access-Control-Allow-Headers",
			"Content-Type, Accept, x-api-key, MCP-Protocol-Version" );
		return true;
	}

	private async Task HandleStreamableMessageAsync( HttpListenerContext context )
	{
		using var reader = new StreamReader( context.Request.InputStream );
		var result = await _protocol.ProcessAsync( await reader.ReadToEndAsync() );
		if ( result == null )
		{
			context.Response.StatusCode = 202;
			context.Response.Close();
			return;
		}

		var payload = Encoding.UTF8.GetBytes( result );
		context.Response.StatusCode = 200;
		context.Response.ContentType = "application/json";
		context.Response.ContentEncoding = Encoding.UTF8;
		context.Response.ContentLength64 = payload.Length;
		await context.Response.OutputStream.WriteAsync( payload, 0, payload.Length );
		context.Response.Close();
	}

	private async Task HandleLegacyMessageAsync(
		HttpListenerContext context,
		CancellationToken cancellationToken )
	{
		var sessionId = context.Request.QueryString["sessionId"] ?? "default";
		using var reader = new StreamReader( context.Request.InputStream );
		var result = await _protocol.ProcessAsync( await reader.ReadToEndAsync() );
		await _sessions.SendAsync( sessionId, result, cancellationToken );
		context.Response.StatusCode = 202;
		context.Response.Close();
	}

	private void CloseListener()
	{
		try
		{
			_listener?.Stop();
			_listener?.Close();
		}
		catch
		{
			// Best-effort shutdown.
		}
	}

	private static async Task WriteProtocolVersionErrorAsync(
		HttpListenerResponse response,
		string requestedVersion )
	{
		var payload = Encoding.UTF8.GetBytes(
			McpJsonRpcSerializer.Error(
				null,
				-32600,
				$"Unsupported MCP protocol version '{requestedVersion}'. Supported: {string.Join( ", ", McpProtocolConstants.SupportedVersions )}" ) );
		response.StatusCode = 400;
		response.ContentType = "application/json";
		response.ContentEncoding = Encoding.UTF8;
		response.ContentLength64 = payload.Length;
		await response.OutputStream.WriteAsync( payload, 0, payload.Length );
		response.Close();
	}
}
