using McpBridge;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge.Tools;

[McpToolGroup("Game")]
public class GameTools
{
	[McpTool("sbox_find_carts", "Finds all cart GameObjects in the scene.")]
	public object FindCarts()
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		var carts = new List<object>();
		foreach ( var go in scene.Children )
		{
			if ( go.Name != null && go.Name.ToLowerInvariant().Contains( "cart" ) )
				carts.Add( new { guid = go.Id, name = go.Name, position = go.WorldPosition, rotation = go.WorldRotation } );
		}
		return new { carts, count = carts.Count };
	}

	[McpTool("sbox_get_cart_info", "Returns detailed info about a GameObject.")]
	public object GetCartInfo( string guidStr )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		return new { guid = go.Id, name = go.Name, position = go.WorldPosition, rotation = go.WorldRotation, scale = go.WorldScale, parent = go.Parent?.Name, children = go.Children.Select( c => c.Name ).ToList() };
	}

	[McpTool("sbox_spawn_cart", "Spawns a new GameObject at a position.")]
	public object SpawnCart( float x, float y, float z, string name = "MCP_Cart" )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		var go = new GameObject(); go.Name = name; go.WorldPosition = new Vector3( x, y, z );
		return new { success = true, guid = go.Id, name, position = new { x, y, z } };
	}

	[McpTool("sbox_remove_gameobject", "Removes a GameObject by GUID.")]
	public object RemoveGameObject( string guidStr )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		go.Destroy(); return new { success = true };
	}

	[McpTool("sbox_set_gameobject_position", "Sets the position of a GameObject.")]
	public object SetPosition( string guidStr, float x, float y, float z )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		go.WorldPosition = new Vector3( x, y, z ); return new { success = true, position = new { x, y, z } };
	}

	[McpTool("sbox_set_gameobject_rotation", "Sets the rotation (pitch,yaw,roll) of a GameObject.")]
	public object SetRotation( string guidStr, float pitch, float yaw, float roll )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		go.WorldRotation = Rotation.From( pitch, yaw, roll ); return new { success = true, rotation = new { pitch, yaw, roll } };
	}

	[McpTool("sbox_set_sun_direction", "Sets the DirectionalLight direction.")]
	public object SetSunDirection( float pitch, float yaw )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		var light = scene.GetAllComponents<DirectionalLight>().FirstOrDefault(); if ( !light.IsValid() ) return new { error = "No DirectionalLight" };
		light.WorldRotation = Rotation.From( pitch, yaw, 0f );
		return new { success = true, pitch, yaw };
	}

	[McpTool("sbox_list_macros", "Lists all saved macros.")]
	public object ListMacros()
	{
		var macros = McpMacroManager.List();
		return new { macros = macros.Select( m => new { name = m.Name, description = m.Description, steps = m.Steps?.Count ?? 0 } ).ToList(), count = macros.Count };
	}

	[McpTool("sbox_get_macro", "Gets a saved macro by name.")]
	public object GetMacro( string name )
	{
		var macro = McpMacroManager.Get( name );
		if ( macro == null ) return new { error = $"Macro not found: {name}" };
		return new { success = true, name = macro.Name, description = macro.Description, steps = macro.Steps };
	}

	[McpTool("sbox_save_macro", "Saves a macro (list of tool steps).")]
	public object SaveMacro( string name, string description = "" )
	{
		var steps = new List<MacroStep>();
		var history = McpReplay.GetHistory( 100 );
		foreach ( var record in history )
			steps.Add( new MacroStep { Method = record.Method, Params = record.Input } );
		McpMacroManager.Save( new MacroData { Name = name, Description = description, Steps = steps } );
		return new { success = true, name, steps = steps.Count };
	}

	[McpTool("sbox_delete_macro", "Deletes a saved macro.")]
	public object DeleteMacro( string name )
	{
		McpMacroManager.Delete( name );
		return new { success = true, name };
	}

	[McpTool("sbox_list_snapshots", "Lists all saved scene snapshots.")]
	public object ListSnapshots()
	{
		var snapshots = McpSnapshotManager.List();
		return new { snapshots, count = snapshots.Count };
	}

	[McpTool("sbox_health", "Returns health metrics for the MCP server.")]
	public object Health()
	{
		var analytics = McpReplay.GetAnalytics();
		var config = McpConfig.Load();
		return new
		{
			status = "ok",
			totalCalls = analytics["totalCalls"],
			errorCount = analytics["errorCount"],
			avgDurationMs = analytics["avgDurationMs"],
			uniqueMethods = analytics["uniqueMethods"],
			macros = McpMacroManager.List().Count,
			port = config.Port
		};
	}

	[McpTool("sbox_metrics", "Returns per-tool performance metrics.")]
	public object Metrics()
	{
		return McpReplay.GetAnalytics();
	}

	[McpTool("sbox_clear_replay", "Clears all replay history.")]
	public object ClearReplay() { McpReplay.Clear(); return new { success = true }; }

	[McpTool("sbox_replay_playback", "Starts replaying recorded commands.")]
	public object ReplayPlayback()
	{
		McpReplay.StartPlayback();
		return new { success = true, message = "Playback started" };
	}
}
