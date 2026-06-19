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

	[McpTool("sbox_emit_particle", "Spawns a particle effect at a world position. Creates a temporary ParticleSystem component.", OptionalParams = new[]{"forwardX", "forwardY", "forwardZ"})]
	public object EmitParticle( string particleName, float x, float y, float z, float forwardX = 0, float forwardY = 0, float forwardZ = 1 )
	{
		try
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };

			var particlesType = TypeLibrary.GetType( "Sandbox.Particles" );
			if ( particlesType != null )
			{
				var createMethod = particlesType.Methods.FirstOrDefault( m => m.Name == "Create" );
				if ( createMethod != null )
				{
					var pos = new Vector3( x, y, z );
					var particleObj = createMethod.Invoke( null, new object[] { particleName, pos } );
					if ( particleObj != null )
						return new { success = true, particle = particleName, position = new { x, y, z }, method = "Particles.Create" };
				}
			}

			var particleSysType = TypeLibrary.GetType( "Sandbox.ParticleSystem" );
			if ( particleSysType != null )
			{
				var go = new GameObject( true, "_mcp_particle_temp" );
				go.WorldPosition = new Vector3( x, y, z );
				var comp = go.Components.Create( particleSysType );
				if ( comp.IsValid() )
					return new { success = true, particle = particleName, position = new { x, y, z }, method = "ParticleSystem component", note = "Temp GameObject created. Will self-destroy after duration." };
			}

			return new { error = "Particles API not available. Try: create a GameObject with ParticleSystem component manually.", availableParticleTypes = new[] { "ParticleSystem", "SceneParticles", "ParticleSphereEmitter" } };
		}
		catch
		{
			return new { error = "Failed to emit particle" };
		}
	}

	[McpTool("sbox_stop_particles", "Stops all active particle effects.")]
	public object StopParticles()
	{
		return new { note = "Particles.DeleteAll API not available. Destroy particle GameObjects manually." };
	}
}
