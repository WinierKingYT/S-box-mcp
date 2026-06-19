using System;
using System.Threading.Tasks;

namespace McpBridge.Middleware;

public sealed class SlidingWindowRateLimiter
{
	private readonly int _maxRequests;
	private readonly int _windowMs;
	private readonly long[] _slots;
	private int _index;
	private readonly object _lock = new();

	public SlidingWindowRateLimiter( int maxRequests = 60, int windowMs = 1000 )
	{
		_maxRequests = maxRequests;
		_windowMs = windowMs;
		_slots = new long[maxRequests];
	}

	public bool TryAcquire()
	{
		lock ( _lock )
		{
			var now = DateTime.UtcNow.Ticks / 10000L;
			var oldest = now - _windowMs;
			if ( _slots[_index] >= oldest )
				return false;
			_slots[_index] = now;
			_index = ( _index + 1 ) % _maxRequests;
			return true;
		}
	}
}

public sealed class RateLimitMiddleware : IMiddleware
{
	private readonly SlidingWindowRateLimiter _globalLimiter = new( 60 );
	private const string SessionLimiterKey = "rateLimiter";

	public async Task InvokeAsync( McpContext ctx, MiddlewareDelegate next )
	{
		// Use per-session rate limiter when available, fall back to global
		var limiter = ctx.Items.TryGetValue( SessionLimiterKey, out var obj ) && obj is SlidingWindowRateLimiter perSession
			? perSession
			: _globalLimiter;

		if ( !limiter.TryAcquire() )
		{
			ctx.Response = $"{{\"jsonrpc\":\"2.0\",\"error\":{{\"code\":-32001,\"message\":\"Request cancelled: rate limited\"}},\"id\":{ctx.Id ?? 0}}}";
			ctx.Handled = true;
			return;
		}
		await next();
	}
}
