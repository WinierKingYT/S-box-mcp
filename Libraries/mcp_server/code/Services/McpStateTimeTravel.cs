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
	private static readonly Queue<StateSnapshot> _buffer = new();
	private static readonly int MaxSnapshots = 60;
	private static DateTime _lastCaptureTime = DateTime.MinValue;

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

		var snap = new StateSnapshot();

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
			snap.GameVariables["GameManager_Cash"] = t.GetProperty( "Cash" )?.GetValue( gm );
			snap.GameVariables["GameManager_Phase"] = t.GetProperty( "Phase" )?.GetValue( gm );
		}

		var qm = GetDynamicComponent( "QuotaManager" );
		if ( qm != null )
		{
			var t = qm.GetType();
			snap.GameVariables["QuotaManager_CurrentQuota"] = t.GetProperty( "CurrentQuota" )?.GetValue( qm );
			snap.GameVariables["QuotaManager_Collected"] = t.GetProperty( "Collected" )?.GetValue( qm );
		}

		_buffer.Enqueue( snap );
		if ( _buffer.Count > MaxSnapshots )
		{
			_buffer.Dequeue();
		}
	}

	public static object Rewind( float secondsAgo )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };

		if ( _buffer.Count == 0 )
			return new { error = "No time travel snapshots recorded yet" };

		var targetTime = DateTime.UtcNow - TimeSpan.FromSeconds( secondsAgo );
		
		StateSnapshot closest = null;
		double minDiff = double.MaxValue;
		foreach ( var snap in _buffer )
		{
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
					gm.GetType().GetProperty( propName )?.SetValue( gm, kv.Value );
					variablesRestored++;
				}
			}
			else if ( kv.Key.StartsWith( "QuotaManager_" ) )
			{
				var qm = GetDynamicComponent( "QuotaManager" );
				if ( qm != null )
				{
					var propName = kv.Key.Substring( 13 );
					qm.GetType().GetProperty( propName )?.SetValue( qm, kv.Value );
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
