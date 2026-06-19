using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge;

public static class Validation
{
	public static Guid? ParseGuid( string guidStr )
	{
		if ( Guid.TryParse( guidStr, out var guid ) )
			return guid;
		return null;
	}

	public static object Error( string message ) => new { error = message };
	public static object Success( object data = null ) => data != null ? new { success = true, data } : new { success = true };

	public static object RequireScene( out Scene scene )
	{
		scene = Game.ActiveScene;
		if ( scene == null ) return Error( "No active scene" );
		return null;
	}

	public static object RequireGuid( string guidStr, out Guid guid )
	{
		if ( Guid.TryParse( guidStr, out guid ) )
			return null;
		return Error( $"Invalid GUID format: '{guidStr}'" );
	}

	public static object RequireObject( Scene scene, string guidStr, out GameObject go )
	{
		go = null;
		var guid = ParseGuid( guidStr );
		if ( guid == null ) return Error( $"Invalid GUID: '{guidStr}'" );
		go = scene.Directory.FindByGuid( guid.Value );
		if ( !go.IsValid() ) return Error( $"GameObject not found: '{guidStr}'" );
		return null;
	}

	public static object RequireValid( bool condition, string message )
	{
		return condition ? null : Error( message );
	}

	public static float Clamp( float val, float min, float max )
	{
		return Math.Max( min, Math.Min( max, val ) );
	}

	public static object RequireComponent<T>( GameObject go, out T comp ) where T : class
	{
		comp = go.Components.Get<T>();
		if ( comp == null ) return Error( $"Required component {typeof( T ).Name} not found on '{go.Name}'" );
		return null;
	}
}
