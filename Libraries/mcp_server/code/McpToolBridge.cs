using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Sandbox;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace McpBridge;

public sealed record BridgeRegistration(
	string Name,
	Func<string, string, Task<string>> OnToolRequest,
	Func<Task<string>> OnListGameTools,
	Func<Task<string>> OnStateSnapshot,
	string ToolPrefix = null,
	string Version = "1.0.0",
	int ToolCount = 0,
	string[] Capabilities = null,
	int Priority = 0
);

public static class McpToolBridge
{
	private static readonly List<BridgeRegistration> _bridges = new();
	private static readonly ConcurrentDictionary<string, byte> _globalToolNames = new();
	private static readonly ConcurrentDictionary<string, int> _bridgeErrors = new();
	private static readonly ConcurrentDictionary<string, string> _prefixOwners = new();

	public static void RegisterGlobalToolName( string name )
	{
		_globalToolNames.TryAdd( name, 0 );
		OnToolsChanged?.Invoke();
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

			// Detect and warn on prefix conflicts
			if ( !string.IsNullOrEmpty( reg.ToolPrefix ) )
			{
				if ( _prefixOwners.TryGetValue( reg.ToolPrefix, out var owner ) && owner != reg.Name )
					Log.Warning( $"[MCP] WARNING: Bridge '{reg.Name}' claims tool prefix '{reg.ToolPrefix}' already owned by '{owner}'" );
				_prefixOwners[reg.ToolPrefix] = reg.Name;
			}
		}
		OnToolsChanged?.Invoke();
	}

	public static void Unregister( string name )
	{
		lock ( _bridges )
		{
			_bridges.RemoveAll( b => b.Name == name );

			// Clean up prefix ownership
			var keys = _prefixOwners.Where( kv => kv.Value == name ).Select( kv => kv.Key ).ToList();
			foreach ( var k in keys )
				_prefixOwners.TryRemove( k, out _ );
		}
		OnToolsChanged?.Invoke();
	}

	public static event Action OnToolsChanged;

	public static string CurrentProgressToken { get; set; } = "";
	public static event Action<double, double?, string> OnProgress;

	public static void ReportProgress( double progress, double? total = null, string message = null )
	{
		OnProgress?.Invoke( progress, total, message );
	}

	public static (string name, int errors, string version, int toolCount, string[] capabilities)[] GetBridgeStatus()
	{
		lock ( _bridges )
		{
			DecayBridgeErrors();
			return _bridges.Select( b => (b.Name, _bridgeErrors.GetValueOrDefault( b.Name, 0 ), b.Version ?? "1.0.0", b.ToolCount, b.Capabilities ?? Array.Empty<string>()) ).ToArray();
		}
	}

	private static readonly ConcurrentDictionary<string, DateTime> _bridgeErrorTimestamps = new();
	private const int MaxBridgeErrors = 5;
	private static readonly TimeSpan BridgeErrorDecayInterval = TimeSpan.FromSeconds( 30 );
	private static readonly object _decayLock = new();

	private static void RecordBridgeError( string name )
	{
		_bridgeErrors.AddOrUpdate( name, 1, ( _, c ) => c + 1 );
		_bridgeErrorTimestamps[name] = DateTime.UtcNow;
	}

	private static void DecayBridgeErrors()
	{
		lock ( _decayLock )
		{
			var cutoff = DateTime.UtcNow - BridgeErrorDecayInterval;
			foreach ( var kv in _bridgeErrorTimestamps.ToArray() )
			{
				if ( kv.Value < cutoff )
				{
					_bridgeErrorTimestamps.TryRemove( kv.Key, out _ );
					if ( _bridgeErrors.TryGetValue( kv.Key, out var count ) && count > 0 )
					{
						var decayed = Math.Max( 0, count - 1 );
						if ( decayed == 0 )
							_bridgeErrors.TryRemove( kv.Key, out _ );
						else
							_bridgeErrors.TryUpdate( kv.Key, decayed, count );
					}
				}
			}
		}
	}

	private static bool IsBridgeBlacklisted( string name ) => _bridgeErrors.TryGetValue( name, out var count ) && count >= MaxBridgeErrors;

	public static async Task<string> RouteToolRequest( string method, string args )
	{
		DecayBridgeErrors();
		BridgeRegistration[] snapshot;
		lock ( _bridges )
		{
			snapshot = _bridges.OrderByDescending( b => b.Priority ).ThenBy( b => b.Name ).ToArray();
		}

		// Phase 1: Try prefix-matched bridges first
		foreach ( var b in snapshot )
		{
			if ( b.OnToolRequest == null ) continue;
			if ( !string.IsNullOrEmpty( b.ToolPrefix ) && !method.StartsWith( b.ToolPrefix, StringComparison.OrdinalIgnoreCase ) )
				continue;
			if ( IsBridgeBlacklisted( b.Name ) ) continue;
			try
			{
				var result = await b.OnToolRequest( method, args );
				if ( result != null )
				{
					_bridgeErrors.TryRemove( b.Name, out _ );
					_bridgeErrorTimestamps.TryRemove( b.Name, out _ );
					return result;
				}
			}
			catch ( Exception e ) when ( e is not OperationCanceledException )
			{
				RecordBridgeError( b.Name );
			}
		}

		// Phase 2: Try all bridges as fallback (including those without prefix match)
		foreach ( var b in snapshot )
		{
			if ( b.OnToolRequest == null ) continue;
			if ( !string.IsNullOrEmpty( b.ToolPrefix ) && method.StartsWith( b.ToolPrefix, StringComparison.OrdinalIgnoreCase ) )
				continue; // already tried in phase 1
			if ( IsBridgeBlacklisted( b.Name ) ) continue;
			try
			{
				var result = await b.OnToolRequest( method, args );
				if ( result != null )
				{
					_bridgeErrors.TryRemove( b.Name, out _ );
					_bridgeErrorTimestamps.TryRemove( b.Name, out _ );
					return result;
				}
			}
			catch ( Exception e ) when ( e is not OperationCanceledException )
			{
				RecordBridgeError( b.Name );
			}
		}
		return null;
	}

	public static async Task<string> ListAllGameTools()
	{
		var allTools = new List<object>();
		BridgeRegistration[] snapshot;
		lock ( _bridges )
		{
			snapshot = _bridges.ToArray();
		}
		foreach ( var b in snapshot )
		{
			if ( b.OnListGameTools != null )
			{
				try
				{
					var result = await b.OnListGameTools();
					if ( !string.IsNullOrEmpty( result ) )
					{
						using var doc = JsonDocument.Parse( result );
						foreach ( var t in doc.RootElement.EnumerateArray() )
							allTools.Add( t );
					}
				}
				catch { }
			}
		}
		return allTools.Count > 0 ? JsonSerializer.Serialize( allTools ) : null;
	}

	public static async Task<string> GetAnyStateSnapshot()
	{
		BridgeRegistration[] snapshot;
		lock ( _bridges )
		{
			snapshot = _bridges.ToArray();
		}
		foreach ( var b in snapshot )
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
