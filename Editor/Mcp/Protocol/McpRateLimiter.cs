using System;

namespace Editor;

internal sealed class McpRateLimiter
{
	private readonly Func<int> _limitProvider;
	private readonly TimeProvider _timeProvider;
	private readonly object _lock = new();
	private int _requestCount;
	private DateTimeOffset _lastReset;

	public McpRateLimiter( Func<int> limitProvider, TimeProvider timeProvider = null )
	{
		_limitProvider = limitProvider ?? throw new ArgumentNullException( nameof( limitProvider ) );
		_timeProvider = timeProvider ?? TimeProvider.System;
		_lastReset = _timeProvider.GetUtcNow();
	}

	public bool TryAcquire()
	{
		lock ( _lock )
		{
			var limit = _limitProvider();
			if ( limit <= 0 ) return true;

			var now = _timeProvider.GetUtcNow();
			if ( (now - _lastReset).TotalSeconds >= 1 )
			{
				_requestCount = 0;
				_lastReset = now;
			}

			if ( _requestCount >= limit ) return false;
			_requestCount++;
			return true;
		}
	}
}
