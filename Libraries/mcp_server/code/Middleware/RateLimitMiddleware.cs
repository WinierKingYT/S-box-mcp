using System;
using System.Threading.Tasks;

namespace McpBridge.Middleware;

internal sealed class SlidingWindowRateLimiter
{
	private readonly int _maxPerSecond;
	private readonly long[] _slots;
	private int _index;
	private readonly object _lock = new();

	public SlidingWindowRateLimiter( int maxPerSecond = 60 )
	{
		_maxPerSecond = maxPerSecond;
		_slots = new long[maxPerSecond];
	}

	public bool TryAcquire()
	{
		lock ( _lock )
		{
			var now = DateTime.UtcNow.Ticks / 10000L;
			_slots[_index] = now;
			_index = ( _index + 1 ) % _maxPerSecond;
			return _slots[_index] < now - 1000;
		}
	}
}

public sealed class RateLimitMiddleware : IMiddleware
{
	private readonly SlidingWindowRateLimiter _limiter = new( 60 );

	public async Task InvokeAsync( McpContext ctx, MiddlewareDelegate next )
	{
		if ( !_limiter.TryAcquire() )
		{
			ctx.Response = $"{{\"jsonrpc\":\"2.0\",\"error\":{{\"code\":-32000,\"message\":\"Rate limited\"}},\"id\":{ctx.Id ?? 0}}}";
			ctx.Handled = true;
			return;
		}
		await next();
	}
}
