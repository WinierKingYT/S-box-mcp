using McpBridge;
using Sandbox;
using System;
using System.Linq;

namespace McpBridge.Tools;

[McpToolGroup("Effects")]
public class EffectsTools
{
	private static float _masterVolume = 1f;

	[McpTool("sbox_set_master_volume", "Set master volume (0.0 = silent, 1.0 = full).")]
	public object SetMasterVolume( float volume )
	{
		var clamped = Validation.Clamp( volume, 0f, 1f );
		_masterVolume = clamped;
		try
		{
			var mixerType = TypeLibrary.GetType( "Sandbox.Mixer" );
			if ( mixerType != null )
			{
				var masterProp = mixerType.Properties.FirstOrDefault( p => p.Name == "MasterVolume" && p.CanWrite );
				masterProp?.SetValue( null, clamped );
			}
		}
		catch { }
		return new { success = true, masterVolume = clamped };
	}

	[McpTool("sbox_get_master_volume", "Returns current master volume level.")]
	public object GetMasterVolume()
	{
		try
		{
			var mixerType = TypeLibrary.GetType( "Sandbox.Mixer" );
			if ( mixerType != null )
			{
				var masterProp = mixerType.Properties.FirstOrDefault( p => p.Name == "MasterVolume" && p.CanRead );
				if ( masterProp != null ) return new { success = true, masterVolume = masterProp.GetValue( null ) };
			}
		}
		catch { }
		return new { success = true, masterVolume = _masterVolume };
	}

	[McpTool("sbox_emit_particle", "Spawns a particle effect at a world position.")]
	public object EmitParticle( string particleName, float x, float y, float z, float forwardX = 0, float forwardY = 0, float forwardZ = 1 )
	{
		try
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };
			var pos = new Vector3( x, y, z );
			var dir = new Vector3( forwardX, forwardY, forwardZ );

			var particlesType = TypeLibrary.GetType( "Sandbox.Particles" );
			if ( particlesType == null ) return new { error = "Particles API not available in this SDK version" };
			var createMethod = particlesType.Methods.FirstOrDefault( m => m.Name == "Create" );
			if ( createMethod == null ) return new { error = "Particles.Create method not found" };
			var particleObj = createMethod.Invoke( null, new object[] { particleName, pos } );
			if ( particleObj == null ) return new { error = $"Particle system '{particleName}' not found" };

			if ( dir.Length > 0.001f )
			{
				var pTd = TypeLibrary.GetType( particleObj.GetType() );
				pTd?.Methods.FirstOrDefault( m => m.Name == "SetOrientation" )?.Invoke( particleObj, new object[] { Rotation.LookAt( dir ) } );
			}
			return new { success = true, particle = particleName, position = new { x, y, z } };
		}
		catch ( Exception e )
		{
			return new { error = $"Failed to emit particle: {e.Message}" };
		}
	}

	[McpTool("sbox_stop_particles", "Stops all active particle effects.")]
	public object StopParticles()
	{
		try
		{
			var particlesType = TypeLibrary.GetType( "Sandbox.Particles" );
			if ( particlesType == null ) return new { error = "Particles API not available" };
			var deleteAll = particlesType.Methods.FirstOrDefault( m => m.Name == "DeleteAll" );
			if ( deleteAll != null ) deleteAll.Invoke( null, Array.Empty<object>() );
			return new { success = true };
		}
		catch ( Exception e ) { return new { error = e.Message }; }
	}
}
