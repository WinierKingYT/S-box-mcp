using System;
using System.Diagnostics;
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
			Log.Info( $"[MCP] {ctx.Method} id={ctx.Id ?? 0} {Math.Round( sw.Elapsed.TotalMilliseconds, 1 )}ms" );
		}
	}
}
