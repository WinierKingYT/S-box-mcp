using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

namespace McpBridge.Middleware;

public sealed class LogMiddleware : IMiddleware
{
	public async Task InvokeAsync( McpContext ctx, MiddlewareDelegate next )
	{
		var sw = Stopwatch.StartNew();
		try { await next(); }
		finally
		{
			sw.Stop();
			var entry = JsonSerializer.Serialize( new
			{
				type = "mcp_request",
				method = ctx.Method,
				id = ctx.Id ?? 0,
				durationMs = Math.Round( sw.Elapsed.TotalMilliseconds, 1 ),
				hasError = ctx.Response?.Contains( "\"error\"" ) == true
			} );
			// Logging is handled by the host environment (EditorServer logs via Sandbox.Log.Info)
		}
	}
}
