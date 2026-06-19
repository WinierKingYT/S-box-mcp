using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace McpBridge;

public sealed record BridgeRegistration(
	string Name,
	Func<string, string, Task<string>> OnToolRequest,
	Func<Task<string>> OnListGameTools,
	Func<Task<string>> OnStateSnapshot,
	string ToolPrefix = null
);

public static class McpToolBridge
{
	private static readonly List<BridgeRegistration> _bridges = new();
	private static volatile BridgeRegistration[] _snapshot = Array.Empty<BridgeRegistration>();
	private static readonly ConcurrentDictionary<string, byte> _globalToolNames = new();
	private static readonly ConcurrentDictionary<string, int> _bridgeErrors = new();

	public static void RegisterGlobalToolName( string name )
	{
		_globalToolNames.TryAdd( name, 0 );
	}

	public static bool GlobalToolExists( string name )
	{
		return _globalToolNames.ContainsKey( name );
	}

	public static void Register( BridgeRegistration reg )
	{
		lock ( _bridges )
		{
			_bridges.RemoveAll( b => b.Name == reg.Name );
			_bridges.Add( reg );
			_snapshot = _bridges.ToArray();
		}
	}

	public static void Unregister( string name )
	{
		lock ( _bridges )
		{
			_bridges.RemoveAll( b => b.Name == name );
			_snapshot = _bridges.ToArray();
		}
	}

	public static (string name, int errors)[] GetBridgeStatus()
	{
		lock ( _bridges )
			return _bridges.Select( b => (b.Name, _bridgeErrors.GetValueOrDefault( b.Name, 0 )) ).ToArray();
	}

	public static async Task<string> RouteToolRequest( string method, string args )
	{
		// Phase 1: Try prefix-matched bridges first
		foreach ( var b in _snapshot )
		{
			if ( b.OnToolRequest == null ) continue;
			if ( !string.IsNullOrEmpty( b.ToolPrefix ) && !method.StartsWith( b.ToolPrefix, StringComparison.OrdinalIgnoreCase ) )
				continue;
			if ( _bridgeErrors.GetValueOrDefault( b.Name, 0 ) >= 5 ) continue;
			try
			{
				var result = await b.OnToolRequest( method, args );
				if ( result != null )
				{
					_bridgeErrors.TryRemove( b.Name, out _ );
					return result;
				}
			}
			catch
			{
				_bridgeErrors.AddOrUpdate( b.Name, 1, ( _, c ) => c + 1 );
			}
		}

		// Phase 2: Try all bridges as fallback (including those without prefix match)
		foreach ( var b in _snapshot )
		{
			if ( b.OnToolRequest == null ) continue;
			if ( !string.IsNullOrEmpty( b.ToolPrefix ) && method.StartsWith( b.ToolPrefix, StringComparison.OrdinalIgnoreCase ) )
				continue; // already tried in phase 1
			if ( _bridgeErrors.GetValueOrDefault( b.Name, 0 ) >= 5 ) continue;
			try
			{
				var result = await b.OnToolRequest( method, args );
				if ( result != null )
				{
					_bridgeErrors.TryRemove( b.Name, out _ );
					return result;
				}
			}
			catch
			{
				_bridgeErrors.AddOrUpdate( b.Name, 1, ( _, c ) => c + 1 );
			}
		}
		return null;
	}

	public static async Task<string> ListAllGameTools()
	{
		var results = new List<string>();
		foreach ( var b in _snapshot )
		{
			if ( b.OnListGameTools != null )
			{
				try { var result = await b.OnListGameTools(); if ( result != null ) results.Add( result ); } catch { }
			}
		}
		return results.Any() ? "[" + string.Join( ",", results ) + "]" : null;
	}

	public static async Task<string> GetAnyStateSnapshot()
	{
		foreach ( var b in _snapshot )
		{
			if ( b.OnStateSnapshot != null )
			{
				try { var result = await b.OnStateSnapshot(); if ( result != null ) return result; } catch { }
			}
		}
		return null;
	}

	public static void ResetBridgeErrors( string name = null )
	{
		if ( name != null )
			_bridgeErrors.TryRemove( name, out _ );
		else
			_bridgeErrors.Clear();
	}

	public static void ResetAllBridgeErrors()
	{
		_bridgeErrors.Clear();
	}

	private static readonly ConcurrentDictionary<string, byte> _eventSubscriptions = new();

	public static void SubscribeEvent( string eventType )
	{
		_eventSubscriptions.TryAdd( eventType, 0 );
	}

	public static void UnsubscribeEvent( string eventType )
	{
		_eventSubscriptions.TryRemove( eventType, out _ );
	}

	public static bool HasEventSubscribers( string eventType )
	{
		return _eventSubscriptions.ContainsKey( eventType );
	}
}
