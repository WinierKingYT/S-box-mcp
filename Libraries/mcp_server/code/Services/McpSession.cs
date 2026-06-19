using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge;

public static class McpSession
{
	private static readonly Dictionary<string, SessionData> _sessions = new();
	private static readonly object _lock = new();
	private static readonly string StoragePath = "mcp_sessions.json";
	private static bool _loaded;
	private static bool _dirty;

	public static void MarkDirty()
	{
		lock ( _lock )
			_dirty = true;
	}

	public static void Flush()
	{
		if ( !_dirty ) return;
		SaveAll();
	}

	private static void EnsureLoaded()
	{
		if ( _loaded ) return;

		lock ( _lock )
		{
			if ( _loaded ) return;
			_loaded = true;
		}

		var data = PersistenceStore.Load<Dictionary<string, SessionData>>( StoragePath );
		if ( data != null )
		{
			lock ( _lock )
			{
				foreach ( var kv in data )
					_sessions[kv.Key] = kv.Value;
			}
		}

		Cleanup();
	}

	public static void Cleanup()
	{
		var config = McpConfig.Load();
		if ( config.CleanupDays <= 0 ) return;
		var cutoff = DateTime.UtcNow - TimeSpan.FromDays( config.CleanupDays );
		lock ( _lock )
		{
			var toRemove = _sessions.Where( kv => kv.Value.UpdatedAt < cutoff ).Select( kv => kv.Key ).ToList();
			foreach ( var key in toRemove )
			{
				_sessions.Remove( key );
				Log.Info( $"[MCP] Cleaned up stale session: {key}" );
			}
			if ( toRemove.Count > 0 ) _dirty = true;
		}
	}

	public static SessionData Get( string id )
	{
		EnsureLoaded();
		lock ( _lock )
		{
			if ( !_sessions.TryGetValue( id, out var session ) )
			{
				session = new SessionData { Id = id };
				_sessions[id] = session;
				_dirty = true;
			}
			return session;
		}
	}

	public static void SaveAll()
	{
		Dictionary<string, SessionData> snapshot;
		lock ( _lock )
		{
			snapshot = new Dictionary<string, SessionData>( _sessions );
			_dirty = false;
		}
		PersistenceStore.Save( StoragePath, snapshot );
	}

	public static void Remove( string id )
	{
		EnsureLoaded();
		lock ( _lock )
		{
			_sessions.Remove( id );
			_dirty = true;
		}
	}

	public static List<SessionData> List()
	{
		EnsureLoaded();
		lock ( _lock )
		{
			return _sessions.Values.ToList();
		}
	}
}
