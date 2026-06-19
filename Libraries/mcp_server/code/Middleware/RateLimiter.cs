using System;

namespace McpBridge.Middleware;

public class RateLimiter
{
	private readonly int _maxPerSecond;
	private readonly long[] _slots;
	private int _index;
	private bool _wasLimited;
	private readonly object _lock = new();

	public RateLimiter( int maxPerSecond = 30 )
	{
		_maxPerSecond = maxPerSecond;
		_slots = new long[maxPerSecond];
	}

	public bool IsLimited => _wasLimited;

	public bool TryAcquire()
	{
		lock ( _lock )
		{
			var now = DateTime.UtcNow.Ticks / 10000L;
			var oldest = now - 1000;
			if ( _slots[_index] >= oldest )
			{
				_wasLimited = true;
				return false;
			}
			_slots[_index] = now;
			_index = ( _index + 1 ) % _maxPerSecond;
			_wasLimited = false;
			return true;
		}
	}

	public void Reset()
	{
		lock ( _lock ) { _wasLimited = false; }
	}
}
