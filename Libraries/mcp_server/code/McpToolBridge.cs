using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace McpBridge;

public sealed record BridgeRegistration( string Name, Func<string, string, Task<string>> OnToolRequest, Func<Task<string>> OnListGameTools, Func<Task<string>> OnStateSnapshot );

public static class McpToolBridge
{
	private static readonly List<BridgeRegistration> _bridges = new();
	private static BridgeRegistration[] _snapshot = Array.Empty<BridgeRegistration>();
	private static readonly ConcurrentDictionary<string, byte> _globalToolNames = new();

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

	public static async Task<string> RouteToolRequest( string method, string args )
	{
		foreach ( var b in _snapshot )
		{
			if ( b.OnToolRequest != null )
			{
				try { var result = await b.OnToolRequest( method, args ); if ( result != null ) return result; } catch { }
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
