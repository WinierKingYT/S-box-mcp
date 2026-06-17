using System;

namespace McpBridge.Middleware;

public class RateLimiter
{
	private readonly int _maxPerSecond;
	private readonly long[] _slots;
	private int _index;
	private bool _wasLimited;

	public RateLimiter( int maxPerSecond = 30 )
	{
		_maxPerSecond = maxPerSecond;
		_slots = new long[maxPerSecond];
	}

	public bool IsLimited => _wasLimited;

	public bool TryAcquire()
	{
		var now = DateTime.UtcNow.Ticks / 10000L;
		_slots[_index] = now;
		_index = ( _index + 1 ) % _maxPerSecond;
		var oldest = now - 1000;
		var limited = _slots[_index] >= oldest;
		_wasLimited = limited;
		return !limited;
	}

	public void Reset() => _wasLimited = false;
}
