using McpBridge;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge.Tools;

[McpToolGroup("Media")]
public class MediaTools
{
	[McpTool("sbox_play_sound", "Plays a sound at a position or on a GameObject.")]
	public object PlaySound( string soundEvent, float x = 0, float y = 0, float z = 0 )
	{
		try { Sound.Play( soundEvent, new Vector3( x, y, z ) ); return new { success = true, soundEvent, position = new { x, y, z } }; } catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_stop_all_sounds", "Stops all playing sounds.")] public object StopAllSounds() { Sound.StopAll( 0 ); return new { success = true }; }

	[McpTool("sbox_set_camera_fov", "Changes the main camera field of view (10-170 degrees).")]
	public object SetCameraFov( float fov )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		var cam = scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c.IsMainCamera ); if ( cam == null ) return new { error = "No main camera" };
		var clamped = Validation.Clamp( fov, 10f, 170f );
		cam.FieldOfView = clamped;
		if ( Math.Abs( clamped - fov ) > 0.01f ) return new { success = true, fieldOfView = clamped, warning = $"FOV clamped to {clamped} (10-170 range)" };
		return new { success = true, fieldOfView = clamped };
	}

	[McpTool("sbox_set_camera_position", "Moves the main camera to a position.")]
	public object SetCameraPosition( float x, float y, float z )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		var cam = scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c.IsMainCamera ); if ( cam == null ) return new { error = "No main camera" };
		cam.WorldPosition = new Vector3( x, y, z ); return new { success = true, position = new { x, y, z } };
	}

	[McpTool("sbox_reset_camera", "Resets the main camera to default position/rotation.")]
	public object ResetCamera()
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		var cam = scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c.IsMainCamera ); if ( cam == null ) return new { error = "No main camera" };
		cam.WorldPosition = Vector3.Zero; cam.WorldRotation = Rotation.Identity; cam.FieldOfView = 90f;
		return new { success = true, message = "Camera reset to origin" };
	}
}
