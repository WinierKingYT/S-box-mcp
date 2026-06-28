using Sandbox;
using System;
using System.Collections.Generic;

namespace McpBridge;

public static class McpReplicationQueue
{
	private static readonly Queue<Action> _queue = new();
	private static readonly object _lock = new();
	
	public static int MaxOperationsPerFrame { get; set; } = 15;

	public static void Enqueue( Action action )
	{
		if ( action == null ) return;
		lock ( _lock )
		{
			_queue.Enqueue( action );
		}
	}

	public static void Tick()
	{
		int processed = 0;
		List<Action> batch = new();

		lock ( _lock )
		{
			while ( _queue.Count > 0 && processed < MaxOperationsPerFrame )
			{
				batch.Add( _queue.Dequeue() );
				processed++;
			}
		}

		foreach ( var action in batch )
		{
			try
			{
				action();
			}
			catch ( Exception e )
			{
				Log.Warning( $"[MCP] Error executing replication queue item: {e.Message}" );
			}
		}
	}

	public static void Clear()
	{
		lock ( _lock )
		{
			_queue.Clear();
		}
	}

	public static int Count
	{
		get
		{
			lock ( _lock )
			{
				return _queue.Count;
			}
		}
	}
}
