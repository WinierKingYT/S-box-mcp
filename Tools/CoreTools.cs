using McpBridge;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace McpBridge.Tools;

[McpToolGroup("Core")]
public class CoreTools
{
	private static readonly Dictionary<string, string[]> AssetExtensions = new()
	{
		["prefab"] = new[] { ".prefab" },
		["model"] = new[] { ".vmdl" },
		["material"] = new[] { ".vmat" },
		["sound"] = new[] { ".vsnd", ".vsndevts", ".wav", ".mp3" },
		["texture"] = new[] { ".png", ".jpg", ".tga", ".vtex" },
		["animation"] = new[] { ".vanim" },
		["all"] = new[] { ".prefab", ".vmdl", ".vmat", ".vsnd", ".vsndevts", ".wav", ".mp3", ".png", ".jpg", ".tga", ".vtex", ".vanim" }
	};

	[McpTool("sbox_run_command", "Executes a console command in the S&box engine.")]
	public object RunCommand( string command )
	{
		try { ConsoleSystem.Run( command ); return new { success = true, command }; }
		catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_get_scene_hierarchy", "Returns scene hierarchy: all GameObjects with name, GUID, child count, component list.")]
	public object GetSceneHierarchy()
	{
		var activeScene = Game.ActiveScene;
		if ( activeScene == null ) return new { error = "No active scene" };
		return new { sceneName = activeScene.Name, rootObjects = activeScene.Children.Select( MapGameObject ).ToList() };
	}

	[McpTool("sbox_get_object_details", "Returns detailed information about a specific GameObject.")]
	public object GetObjectDetails( string guidStr )
	{
		var activeScene = Game.ActiveScene;
		if ( activeScene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid format" };
		var go = activeScene.Directory.FindByGuid( guid );
		if ( go == null ) return new { error = "GameObject not found" };
		var components = go.Components.GetAll<Component>().Select( c => new { type = c.GetType().Name, enabled = c.Enabled } ).ToList();
		return new { id = go.Id, name = go.Name, worldPosition = go.WorldPosition, worldRotation = go.WorldRotation.Angles(), components };
	}

	[McpTool("sbox_destroy_gameobject", "Destroys a GameObject by GUID. Supports undo via sbox_undo.")]
	public object DestroyGameObject( string guidStr )
	{
		var activeScene = Game.ActiveScene;
		if ( activeScene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid format" };
		var go = activeScene.Directory.FindByGuid( guid );
		if ( go == null ) return new { error = "GameObject not found" };
		var name = go.Name;
		LogicWeaver.Fire( "destroyed", guidStr );
		go.Destroy();
		UndoRedoManager.Record( "destroy_gameobject", $"Destroyed {name}",
			undoFn: () => new { warning = "Full undo of destroy requires a prefab reference" },
			redoFn: () => { var s = Game.ActiveScene; if ( s == null ) return new { error = "No active scene" }; var g = s.Directory.FindByGuid( guid ); if ( g.IsValid() ) g.Destroy(); return new { success = true, message = $"Redestroyed {name}" }; } );
		return new { success = true, message = $"Destroyed {name}" };
	}

	[McpTool("sbox_set_transform", "Sets the transform of a GameObject with undo support.")]
	public object SetTransform(
		string guidStr,
		string position = null,
		string rotation = null,
		string scale = null )
	{
		var activeScene = Game.ActiveScene;
		if ( activeScene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = activeScene.Directory.FindByGuid( guid );
		if ( go == null ) return new { error = "GameObject not found" };
		var oldPos = go.WorldPosition; var oldRot = go.WorldRotation; var oldScale = go.LocalScale;
		if ( position != null ) { var p = position.Split( ',' ); if ( p.Length == 3 && float.TryParse( p[0], out var x ) && float.TryParse( p[1], out var y ) && float.TryParse( p[2], out var z ) ) go.WorldPosition = new Vector3( x, y, z ); }
		if ( rotation != null ) { var p = rotation.Split( ',' ); if ( p.Length == 3 && float.TryParse( p[0], out var p1 ) && float.TryParse( p[1], out var y ) && float.TryParse( p[2], out var r ) ) go.WorldRotation = new Angles( p1, y, r ).ToRotation(); }
		if ( scale != null ) { var p = scale.Split( ',' ); if ( p.Length == 3 && float.TryParse( p[0], out var x ) && float.TryParse( p[1], out var y ) && float.TryParse( p[2], out var z ) ) go.LocalScale = new Vector3( x, y, z ); }
		var newPos = go.WorldPosition; var newRot = go.WorldRotation; var newScale = go.LocalScale;
		UndoRedoManager.Record( "set_transform", $"Moved {go.Name}",
			undoFn: () => { var s = Game.ActiveScene; if ( s == null ) return new { error = "No active scene" }; var g = s.Directory.FindByGuid( guid ); if ( !g.IsValid() ) return new { error = "Object gone" }; g.WorldPosition = oldPos; g.WorldRotation = oldRot; g.LocalScale = oldScale; return new { success = true, message = $"Restored {g.Name}" }; },
			redoFn: () => { var s = Game.ActiveScene; if ( s == null ) return new { error = "No active scene" }; var g = s.Directory.FindByGuid( guid ); if ( !g.IsValid() ) return new { error = "Object gone" }; g.WorldPosition = newPos; g.WorldRotation = newRot; g.LocalScale = newScale; return new { success = true, message = $"Reapplied transform on {g.Name}" }; } );
		return new { success = true, name = go.Name, position = go.WorldPosition, rotation = go.WorldRotation.Angles(), scale = go.LocalScale };
	}

	[McpTool("sbox_spawn_prefab", "Spawns a prefab at the given position with undo support.")]
	public object SpawnPrefab( string prefabPath, string position = null )
	{
		var activeScene = Game.ActiveScene;
		if ( activeScene == null ) return new { error = "No active scene" };
		var prefab = ResourceLibrary.Get<PrefabFile>( prefabPath );
		if ( prefab == null ) return new { error = $"Prefab not found: {prefabPath}" };
		var go = SceneUtility.GetPrefabScene( prefab ).Clone();
		go.SetParent( activeScene );
		if ( position != null ) { var p = position.Split( ',' ); if ( p.Length == 3 && float.TryParse( p[0], out var x ) && float.TryParse( p[1], out var y ) && float.TryParse( p[2], out var z ) ) go.WorldPosition = new Vector3( x, y, z ); }
		var spawnedGuid = go.Id; var spawnedName = go.Name;
		UndoRedoManager.Record( "spawn_prefab", $"Spawned {spawnedName}",
			undoFn: () => { var s = Game.ActiveScene; if ( s == null ) return new { error = "No active scene" }; var g = s.Directory.FindByGuid( spawnedGuid ); if ( g.IsValid() ) { g.Destroy(); return new { success = true, message = $"Destroyed {spawnedName}" }; } return new { warning = "Already destroyed" }; },
			redoFn: () => { var s = Game.ActiveScene; if ( s == null ) return new { error = "No active scene" }; var p2 = ResourceLibrary.Get<PrefabFile>( prefabPath ); if ( p2 == null ) return new { error = "Prefab not found" }; var g = SceneUtility.GetPrefabScene( p2 ).Clone(); g.SetParent( s ); g.WorldPosition = go.WorldPosition; return new { success = true, guid = g.Id, name = g.Name }; } );
		return new { success = true, guid = go.Id, name = go.Name };
	}

	[McpTool("sbox_list_assets", "Lists project assets by type.")]
	public object ListAssets( string assetType = "all" )
	{
		if ( !AssetExtensions.TryGetValue( assetType, out var exts ) ) return new { error = $"Unknown type '{assetType}'" };
		var results = new Dictionary<string, List<string>>();
		foreach ( var ext in exts ) { try { var files = FileSystem.Mounted.FindFile( ".", $"*{ext}", true ); results[ext.TrimStart('.')] = files.ToList(); } catch ( Exception e ) { results[ext] = new() { $"error: {e.Message}" }; } }
		return new { assetType, total = results.Sum( kv => kv.Value.Count ), byExtension = results };
	}

	[McpTool("sbox_get_property", "Reads a property value from a component on a GameObject.")]
	public object GetProperty( string guidStr, string componentType, string propertyName )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( go == null ) return new { error = "GameObject not found" };
		var typeDesc = TypeLibrary.GetType( componentType ); if ( typeDesc == null ) return new { error = $"Type '{componentType}' not found" };
		var comp = go.Components.Get( typeDesc.TargetType ); if ( comp == null ) return new { error = $"{componentType} not found" };
		var prop = typeDesc.Properties.FirstOrDefault( p => p.Name == propertyName && p.CanRead );
		if ( prop == null ) return new { error = $"Property '{propertyName}' not found" };
		try { return new { success = true, value = FormatValue( prop.GetValue( comp ) ) }; } catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_set_property", "Sets a component property by name. Value is parsed to the correct type automatically.")]
	public object SetProperty( string guidStr, string componentType, string propertyName, string value )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( go == null ) return new { error = "GameObject not found" };
		var typeDesc = TypeLibrary.GetType( componentType ); if ( typeDesc == null ) return new { error = $"Type '{componentType}' not found" };
		var comp = go.Components.Get( typeDesc.TargetType ); if ( comp == null ) return new { error = $"{componentType} not found" };
		var prop = typeDesc.Properties.FirstOrDefault( p => p.Name == propertyName && p.CanWrite );
		if ( prop == null ) return new { error = $"Property '{propertyName}' not found" };
		try { var converted = ConvertValue( value, prop.PropertyType ); prop.SetValue( comp, converted ); return new { success = true, newValue = FormatValue( converted ) }; } catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_add_component", "Adds a component to a GameObject.")]
	public object AddComponent( string guidStr, string componentType )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var typeDesc = FindComponentType( componentType ); if ( typeDesc == null ) return new { error = $"Component type '{componentType}' not found" };
		if ( go.Components.Get( typeDesc.TargetType ).IsValid() ) return new { error = $"{componentType} already exists" };
		var comp = go.Components.Create( typeDesc ); if ( !comp.IsValid() ) return new { error = $"Failed to create {componentType}" };
		return new { success = true, gameObject = go.Name, component = componentType };
	}

	[McpTool("sbox_remove_component", "Removes a component from a GameObject.")]
	public object RemoveComponent( string guidStr, string componentType )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var typeDesc = FindComponentType( componentType ); if ( typeDesc == null ) return new { error = $"Component type '{componentType}' not found" };
		var comp = go.Components.Get( typeDesc.TargetType ); if ( !comp.IsValid() ) return new { error = $"{componentType} not found" };
		comp.Destroy(); return new { success = true, removed = componentType };
	}

	[McpTool("sbox_set_component_enabled", "Enables or disables a component on a GameObject.")]
	public object SetComponentEnabled( string guidStr, string componentType, string enabled )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		if ( !bool.TryParse( enabled, out var val ) ) return new { error = "enabled must be true or false" };
		var typeDesc = FindComponentType( componentType ); if ( typeDesc == null ) return new { error = $"Component type '{componentType}' not found" };
		var comp = go.Components.Get( typeDesc.TargetType ); if ( !comp.IsValid() ) return new { error = $"{componentType} not found" };
		comp.Enabled = val; return new { success = true, enabled = val };
	}

	[McpTool("sbox_list_components", "Lists all available component types.")]
	public object ListComponents( string filter = null )
	{
		try { var types = TypeLibrary.GetTypes<Component>(); var result = types.Where( t => filter == null || t.Name.IndexOf( filter, StringComparison.OrdinalIgnoreCase ) >= 0 ).Select( t => new { name = t.Name, fullName = t.FullName, baseType = t.BaseType?.Name } ).OrderBy( t => t.name ).ToList(); return new { success = true, count = result.Count, components = result }; } catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_duplicate_object", "Duplicates a GameObject.")]
	public object DuplicateObject( string guidStr, string newName = null, float offsetX = 0f, float offsetY = 0f, float offsetZ = 0f )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var clone = go.Clone(); clone.SetParent( go.Parent );
		if ( !string.IsNullOrEmpty( newName ) ) clone.Name = newName;
		var offset = new Vector3( offsetX, offsetY, offsetZ ); if ( offset.Length > 0f ) clone.WorldPosition = go.WorldPosition + offset;
		return new { success = true, originalGuid = guidStr, originalName = go.Name, newGuid = clone.Id, newName = clone.Name };
	}

	[McpTool("sbox_set_parent", "Changes the parent of a GameObject.")]
	public object SetParent( string childGuidStr, string parentGuidStr )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( childGuidStr, out var childGuid ) ) return new { error = "Invalid child Guid" };
		var child = scene.Directory.FindByGuid( childGuid ); if ( !child.IsValid() ) return new { error = "Child not found" };
		if ( string.IsNullOrEmpty( parentGuidStr ) || parentGuidStr == "null" ) { child.SetParent( null ); return new { success = true, child = child.Name, parent = "null (root)" }; }
		if ( !Guid.TryParse( parentGuidStr, out var parentGuid ) ) return new { error = "Invalid parent Guid" };
		var parent = scene.Directory.FindByGuid( parentGuid ); if ( !parent.IsValid() ) return new { error = "Parent not found" };
		if ( childGuid == parentGuid ) return new { error = "Cannot parent to itself" };
		child.SetParent( parent ); return new { success = true, child = child.Name, parent = parent.Name };
	}

	[McpTool("sbox_run_sequence", "Execute multiple tools in sequence. Steps is JSON array of { tool, params } objects.")]
	public object RunSequence( string steps )
	{
		try
		{
			var stepList = JsonSerializer.Deserialize<List<JsonElement>>( steps );
			if ( stepList == null ) return new { error = "Invalid steps JSON" };
			var results = new List<object>();
			foreach ( var step in stepList )
			{
				var tool = step.GetProperty( "tool" ).GetString();
				var args = step.TryGetProperty( "params", out var p ) ? p.GetRawText() : "{}";
				try { results.Add( new { tool, status = "ok", result = McpToolDispatcher.Instance.RunSingle( tool, args ) } ); }
				catch ( Exception ex ) { results.Add( new { tool, status = "error", error = ex.Message } ); }
			}
			return new { success = true, stepCount = results.Count, results };
		}
		catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_run_batch", "Execute multiple tools sequentially. All steps run regardless of errors. Steps is JSON array of { tool, params }.")]
	public object RunBatch( string steps )
	{
		try
		{
			var stepList = JsonSerializer.Deserialize<List<JsonElement>>( steps );
			if ( stepList == null ) return new { error = "Invalid steps JSON" };
			var results = new List<object>();
			foreach ( var step in stepList )
			{
				var tool = step.GetProperty( "tool" ).GetString();
				var args = step.TryGetProperty( "params", out var p ) ? p.GetRawText() : "{}";
				try { results.Add( new { tool, status = "ok", result = McpToolDispatcher.Instance.RunSingle( tool, args ) } ); }
				catch ( Exception ex ) { results.Add( new { tool, status = "error", error = ex.Message } ); }
			}
			return new { success = true, stepCount = results.Count, results };
		}
		catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_batch_delete", "Delete multiple GameObjects by GUID. Takes a JSON array of GUID strings.")]
	public object BatchDelete( string guids )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var list = JsonSerializer.Deserialize<List<string>>( guids );
		if ( list == null ) return new { error = "Invalid GUIDs JSON array" };
		var results = new List<object>();
		foreach ( var idStr in list )
		{
			if ( Guid.TryParse( idStr, out var guid ) )
			{
				var go = scene.Directory.FindByGuid( guid );
				if ( go != null ) { results.Add( new { id = idStr, name = go.Name, deleted = true } ); go.Destroy(); }
				else results.Add( new { id = idStr, error = "Not found" } );
			}
			else results.Add( new { id = idStr, error = "Invalid GUID" } );
		}
		return new { success = true, count = results.Count, results };
	}

	[McpTool("sbox_batch_transform", "Set transform on multiple GameObjects. Takes a JSON array of { id, x?, y?, z?, pitch?, yaw?, roll?, scale? }.")]
	public object BatchTransform( string transforms )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var list = JsonSerializer.Deserialize<List<JsonElement>>( transforms );
		if ( list == null ) return new { error = "Invalid transforms JSON array" };
		var results = new List<object>();
		foreach ( var item in list )
		{
			try
			{
				var idStr = item.GetProperty( "id" ).GetString();
				if ( !Guid.TryParse( idStr, out var guid ) ) { results.Add( new { id = idStr, error = "Invalid GUID" } ); continue; }
				var go = scene.Directory.FindByGuid( guid );
				if ( go == null ) { results.Add( new { id = idStr, error = "Not found" } ); continue; }

				if ( item.TryGetProperty( "x", out var xp ) && item.TryGetProperty( "y", out var yp ) )
				{
					var z = item.TryGetProperty( "z", out var zp ) ? zp.GetSingle() : 0f;
					go.WorldPosition = new Vector3( xp.GetSingle(), yp.GetSingle(), z );
				}
				if ( item.TryGetProperty( "pitch", out var pitch ) && item.TryGetProperty( "yaw", out var yaw ) )
				{
					var roll = item.TryGetProperty( "roll", out var r ) ? r.GetSingle() : 0f;
					go.WorldRotation = Rotation.From( pitch.GetSingle(), yaw.GetSingle(), roll );
				}
				if ( item.TryGetProperty( "scale", out var scale ) )
					go.LocalScale = scale.GetSingle();

				results.Add( new { id = idStr, name = go.Name, position = new { x = go.WorldPosition.x, y = go.WorldPosition.y, z = go.WorldPosition.z }, success = true } );
			}
			catch ( Exception e ) { results.Add( new { error = e.Message } ); }
		}
		return new { success = true, count = results.Count, results };
	}

	[McpTool("sbox_undo", "Undoes the last action.")] public object Undo() => UndoRedoManager.Undo();
	[McpTool("sbox_redo", "Redoes the last undone action.")] public object Redo() => UndoRedoManager.Redo();
	[McpTool("sbox_undo_history", "Lists all undo-able actions.")] public object UndoHistory() { var h = UndoRedoManager.GetHistory(); return new { success = true, count = h.Count, history = h }; }
	[McpTool("sbox_undo_clear", "Clears all undo/redo history.")] public object UndoClear() { UndoRedoManager.Clear(); return new { success = true, message = "Cleared" }; }

	[McpTool("sbox_config_set", "Save a key-value pair to persistent storage. Values persist across sessions.")]
	public object ConfigSet( string key, string value )
	{
		var path = $"mcp_state/{PersistenceStore.SafeFileName( key )}.json";
		PersistenceStore.EnsureDirectory( "mcp_state" );
		PersistenceStore.Save( path, new { key, value, saved = DateTime.UtcNow.ToString( "o" ) } );
		return new { success = true, key, stored = true };
	}

	[McpTool("sbox_config_get", "Read a value from persistent storage by key.")]
	public object ConfigGet( string key )
	{
		var path = $"mcp_state/{PersistenceStore.SafeFileName( key )}.json";
		if ( !PersistenceStore.Exists( path ) )
			return new { error = $"Key '{key}' not found" };
		var data = PersistenceStore.Load<JsonElement>( path );
		return new { key, value = data.TryGetProperty( "value", out var v ) ? v.GetString() : null };
	}

	[McpTool("sbox_config_list", "List all stored configuration keys.")]
	public object ConfigList()
	{
		try
		{
			var files = FileSystem.Data.FindFile( "mcp_state", "*.json", false ).ToList();
			var keys = files.Select( f => f.Replace( ".json", "" ) ).ToList();
			return new { count = keys.Count, keys };
		}
		catch { return new { count = 0, keys = new List<string>() }; }
	}

	[McpTool("sbox_config_delete", "Delete a stored configuration value by key.")]
	public object ConfigDelete( string key )
	{
		var path = $"mcp_state/{PersistenceStore.SafeFileName( key )}.json";
		if ( !PersistenceStore.Exists( path ) )
			return new { error = $"Key '{key}' not found" };
		PersistenceStore.Delete( path );
		return new { success = true, key, deleted = true };
	}

	[McpTool("sbox_subscribe_event", "Subscribe to game events. Events: phase_change, day_change, alarm, quota_due")]
	public object SubscribeEvent( string eventType )
	{
		var valid = new[] { "phase_change", "day_change", "alarm", "quota_due" };
		if ( !valid.Contains( eventType ) )
			return new { error = $"Invalid event type. Valid: {string.Join( ", ", valid )}" };
		return new { success = true, eventType, subscribed = true };
	}

	[McpTool("sbox_unsubscribe_event", "Unsubscribe from a game event.")]
	public object UnsubscribeEvent( string eventType )
	{
		return new { success = true, eventType, unsubscribed = true };
	}

	[McpTool("sbox_search_assets", "Search for assets by type (prefab, model, material, sound, texture, animation) and name query.")]
	public object SearchAssets( string type = "all", string query = "", int maxResults = 20 )
	{
		if ( !AssetExtensions.TryGetValue( type, out var exts ) )
			return new { error = $"Unknown type '{type}'. Valid: {string.Join( ", ", AssetExtensions.Keys )}" };
		try
		{
			var results = new List<string>();
			foreach ( var ext in exts )
			{
				foreach ( var f in FileSystem.Mounted.FindFile( ".", $"*{ext}", true ) )
				{
					if ( results.Count >= maxResults ) break;
					var name = f.Replace( '\\', '/' );
					if ( string.IsNullOrEmpty( query ) || name.IndexOf( query, StringComparison.OrdinalIgnoreCase ) >= 0 )
						results.Add( name );
				}
				if ( results.Count >= maxResults ) break;
			}
			return new { type, query, count = results.Count, results };
		}
		catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_list_events", "List all available game event types.")]
	public object ListEvents()
	{
		return new { events = new[] { "phase_change", "day_change", "alarm", "quota_due" } };
	}

	[McpTool("sbox_dry_run", "Validate a tool call without executing it. Checks if tool exists and parses arguments.")]
	public object DryRun( string tool, string paramsJson = "{}" )
	{
		var result = McpToolDispatcher.Instance.Registry.TryGet( tool, out var mdesc, out _ );
		if ( !result )
			return new { valid = false, error = $"Tool '{tool}' not found" };

		var paramInfo = new List<object>();
		var errorList = new List<string>();
		using var doc = JsonDocument.Parse( paramsJson ?? "{}" );
		var root = doc.RootElement;

		foreach ( var p in mdesc.Parameters )
		{
			var hasValue = root.TryGetProperty( p.Name, out var val );
			paramInfo.Add( new { name = p.Name, type = p.ParameterType.Name, provided = hasValue, value = hasValue ? val.GetRawText() : null } );
			if ( !hasValue )
				errorList.Add( $"Missing required parameter: '{p.Name}' ({p.ParameterType.Name})" );
		}

		var pc = mdesc.Parameters.Count();
		string[] errArr = errorList.Count > 0 ? errorList.ToArray() : null;
		return new { valid = errorList.Count == 0, tool = tool, paramCount = pc, parameters = paramInfo, errors = errArr };
	}

	[McpTool("sbox_read_logs", "Returns recent log entries. Call enable=true once to start capturing.")]
	public object ReadLogs( int count = 50, bool enable = false )
	{
		if ( enable ) McpLogBridge.Enable();
		var logs = McpLogBridge.GetRecent( count );
		return new { enabled = McpLogBridge.IsEnabled, count = logs.Count, logs = logs.Select( e => new { e.Level, e.Message, time = e.Time.ToString( "HH:mm:ss" ) } ).ToList() };
	}

	[McpTool("sbox_export_scene", "Export entire scene hierarchy as JSON. Includes transforms, components, and children.")]
	public object ExportScene()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var rootNodes = scene.Children.Select( SerializeGameObject ).ToList();
		return new { sceneName = scene.Name, rootCount = rootNodes.Count, roots = rootNodes };
	}

	private object SerializeGameObject( GameObject go )
	{
		var comps = go.Components.GetAll<Component>().Select( c =>
		{
			var td = TypeLibrary.GetType( c.GetType() );
			var props = new Dictionary<string, object>();
			if ( td != null )
			{
				foreach ( var p in td.Properties.Where( p => p.CanRead && p.CanWrite ) )
				{
					try { var val = p.GetValue( c ); if ( val != null && (val.GetType().IsValueType || val is string) ) props[p.Name] = val; } catch { }
				}
			}
			return new { type = c.GetType().Name, enabled = c.Enabled, properties = props };
		} ).ToList();

		return new
		{
			id = go.Id.ToString(),
			name = go.Name,
			position = new { x = go.WorldPosition.x, y = go.WorldPosition.y, z = go.WorldPosition.z },
			rotation = new { pitch = go.WorldRotation.Pitch(), yaw = go.WorldRotation.Yaw(), roll = go.WorldRotation.Roll() },
			scale = go.LocalScale,
			components = comps,
			children = go.Children.Select( SerializeGameObject ).ToList()
		};
	}

	[McpTool("sbox_import_scene", "Import scene hierarchy from JSON. Recreates GameObjects, transforms, components, and hierarchy.")]
	public object ImportScene( string sceneJson )
	{
		try
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };

			var doc = JsonDocument.Parse( sceneJson );
			var roots = doc.RootElement.GetProperty( "roots" );
			var idMap = new Dictionary<string, string>();

			// Pass 1: create all GameObjects, store old->new GUID
			var created = new List<(GameObject go, JsonElement data)>();
			void CreateRecursive( JsonElement node, GameObject parent )
			{
				var name = node.GetProperty( "name" ).GetString();
				var go = new GameObject( true, name );
				if ( node.TryGetProperty( "position", out var pos ) )
					go.WorldPosition = new Vector3( pos.GetProperty( "x" ).GetSingle(), pos.GetProperty( "y" ).GetSingle(), pos.GetProperty( "z" ).GetSingle() );
				if ( node.TryGetProperty( "scale", out var scale ) )
					go.LocalScale = scale.GetSingle();
				if ( node.TryGetProperty( "rotation", out var rot ) )
					go.WorldRotation = Rotation.From( rot.GetProperty( "pitch" ).GetSingle(), rot.GetProperty( "yaw" ).GetSingle(), rot.GetProperty( "roll" ).GetSingle() );

				if ( node.TryGetProperty( "id", out var idProp ) )
					idMap[idProp.GetString()] = go.Id.ToString();

				created.Add( (go, node) );

				if ( node.TryGetProperty( "children", out var children ) )
				{
					foreach ( var child in children.EnumerateArray() )
						CreateRecursive( child, go );
				}
			}
			foreach ( var root in roots.EnumerateArray() )
				CreateRecursive( root, null );

			// Pass 3: add components
			foreach ( var (go, node) in created )
			{
				if ( !node.TryGetProperty( "components", out var comps ) ) continue;
				foreach ( var comp in comps.EnumerateArray() )
				{
					var typeName = comp.GetProperty( "type" ).GetString();
					var typeDesc = FindComponentType( typeName );
					if ( typeDesc == null ) continue;
					try
					{
						var instance = go.Components.Create( typeDesc );
						if ( comp.TryGetProperty( "enabled", out var en ) && !en.GetBoolean() )
							instance.Enabled = false;
						if ( comp.TryGetProperty( "properties", out var props ) )
						{
							foreach ( var p in props.EnumerateObject() )
							{
								var pd = typeDesc.Properties.FirstOrDefault( x => x.Name == p.Name && x.CanWrite );
								if ( pd != null )
								{
									try { pd.SetValue( instance, JsonSerializer.Deserialize( p.Value.GetRawText(), pd.PropertyType ) ); } catch { }
								}
							}
						}
					}
					catch { }
				}
			}

			return new { success = true, createdCount = created.Count, idMap };
		}
		catch ( Exception e ) { return new { error = e.Message }; }
	}

	private object MapGameObject( GameObject go ) => new { id = go.Id, name = go.Name, children = go.Children.Any() ? go.Children.Select( MapGameObject ).ToList() : null };
	private static TypeDescription FindComponentType( string name ) { var types = TypeLibrary.GetTypes<Component>(); foreach ( var t in types ) { if ( string.Equals( t.Name, name, StringComparison.OrdinalIgnoreCase ) ) return t; } foreach ( var t in types ) { if ( t.Name.IndexOf( name, StringComparison.OrdinalIgnoreCase ) >= 0 ) return t; } return null; }
	private static object FormatValue( object value ) { if ( value is Vector3 v3 ) return $"{v3.x:F1},{v3.y:F1},{v3.z:F1}"; if ( value is Angles ang ) return $"{ang.pitch:F1},{ang.yaw:F1},{ang.roll:F1}"; if ( value is Rotation rot ) return rot.Angles().ToString(); if ( value is Enum e ) return e.ToString(); if ( value is float f ) return Math.Round( f, 2 ); return value?.ToString() ?? "null"; }
	private static object ConvertValue( string str, Type targetType ) { if ( targetType == typeof( string ) ) return str; if ( targetType == typeof( int ) ) return int.Parse( str ); if ( targetType == typeof( float ) ) return float.Parse( str ); if ( targetType == typeof( bool ) ) return bool.Parse( str ); if ( targetType == typeof( Vector3 ) ) { var p = str.Split( ',' ); return new Vector3( float.Parse( p[0] ), float.Parse( p[1] ), float.Parse( p[2] ) ); } if ( TypeLibrary.GetType( targetType )?.IsEnum == true ) return Enum.Parse( targetType, str ); if ( targetType == typeof( Angles ) ) { var p = str.Split( ',' ); return new Angles( float.Parse( p[0] ), float.Parse( p[1] ), float.Parse( p[2] ) ); } return Convert.ChangeType( str, targetType ); }
}
