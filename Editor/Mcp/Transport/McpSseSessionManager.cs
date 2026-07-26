using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Editor;

internal sealed class McpSseSessionManager
{
	private readonly ConcurrentDictionary<string, Session> _sessions = new();
	private readonly Func<McpConfig> _configProvider;

	public McpSseSessionManager( Func<McpConfig> configProvider )
	{
		_configProvider = configProvider;
	}

	public int Count => _sessions.Count;

	public async Task OpenAsync(
		HttpListenerContext context,
		CancellationToken cancellationToken )
	{
		var response = context.Response;
		var sessionId = Guid.NewGuid().ToString();
		response.ContentType = "text/event-stream";
		response.Headers.Add( "Cache-Control", "no-cache" );
		response.Headers.Add( "Connection", "keep-alive" );

		var session = new Session( new StreamWriter( response.OutputStream, Encoding.UTF8 ) );
		_sessions[sessionId] = session;
		await session.WriteAsync(
			$"event: endpoint\ndata: http://localhost:{_configProvider().Port}/messages?sessionId={sessionId}\n\n",
			cancellationToken );

		try
		{
			while ( !cancellationToken.IsCancellationRequested
				&& response.OutputStream.CanWrite )
			{
				var config = _configProvider();
				await Task.Delay(
					Math.Max( 250, config.SseKeepAliveIntervalMs ),
					cancellationToken );
				if ( config.EnableSseKeepAlive )
					await session.WriteAsync( ": keep-alive\n\n", cancellationToken );
			}
		}
		catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
		{
			// Expected during server shutdown.
		}
		catch
		{
			// Client disconnected.
		}
		finally
		{
			_sessions.TryRemove( sessionId, out _ );
			session.Close();
			try { response.Close(); } catch { }
		}
	}

	public async Task<bool> SendAsync(
		string sessionId,
		string payload,
		CancellationToken cancellationToken = default )
	{
		if ( payload == null || !_sessions.TryGetValue( sessionId, out var session ) )
			return false;

		try
		{
			await session.WriteAsync(
				$"event: message\ndata: {payload}\n\n",
				cancellationToken );
			return true;
		}
		catch
		{
			_sessions.TryRemove( sessionId, out _ );
			session.Close();
			return false;
		}
	}

	public void CloseAll()
	{
		foreach ( var pair in _sessions )
		{
			if ( _sessions.TryRemove( pair.Key, out var session ) )
				session.Close();
		}
	}

	private sealed class Session
	{
		private readonly StreamWriter _writer;
		private readonly SemaphoreSlim _writeLock = new( 1, 1 );

		public Session( StreamWriter writer )
		{
			_writer = writer;
		}

		public async Task WriteAsync( string payload, CancellationToken cancellationToken )
		{
			await _writeLock.WaitAsync( cancellationToken );
			try
			{
				await _writer.WriteAsync( payload );
				await _writer.FlushAsync();
			}
			finally
			{
				_writeLock.Release();
			}
		}

		public void Close()
		{
			try { _writer.Close(); } catch { }
		}
	}
}
