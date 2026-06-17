using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge.Tools;

[McpToolGroup("Physics")]
public class PhysicsTools
{
	private const string TimescalePath = "mcp_state/_timescale.json";

	private static float LoadTimescale()
	{
		try
		{
			if ( PersistenceStore.Exists( TimescalePath ) )
			{
				var data = PersistenceStore.Load<TimescaleData>( TimescalePath );
				return data?.scale ?? 1f;
			}
		}
		catch { }
		return 1f;
	}

	private static void SaveTimescale( float scale )
	{
		PersistenceStore.EnsureDirectory( "mcp_state" );
		PersistenceStore.Save( TimescalePath, new TimescaleData { scale = scale } );
	}

	private class TimescaleData { public float scale { get; set; } }

	[McpTool("sbox_set_gravity", "Sets the scene's global gravity vector.")]
	public object SetGravity( float x = 0f, float y = 0f, float z = -800f )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		scene.PhysicsWorld.Gravity = new Vector3( x, y, z );
		return new { success = true, gravity = new Vector3( x, y, z ) };
	}

	[McpTool("sbox_get_gravity", "Returns the current global gravity vector.")]
	public object GetGravity() { var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" }; return new { success = true, gravity = scene.PhysicsWorld.Gravity }; }

	[McpTool("sbox_set_timescale", "Sets the global time scale (1.0 = normal speed).")]
	public object SetTimescale( float scale = 1f ) { var ts = Math.Max( 0.01f, scale ); SaveTimescale( ts ); try { ConsoleSystem.Run( "sv_timescale", ts ); } catch { } return new { success = true, timescale = ts }; }

	[McpTool("sbox_get_timescale", "Returns the current global time scale.")] public object GetTimescale() => new { success = true, timescale = LoadTimescale() };

	[McpTool("sbox_apply_impulse", "Applies a physics impulse to a GameObject's Rigidbody.")]
	public object ApplyImpulse( string guidStr, float x = 0f, float y = 0f, float z = 0f )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( go == null ) return new { error = "GameObject not found" };
		var rb = go.Components.Get<Rigidbody>(); if ( rb == null ) return new { error = "No Rigidbody" };
		rb.ApplyImpulse( new Vector3( x, y, z ) ); return new { success = true, gameObject = go.Name, impulse = new Vector3( x, y, z ) };
	}

	[McpTool("sbox_get_velocity", "Returns the velocity of a GameObject's Rigidbody.")]
	public object GetVelocity( string guidStr )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( go == null ) return new { error = "GameObject not found" };
		var rb = go.Components.Get<Rigidbody>(); if ( rb == null ) return new { error = "No Rigidbody" };
		return new { success = true, gameObject = go.Name, velocity = rb.Velocity, angularVelocity = rb.AngularVelocity };
	}

	[McpTool("sbox_raycast", "Casts a ray from start to end and returns hit info.")]
	public object Raycast( float ox, float oy, float oz, float tx, float ty, float tz )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		try { var start = new Vector3( ox, oy, oz ); var end = new Vector3( tx, ty, tz ); var tr = scene.Trace.Ray( start, end ).Run(); if ( !tr.Hit ) return new { success = true, hit = false, start, end }; return new { success = true, hit = true, position = tr.EndPosition, normal = tr.Normal, distance = tr.Distance, gameObject = tr.GameObject?.Name, gameObjectGuid = tr.GameObject?.Id.ToString() }; } catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_overlap_sphere", "Finds GameObjects with colliders in a sphere radius.")]
	public object OverlapSphere( float cx, float cy, float cz, float radius )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		try { var center = new Vector3( cx, cy, cz ); var radiusSq = radius * radius; var hits = new List<object>(); var seen = new HashSet<Guid>(); foreach ( var col in scene.GetAllComponents<Collider>() ) { if ( !col.IsValid() ) continue; var distSq = col.WorldPosition.DistanceSquared( center ); if ( distSq <= radiusSq && !seen.Contains( col.GameObject.Id ) ) { seen.Add( col.GameObject.Id ); hits.Add( new { name = col.GameObject.Name, guid = col.GameObject.Id, distance = MathF.Sqrt( distSq ) } ); } } return new { success = true, center, radius, count = hits.Count, hits }; } catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_get_physics_bodies", "Lists all GameObjects with a Rigidbody.")]
	public object GetPhysicsBodies( string filter = null )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		try { var bodies = scene.GetAllComponents<Rigidbody>().Where( rb => rb.IsValid() ).Select( rb => new { gameObject = rb.GameObject.Name, guid = rb.GameObject.Id, mass = rb.Mass, speed = rb.Velocity.Length } ); if ( !string.IsNullOrEmpty( filter ) ) bodies = bodies.Where( b => b.gameObject.IndexOf( filter, StringComparison.OrdinalIgnoreCase ) >= 0 ); return new { success = true, count = bodies.Count(), bodies = bodies.ToList() }; } catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_set_physics_material", "Sets physics damping/mass on a Rigidbody.")]
	public object SetPhysicsMaterial( string guidStr, string linearDamping = null, string angularDamping = null, string mass = null )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var rb = go.Components.Get<Rigidbody>(); if ( !rb.IsValid() ) return new { error = "No Rigidbody" };
		if ( linearDamping != null && float.TryParse( linearDamping, out var ld ) ) rb.LinearDamping = Math.Max( 0f, ld );
		if ( angularDamping != null && float.TryParse( angularDamping, out var ad ) ) rb.AngularDamping = Math.Max( 0f, ad );
		return new { success = true, linearDamping = rb.LinearDamping, angularDamping = rb.AngularDamping, mass = rb.Mass };
	}

	[McpTool("sbox_add_force", "Applies an impulse force to a Rigidbody.")]
	public object AddForce( string guidStr, float dx = 0f, float dy = 0f, float dz = 0f, float magnitude = 100f )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var rb = go.Components.Get<Rigidbody>(); if ( !rb.IsValid() ) return new { error = "No Rigidbody" };
		var dir = new Vector3( dx, dy, dz ); if ( dir.Length < 0.001f ) dir = go.WorldRotation.Forward;
		rb.ApplyImpulse( dir.Normal * magnitude ); return new { success = true, force = dir.Normal * magnitude };
	}

	[McpTool("sbox_add_torque", "Applies angular impulse to a Rigidbody.")]
	public object AddTorque( string guidStr, float tx = 0f, float ty = 0f, float tz = 0f )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var rb = go.Components.Get<Rigidbody>(); if ( !rb.IsValid() ) return new { error = "No Rigidbody" };
		rb.AngularVelocity += new Vector3( tx, ty, tz ); return new { success = true, torque = new Vector3( tx, ty, tz ) };
	}
}
