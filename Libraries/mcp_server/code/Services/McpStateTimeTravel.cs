using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge;

public class StateSnapshot
{
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;
	public Dictionary<Guid, Vector3> ObjectPositions { get; set; } = new();
	public Dictionary<Guid, Rotation> ObjectRotations { get; set; } = new();
	public Dictionary<string, object> GameVariables { get; set; } = new();
}

public static class McpStateTimeTravel
{
	private static readonly int MaxSnapshots = 60;
	private static readonly StateSnapshot[] _snapshots = new StateSnapshot[60];
	private static int _writeIndex = 0;
	private static int _snapshotCount = 0;
	private static DateTime _lastCaptureTime = DateTime.MinValue;
	private static readonly Dictionary<string, System.Reflection.PropertyInfo> _propertyCache = new();

	static McpStateTimeTravel()
	{
		for ( int i = 0; i < MaxSnapshots; i++ )
		{
			_snapshots[i] = new StateSnapshot();
		}
	}

	private static System.Reflection.PropertyInfo GetCachedProperty( Type type, string propName )
	{
		var key = $"{type.FullName}_{propName}";
		if ( !_propertyCache.TryGetValue( key, out var prop ) )
		{
			prop = type.GetProperty( propName );
			_propertyCache[key] = prop;
		}
		return prop;
	}

	public static void Tick()
	{
		var now = DateTime.UtcNow;
		if ( (now - _lastCaptureTime).TotalSeconds < 1.0 ) return;
		_lastCaptureTime = now;

		Capture();
	}

	private static void Capture()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return;

		// Select the next pre-allocated snapshot in the ring buffer
		var snap = _snapshots[_writeIndex];

		// Clear internal dictionaries to reuse their memory buffers without GC allocation
		snap.Timestamp = DateTime.UtcNow;
		snap.ObjectPositions.Clear();
		snap.ObjectRotations.Clear();
		snap.GameVariables.Clear();

		foreach ( var go in scene.GetAllObjects( true ) )
		{
			if ( !go.IsValid() ) continue;
			if ( go.Name == "GameObject" && !go.Components.GetAll<Component>().Any( c => c.GetType().Name.Contains( "Cart" ) || c.GetType().Name.Contains( "Bot" ) || c.GetType().Name.Contains( "Controller" ) ) )
				continue;

			snap.ObjectPositions[go.Id] = go.WorldPosition;
			snap.ObjectRotations[go.Id] = go.WorldRotation;
		}

		var gm = GetDynamicComponent( "BlackFridayGameManager" );
		if ( gm != null )
		{
			var t = gm.GetType();
			snap.GameVariables["GameManager_Cash"] = GetCachedProperty( t, "Cash" )?.GetValue( gm );
			snap.GameVariables["GameManager_Phase"] = GetCachedProperty( t, "Phase" )?.GetValue( gm );
		}

		var qm = GetDynamicComponent( "QuotaManager" );
		if ( qm != null )
		{
			var t = qm.GetType();
			snap.GameVariables["QuotaManager_CurrentQuota"] = GetCachedProperty( t, "CurrentQuota" )?.GetValue( qm );
			snap.GameVariables["QuotaManager_Collected"] = GetCachedProperty( t, "Collected" )?.GetValue( qm );
		}

		// Advance ring buffer write head
		_writeIndex = (_writeIndex + 1) % MaxSnapshots;
		_snapshotCount = Math.Min( _snapshotCount + 1, MaxSnapshots );
	}

	public static object Rewind( float secondsAgo )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };

		if ( _snapshotCount == 0 )
			return new { error = "No time travel snapshots recorded yet" };

		var targetTime = DateTime.UtcNow - TimeSpan.FromSeconds( secondsAgo );
		
		StateSnapshot closest = null;
		double minDiff = double.MaxValue;
		
		for ( int i = 0; i < _snapshotCount; i++ )
		{
			var snap = _snapshots[i];
			double diff = Math.Abs( (snap.Timestamp - targetTime).TotalMilliseconds );
			if ( diff < minDiff )
			{
				minDiff = diff;
				closest = snap;
			}
		}

		if ( closest == null )
			return new { error = "No matching state snapshot found" };

		int objectsRestored = 0;
		foreach ( var kv in closest.ObjectPositions )
		{
			var go = scene.Directory.FindByGuid( kv.Key );
			if ( go.IsValid() )
			{
				go.WorldPosition = kv.Value;
				if ( closest.ObjectRotations.TryGetValue( kv.Key, out var rot ) )
				{
					go.WorldRotation = rot;
				}
				objectsRestored++;
			}
		}

		int variablesRestored = 0;
		foreach ( var kv in closest.GameVariables )
		{
			if ( kv.Key.StartsWith( "GameManager_" ) )
			{
				var gm = GetDynamicComponent( "BlackFridayGameManager" );
				if ( gm != null )
				{
					var propName = kv.Key.Substring( 12 );
					GetCachedProperty( gm.GetType(), propName )?.SetValue( gm, kv.Value );
					variablesRestored++;
				}
			}
			else if ( kv.Key.StartsWith( "QuotaManager_" ) )
			{
				var qm = GetDynamicComponent( "QuotaManager" );
				if ( qm != null )
				{
					var propName = kv.Key.Substring( 13 );
					GetCachedProperty( qm.GetType(), propName )?.SetValue( qm, kv.Value );
					variablesRestored++;
				}
			}
		}

		var actualSeconds = (DateTime.UtcNow - closest.Timestamp).TotalSeconds;

		return new
		{
			success = true,
			requestedSecondsAgo = secondsAgo,
			actualSecondsAgo = Math.Round( actualSeconds, 1 ),
			objectsRestored,
			variablesRestored
		};
	}

	private static Component GetDynamicComponent( string typeName )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return null;
		foreach ( var c in scene.GetAllComponents<Component>() )
		{
			if ( c.IsValid() && c.GetType().Name == typeName )
				return c;
		}
		return null;
	}
}
