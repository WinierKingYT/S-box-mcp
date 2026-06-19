using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge;

public static class NavMeshHelper
{
	private static TypeDescription _navMeshType;
	private static TypeDescription _navPathType;

	private static PropertyDescription _isLoadedProp;
	private static PropertyDescription _pointsProp;
	private static MethodDescription _closestPoint1;
	private static MethodDescription _closestPoint2;
	private static MethodDescription _buildPath;
	private static MethodDescription _randomPoint;
	private static MethodDescription _pointWithinRadius;

	private static bool Resolve()
	{
		if ( _navMeshType != null ) return true;

		try
		{
			_navMeshType = TypeLibrary.GetType( "Sandbox.NavMesh" );
			if ( _navMeshType == null )
				return false;

			_navPathType = TypeLibrary.GetType( "Sandbox.NavPath" );

			_isLoadedProp = _navMeshType.Properties.FirstOrDefault( p => p.Name == "IsLoaded" || p.Name == "Loaded" );

			var allMethods = _navMeshType.Methods;
			_closestPoint1 = allMethods.FirstOrDefault( m => m.Name == "GetClosestPoint" && m.Parameters.Length == 1 );
			_closestPoint2 = allMethods.FirstOrDefault( m => m.Name == "GetClosestPoint" && m.Parameters.Length >= 2 );
			_buildPath = allMethods.FirstOrDefault( m => m.Name == "BuildPath" && m.Parameters.Length == 2 );
			_randomPoint = allMethods.FirstOrDefault( m => m.Name == "GetRandomPoint" && m.Parameters.Length == 2 )
				?? allMethods.FirstOrDefault( m => ( m.Name == "GetRandomPoint" || m.Name == "RandomPoint" ) && m.Parameters.Length == 2 );
			_pointWithinRadius = allMethods.FirstOrDefault( m => m.Name == "GetPointWithinRadius" && m.Parameters.Length == 2 );

			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool IsAvailable
	{
		get
		{
			try { return Resolve() && _navMeshType != null; }
			catch { return false; }
		}
	}

	public static bool IsLoaded
	{
		get
		{
			if ( !Resolve() ) return false;
			try
			{
				if ( _isLoadedProp != null && _isLoadedProp.CanRead )
					return (bool)_isLoadedProp.GetValue( null );
				return false;
			}
			catch { return false; }
		}
	}

	public static Vector3? GetClosestPoint( Vector3 position )
	{
		if ( !Resolve() ) return null;
		try
		{
			if ( _closestPoint1 != null )
			{
				var result = _closestPoint1.Invoke( null, new object[] { position } );
				if ( result is Vector3 v ) return v;
			}
			if ( _closestPoint2 != null )
			{
				var result = _closestPoint2.Invoke( null, new object[] { position, 500f } );
				if ( result is Vector3 v ) return v;
			}
			return null;
		}
		catch { return null; }
	}

	public static List<Vector3> BuildPath( Vector3 start, Vector3 end )
	{
		if ( !Resolve() ) return null;
		try
		{
			if ( _buildPath != null )
			{
				var result = _buildPath.Invoke( null, new object[] { start, end } );
				return ExtractPathPoints( result );
			}

			var fallback = _navMeshType.Methods.FirstOrDefault( m => ( m.Name.Contains( "BuildPath" ) || m.Name.Contains( "GetPath" ) ) && m.Parameters.Length >= 2 );
			if ( fallback != null )
			{
				var args = new object[fallback.Parameters.Length];
				args[0] = start;
				args[1] = end;
				var result = fallback.Invoke( null, args );
				return ExtractPathPoints( result );
			}

			return null;
		}
		catch { return null; }
	}

	public static Vector3? GetRandomPoint( Vector3 center, float radius )
	{
		if ( !Resolve() ) return null;
		try
		{
			if ( _randomPoint != null )
			{
				var result = _randomPoint.Invoke( null, new object[] { center, radius } );
				if ( result is Vector3 v ) return v;
			}
			if ( _pointWithinRadius != null )
			{
				var result = _pointWithinRadius.Invoke( null, new object[] { center, radius } );
				if ( result is Vector3 v ) return v;
			}
			return null;
		}
		catch { return null; }
	}

	public static float GetPathDistance( List<Vector3> points )
	{
		if ( points == null || points.Count < 2 ) return 0f;
		var dist = 0f;
		for ( int i = 1; i < points.Count; i++ )
			dist += points[i - 1].Distance( points[i] );
		return dist;
	}

	private static List<Vector3> ExtractPathPoints( object pathObj )
	{
		if ( pathObj == null ) return null;

		if ( pathObj is List<Vector3> list ) return list;

		try
		{
			if ( _pointsProp == null && _navPathType != null )
				_pointsProp = _navPathType.Properties.FirstOrDefault( p => p.Name == "Points" );

			if ( _pointsProp != null && _pointsProp.CanRead )
			{
				var val = _pointsProp.GetValue( pathObj );
				if ( val is List<Vector3> ptList ) return ptList;
				if ( val is IEnumerable<Vector3> ptEnum ) return new List<Vector3>( ptEnum );
			}

			if ( pathObj is IEnumerable<Vector3> directEnum )
				return new List<Vector3>( directEnum );

			return null;
		}
		catch
		{
			return null;
		}
	}
}
