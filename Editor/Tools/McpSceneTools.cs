using McpBridge;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Editor;

internal static class McpSceneTools
{
	internal static void Register()
	{
		McpEditorServer.RegisterTool( "list_objects", "List GameObjects in the scene with optional pagination", args =>
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			var page = args.TryGetProperty( "page", out var pn ) ? Math.Max( 1, pn.GetInt32() ) : 1;
			var perPage = args.TryGetProperty( "perPage", out var pp ) ? Math.Clamp( pp.GetInt32(), 1, 500 ) : 50;
			var all = scene.GetAllObjects( true ).ToList();
			var total = all.Count;
			var paged = all.Skip( (page - 1) * perPage ).Take( perPage ).Select( g => new { g.Name, g.Id } ).ToList();
			return new { total, page, perPage, objects = paged };
		}, new { type = "object", properties = new { page = new { type = "number", description = "Page number (1-based)" }, perPage = new { type = "number", description = "Results per page (max 500)" } }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "create_object", "Create a new GameObject", args =>
		{
			var name = args.GetProperty( "name" ).GetString();
			var pos = args.TryGetProperty( "position", out var p )
				? JsonSerializer.Deserialize<Vector3>( p.GetRawText() )
				: Vector3.Zero;
			var go = new GameObject( true, name );
			go.WorldPosition = pos;
			return new { id = go.Id.ToString(), name };
		}, new { type = "object", properties = new { name = new { type = "string" }, position = new { type = "string", description = "x,y,z" } }, required = new[] { "name" } }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_create_gameobject", "Create a new GameObject in the scene", args =>
		{
			var name = args.GetProperty( "name" ).GetString();
			var x = args.TryGetProperty( "x", out var xp ) ? xp.GetSingle() : 0f;
			var y = args.TryGetProperty( "y", out var yp ) ? yp.GetSingle() : 0f;
			var z = args.TryGetProperty( "z", out var zp ) ? zp.GetSingle() : 0f;
			var go = new GameObject( true, name );
			go.WorldPosition = new Vector3( x, y, z );
			return new { id = go.Id.ToString(), name, position = new { x, y, z } };
		}, new { type = "object", properties = new { name = new { type = "string", description = "Display name" }, x = new { type = "number", description = "X position" }, y = new { type = "number", description = "Y position" }, z = new { type = "number", description = "Z position" } }, required = new[] { "name" } }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_delete_gameobject", "Delete a GameObject from the scene by GUID", args =>
		{
			var idStr = args.GetProperty( "id" ).GetString();
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			if ( !Guid.TryParse( idStr, out var guid ) ) return "Invalid GUID format";
			var go = scene.Directory.FindByGuid( guid );
			if ( go == null ) return "GameObject not found";
			var name = go.Name;
			go.Destroy();
			return new { deleted = true, id = idStr, name };
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject" } }, required = new[] { "id" } }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_set_transform", "Set position/rotation/scale of a GameObject", args =>
		{
			var idStr = args.GetProperty( "id" ).GetString();
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			if ( !Guid.TryParse( idStr, out var guid ) ) return "Invalid GUID format";
			var go = scene.Directory.FindByGuid( guid );
			if ( go == null ) return "GameObject not found";

			if ( args.TryGetProperty( "x", out var xp ) && args.TryGetProperty( "y", out var yp ) )
			{
				var z = args.TryGetProperty( "z", out var zp ) ? zp.GetSingle() : 0f;
				var pos = new Vector3( xp.GetSingle(), yp.GetSingle(), z );
				go.WorldPosition = pos;
			}

			if ( args.TryGetProperty( "pitch", out var pitch ) && args.TryGetProperty( "yaw", out var yaw ) )
			{
				var roll = args.TryGetProperty( "roll", out var r ) ? r.GetSingle() : 0f;
				go.WorldRotation = Rotation.From( pitch.GetSingle(), yaw.GetSingle(), roll );
			}

			if ( args.TryGetProperty( "scale", out var scale ) )
			{
				go.WorldScale = scale.GetSingle();
			}

			return new { id = idStr, name = go.Name, position = new { x = go.WorldPosition.x, y = go.WorldPosition.y, z = go.WorldPosition.z } };
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject" }, x = new { type = "number", description = "New X position" }, y = new { type = "number", description = "New Y position" }, z = new { type = "number", description = "New Z position" }, pitch = new { type = "number" }, yaw = new { type = "number" }, roll = new { type = "number" }, scale = new { type = "number" } }, required = new[] { "id" } }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_add_component", "Add a component to a GameObject by GUID", args =>
		{
			var idStr = args.GetProperty( "id" ).GetString();
			var typeName = args.GetProperty( "type" ).GetString();
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			if ( !Guid.TryParse( idStr, out var guid ) ) return "Invalid GUID format";
			var go = scene.Directory.FindByGuid( guid );
			if ( go == null ) return "GameObject not found";

			var compTypes = TypeLibrary.GetTypes<Component>();
			var typeDesc = compTypes.FirstOrDefault( t => string.Equals( t.Name, typeName, StringComparison.OrdinalIgnoreCase ) );
			if ( typeDesc == null )
				return $"Component type '{typeName}' not found";

			var comp = go.Components.Create( typeDesc );
			return new { added = true, id = idStr, name = go.Name, componentType = typeDesc.Name, componentId = comp.Id.ToString() };
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject" }, type = new { type = "string", description = "Component type name (e.g. Sandbox.ModelComponent)" } }, required = new[] { "id", "type" } }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_list_component_types", "List all available Component types (built-in and game-specific) in the project.", args =>
		{
			var query = args.TryGetProperty( "query", out var q ) ? q.GetString() ?? "" : "";
			var types = TypeLibrary.GetTypes<Component>();
			var all = new List<object>();
			foreach ( var t in types )
			{
				if ( !string.IsNullOrEmpty( query ) && t.Name.IndexOf( query, StringComparison.OrdinalIgnoreCase ) < 0 && (t.FullName?.IndexOf( query, StringComparison.OrdinalIgnoreCase ) ?? -1) < 0 )
					continue;

				all.Add( new
				{
					name = t.Name,
					fullName = t.FullName,
					description = t.Description ?? ""
				} );
			}
			return new { count = all.Count, types = all };
		}, new { type = "object", properties = new { query = new { type = "string", description = "Optional text filter by component name" } }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_get_component_properties", "Get all readable properties and their values of a component on a GameObject.", args =>
		{
			var idStr = args.GetProperty( "id" ).GetString();
			var componentName = args.GetProperty( "component" ).GetString();

			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };
			if ( !Guid.TryParse( idStr, out var guid ) ) return new { error = "Invalid GUID format" };
			var go = scene.Directory.FindByGuid( guid );
			if ( go == null ) return new { error = "GameObject not found" };

			var typeDesc = McpBridge.Tools.AssetTools.FindComponentType( componentName );
			if ( typeDesc == null ) return new { error = $"Component type '{componentName}' not found" };

			var comp = go.Components.Get( typeDesc.TargetType );
			if ( comp == null ) return new { error = $"Component '{componentName}' not attached to this GameObject" };

			var properties = new Dictionary<string, object>();
			foreach ( var prop in typeDesc.Properties )
			{
				if ( !prop.CanRead ) continue;
				try
				{
					var val = prop.GetValue( comp );
					if ( val != null && val is not Component && val is not GameObject )
					{
						properties[prop.Name] = val;
					}
					else if ( val != null )
					{
						properties[prop.Name] = val.ToString();
					}
					else
					{
						properties[prop.Name] = null;
					}
				}
				catch ( Exception e )
				{
					properties[prop.Name] = $"<Error: {e.Message}>";
				}
			}

			return new { id = idStr, gameObjectName = go.Name, component = typeDesc.Name, properties };
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject" }, component = new { type = "string", description = "Component type name (e.g. ModelComponent)" } }, required = new[] { "id", "component" } }, annotations: new { readOnlyHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_set_component_property", "Set a property value of a component on a GameObject.", args =>
		{
			var idStr = args.GetProperty( "id" ).GetString();
			var componentName = args.GetProperty( "component" ).GetString();
			var propertyName = args.GetProperty( "property" ).GetString();
			var valueVal = args.GetProperty( "value" );

			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };
			if ( !Guid.TryParse( idStr, out var guid ) ) return new { error = "Invalid GUID format" };
			var go = scene.Directory.FindByGuid( guid );
			if ( go == null ) return new { error = "GameObject not found" };

			var typeDesc = McpBridge.Tools.AssetTools.FindComponentType( componentName );
			if ( typeDesc == null ) return new { error = $"Component type '{componentName}' not found" };

			var comp = go.Components.Get( typeDesc.TargetType );
			if ( comp == null ) return new { error = $"Component '{componentName}' not attached to this GameObject" };

			var prop = typeDesc.Properties.FirstOrDefault( p => string.Equals( p.Name, propertyName, StringComparison.OrdinalIgnoreCase ) );
			if ( prop == null ) return new { error = $"Property '{propertyName}' not found on component '{componentName}'" };
			if ( !prop.CanWrite ) return new { error = $"Property '{propertyName}' is read-only" };

			try
			{
				var converted = McpBridge.Tools.AssetTools.ConvertValue( valueVal, prop.PropertyType );
				if ( converted == null && prop.PropertyType.IsValueType && Nullable.GetUnderlyingType( prop.PropertyType ) == null )
				{
					return new { error = $"Cannot set null value to non-nullable type '{prop.PropertyType.Name}'" };
				}

				prop.SetValue( comp, converted );
				return new { success = true, id = idStr, gameObjectName = go.Name, component = typeDesc.Name, property = prop.Name, newValue = converted?.ToString() };
			}
			catch ( Exception e )
			{
				return new { error = $"Failed to set property: {e.Message}" };
			}
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject" }, component = new { type = "string", description = "Component type name" }, property = new { type = "string", description = "Property name" }, value = new { type = "string", description = "Value to set (can be any type: string, number, bool, object for Vector3/Rotation)" } }, required = new[] { "id", "component", "property", "value" } }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_call_component_method", "Invoke a method on a component on a GameObject.", args =>
		{
			var idStr = args.GetProperty( "id" ).GetString();
			var componentName = args.GetProperty( "component" ).GetString();
			var methodName = args.GetProperty( "method" ).GetString();

			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };
			if ( !Guid.TryParse( idStr, out var guid ) ) return new { error = "Invalid GUID format" };
			var go = scene.Directory.FindByGuid( guid );
			if ( go == null ) return new { error = "GameObject not found" };

			var typeDesc = McpBridge.Tools.AssetTools.FindComponentType( componentName );
			if ( typeDesc == null ) return new { error = $"Component type '{componentName}' not found" };

			var comp = go.Components.Get( typeDesc.TargetType );
			if ( comp == null ) return new { error = $"Component '{componentName}' not attached to this GameObject" };

			var method = typeDesc.Methods.FirstOrDefault( m => string.Equals( m.Name, methodName, StringComparison.OrdinalIgnoreCase ) );
			if ( method == null ) return new { error = $"Method '{methodName}' not found on component '{componentName}'" };

			var methodParams = new List<object>();
			if ( args.TryGetProperty( "params", out var pEl ) && pEl.ValueKind == JsonValueKind.Array )
			{
				var parameters = method.Parameters;
				var idx = 0;
				foreach ( var paramEl in pEl.EnumerateArray() )
				{
					if ( idx >= parameters.Length ) break;
					var paramType = parameters[idx].ParameterType;
					var converted = McpBridge.Tools.AssetTools.ConvertValue( paramEl, paramType );
					methodParams.Add( converted );
					idx++;
				}
				while ( idx < parameters.Length )
				{
					methodParams.Add( null );
					idx++;
				}
			}
			else
			{
				if ( method.Parameters.Length > 0 && method.Parameters.Any( p => !p.IsOptional ) )
				{
					return new { error = $"Method '{methodName}' requires parameters but none were provided." };
				}
			}

			try
			{
				var res = method.Invoke( comp, methodParams.ToArray() );
				return new { success = true, id = idStr, gameObjectName = go.Name, component = typeDesc.Name, method = method.Name, returnValue = res?.ToString() };
			}
			catch ( Exception e )
			{
				return new { error = $"Failed to invoke method: {e.Message}" };
			}
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject" }, component = new { type = "string", description = "Component type name" }, method = new { type = "string", description = "Method name to invoke" }, @params = new { type = "array", description = "Optional array of parameters to pass to the method" } }, required = new[] { "id", "component", "method" } }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_get_scene_hierarchy", "Get a tree-like representation of all GameObjects in the active scene.", _ =>
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };

			var rootNodes = new List<object>();
			foreach ( var go in scene.Children )
			{
				if ( go.IsValid() ) rootNodes.Add( McpEditorServer.BuildSceneNode( go ) );
			}

			var info = scene.GetAllComponents<SceneInformation>().FirstOrDefault();
			var title = info?.Title ?? "Untitled";
			return new { activeScene = title, rootNodes };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_instantiate_prefab", "Instantiate a prefab file in the scene.", args =>
		{
			var path = args.GetProperty( "path" ).GetString();
			var x = args.TryGetProperty( "x", out var xp ) ? xp.GetSingle() : 0f;
			var y = args.TryGetProperty( "y", out var yp ) ? yp.GetSingle() : 0f;
			var z = args.TryGetProperty( "z", out var zp ) ? zp.GetSingle() : 0f;
			var parentGuidStr = args.TryGetProperty( "parentGuid", out var pg ) ? pg.GetString() : null;

			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };

			GameObject parentGo = null;
			if ( !string.IsNullOrEmpty( parentGuidStr ) && Guid.TryParse( parentGuidStr, out var parentGuid ) )
			{
				parentGo = scene.Directory.FindByGuid( parentGuid );
				if ( parentGo == null ) return new { error = $"Parent GameObject with GUID '{parentGuidStr}' not found" };
			}

			try
			{
				var resourceLibType = TypeLibrary.GetType( "Sandbox.ResourceLibrary" );
				var getMethod = resourceLibType?.Methods.FirstOrDefault( m => m.Name == "Get" );
				if ( getMethod == null ) return new { error = "ResourceLibrary.Get not found" };

				var prefab = getMethod.Invoke( null, new object[] { path } );
				if ( prefab == null ) return new { error = $"Prefab not found at path: {path}" };

				var sceneUtilType = TypeLibrary.GetType( "Sandbox.SceneUtility" );
				var getSceneMethod = sceneUtilType?.Methods.FirstOrDefault( m => m.Name == "GetPrefabScene" );
				if ( getSceneMethod == null ) return new { error = "SceneUtility.GetPrefabScene not found" };

				var prefabScene = getSceneMethod.Invoke( null, new object[] { prefab } );
				if ( prefabScene == null ) return new { error = "GetPrefabScene returned null" };

				var psTd = TypeLibrary.GetType( prefabScene.GetType() );
				var cloneMethod = psTd?.Methods.FirstOrDefault( m => m.Name == "Clone" );
				if ( cloneMethod == null ) return new { error = "Clone method not found on prefab scene" };

				var go = cloneMethod.Invoke( prefabScene, Array.Empty<object>() ) as GameObject;
				if ( go == null || !go.IsValid() ) return new { error = "Failed to clone prefab scene" };

				if ( parentGo != null )
				{
					go.Parent = parentGo;
				}
				go.WorldPosition = new Vector3( x, y, z );

				return new { success = true, id = go.Id.ToString(), name = go.Name, position = new { x, y, z } };
			}
			catch ( Exception e )
			{
				return new { error = $"Instantiation failed: {e.Message}" };
			}
		}, new { type = "object", properties = new { path = new { type = "string", description = "Path to the .prefab file (e.g. prefabs/dummy_bot.prefab)" }, x = new { type = "number", description = "Target X position" }, y = new { type = "number", description = "Target Y position" }, z = new { type = "number", description = "Target Z position" }, parentGuid = new { type = "string", description = "Optional GUID of the parent GameObject" } }, required = new[] { "path" } }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_search_assets", "Search for assets (models, prefabs, sounds, materials) in the project.", args =>
		{
			var query = args.TryGetProperty( "query", out var q ) ? q.GetString() ?? "" : "";
			var ext = args.TryGetProperty( "extension", out var e ) ? e.GetString() ?? "" : "";

			var exts = string.IsNullOrEmpty( ext ) 
				? new[] { ".prefab", ".vmdl", ".vmat", ".vsnd", ".vtex" } 
				: new[] { ext.StartsWith(".") ? ext : "." + ext };

			var files = new List<string>();
			foreach ( var extension in exts )
			{
				try
				{
					var found = FileSystem.Mounted.FindFile( ".", $"*{extension}", true );
					foreach ( var f in found )
					{
						var normalPath = f.Replace( '\\', '/' );
						if ( string.IsNullOrEmpty( query ) || normalPath.IndexOf( query, StringComparison.OrdinalIgnoreCase ) >= 0 )
						{
							files.Add( normalPath );
						}
					}
				}
				catch { }
			}

			var limited = files.Take( 100 ).ToList();
			return new { totalCount = files.Count, returnedCount = limited.Count, assets = limited };
		}, new { type = "object", properties = new { query = new { type = "string", description = "Text to search in asset paths" }, extension = new { type = "string", description = "Optional asset extension (e.g. .prefab, .vmdl, .vsnd)" } }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_inspect_prefab", "Read and inspect a .prefab file structure without instantiating it.", args =>
		{
			var path = args.GetProperty( "path" ).GetString();
			try
			{
				if ( !FileSystem.Mounted.FileExists( path ) )
					return new { error = $"Prefab file not found: {path}" };

				var jsonText = FileSystem.Mounted.ReadAllText( path );
				using var doc = JsonDocument.Parse( jsonText );
				return new { path, structure = doc.RootElement.Clone() };
			}
			catch ( Exception e )
			{
				return new { error = $"Failed to read prefab: {e.Message}" };
			}
		}, new { type = "object", properties = new { path = new { type = "string", description = "Path to the .prefab file" } }, required = new[] { "path" } }, annotations: new { readOnlyHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_run_console_command", "Run a console command in the game/editor system.", args =>
		{
			var cmd = args.GetProperty( "command" ).GetString();
			try
			{
				var tConsole = TypeLibrary.GetType( "Sandbox.ConsoleSystem" ) ?? TypeLibrary.GetType( "Sandbox.Editor.Console" );
				if ( tConsole == null ) return new { error = "Console system type not found" };

				var runMethod = tConsole.Methods.FirstOrDefault( m => m.Name == "Run" && m.Parameters.Length == 1 && m.Parameters[0].ParameterType == typeof( string ) );
				if ( runMethod == null ) return new { error = "ConsoleSystem.Run(string) method not found" };

				runMethod.Invoke( null, new object[] { cmd } );
				return new { success = true, command = cmd };
			}
			catch ( Exception e )
			{
				return new { error = $"Failed to run command: {e.Message}" };
			}
		}, new { type = "object", properties = new { command = new { type = "string", description = "The console command string to execute (e.g. noclip)" } }, required = new[] { "command" } }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_focus_camera", "Move the main scene camera to focus on a target GameObject.", args =>
		{
			var idStr = args.GetProperty( "id" ).GetString();
			var distance = args.TryGetProperty( "distance", out var distProp ) ? distProp.GetSingle() : 150f;

			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };
			if ( !Guid.TryParse( idStr, out var guid ) ) return new { error = "Invalid GUID format" };
			var go = scene.Directory.FindByGuid( guid );
			if ( go == null ) return new { error = "GameObject not found" };

			var camera = scene.Camera;
			if ( camera == null ) return new { error = "No camera component found in active scene" };

			var targetPos = go.WorldPosition;
			var offset = new Vector3( -1f, 1f, 0.75f ).Normal * distance;
			camera.WorldPosition = targetPos + offset;

			var lookDir = (targetPos - camera.WorldPosition).Normal;
			camera.WorldRotation = Rotation.LookAt( lookDir, Vector3.Up );

			return new { success = true, focusedObjectId = idStr, focusedObjectName = go.Name, cameraPosition = new { x = camera.WorldPosition.x, y = camera.WorldPosition.y, z = camera.WorldPosition.z } };
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject to focus on" }, distance = new { type = "number", description = "Distance from camera to target" } }, required = new[] { "id" } }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_find_by_component", "Find all GameObjects in the scene that have a specific component.", args =>
		{
			var componentName = args.GetProperty( "component" ).GetString();
			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };

			var typeDesc = McpBridge.Tools.AssetTools.FindComponentType( componentName );
			if ( typeDesc == null ) return new { error = $"Component type '{componentName}' not found" };

			var gameObjects = new List<object>();
			foreach ( var go in scene.GetAllObjects( true ) )
			{
				var comp = go.Components.Get( typeDesc.TargetType );
				if ( comp.IsValid() )
				{
					gameObjects.Add( new { id = go.Id.ToString(), name = go.Name, position = new { x = go.WorldPosition.x, y = go.WorldPosition.y, z = go.WorldPosition.z } });
				}
			}

			return new { count = gameObjects.Count, component = typeDesc.Name, gameObjects };
		}, new { type = "object", properties = new { component = new { type = "string", description = "Component name (e.g. ModelComponent)" } }, required = new[] { "component" } }, annotations: new { readOnlyHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_find_by_name", "Find all GameObjects in the scene by name (wildcard/substring search).", args =>
		{
			var nameQuery = args.GetProperty( "name" ).GetString();
			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };

			var list = new List<object>();
			foreach ( var go in scene.GetAllObjects( true ) )
			{
				if ( go.IsValid() && go.Name.IndexOf( nameQuery, StringComparison.OrdinalIgnoreCase ) >= 0 )
				{
					list.Add( new
					{
						id = go.Id.ToString(),
						name = go.Name,
						position = new { x = go.WorldPosition.x, y = go.WorldPosition.y, z = go.WorldPosition.z }
					} );
				}
			}
			return new { count = list.Count, query = nameQuery, gameObjects = list };
		}, new { type = "object", properties = new { name = new { type = "string", description = "GameObject name or substring to search for" } }, required = new[] { "name" } }, annotations: new { readOnlyHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_raycast", "Perform a physics raycast in the active scene.", args =>
		{
			var startX = args.GetProperty( "startX" ).GetSingle();
			var startY = args.GetProperty( "startY" ).GetSingle();
			var startZ = args.GetProperty( "startZ" ).GetSingle();
			var endX = args.GetProperty( "endX" ).GetSingle();
			var endY = args.GetProperty( "endY" ).GetSingle();
			var endZ = args.GetProperty( "endZ" ).GetSingle();

			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };

			try
			{
				var start = new Vector3( startX, startY, startZ );
				var end = new Vector3( endX, endY, endZ );
				var tr = scene.Trace.Ray( start, end ).Run();

				return new
				{
					hit = tr.Hit,
					distance = tr.Distance,
					hitPosition = new { x = tr.EndPosition.x, y = tr.EndPosition.y, z = tr.EndPosition.z },
					normal = new { x = tr.Normal.x, y = tr.Normal.y, z = tr.Normal.z },
					hitGameObjectId = tr.GameObject.IsValid() ? tr.GameObject.Id.ToString() : null,
					hitGameObjectName = tr.GameObject.IsValid() ? tr.GameObject.Name : null
				};
			}
			catch ( Exception e )
			{
				return new { error = $"Raycast failed: {e.Message}" };
			}
		}, new { type = "object", properties = new { startX = new { type = "number" }, startY = new { type = "number" }, startZ = new { type = "number" }, endX = new { type = "number" }, endY = new { type = "number" }, endZ = new { type = "number" } }, required = new[] { "startX", "startY", "startZ", "endX", "endY", "endZ" } }, annotations: new { readOnlyHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_apply_physics_impulse", "Apply a force impulse to a GameObject's Rigidbody.", args =>
		{
			var idStr = args.GetProperty( "id" ).GetString();
			var forceVal = args.GetProperty( "force" );

			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };
			if ( !Guid.TryParse( idStr, out var guid ) ) return new { error = "Invalid GUID format" };
			var go = scene.Directory.FindByGuid( guid );
			if ( go == null ) return new { error = "GameObject not found" };

			var rb = go.Components.Get<Rigidbody>();
			if ( !rb.IsValid() ) return new { error = "No Rigidbody component found on this GameObject" };

			try
			{
				var forceVec = (Vector3)McpBridge.Tools.AssetTools.ConvertValue( forceVal, typeof( Vector3 ) );
				rb.ApplyImpulse( forceVec );
				return new { success = true, id = idStr, gameObjectName = go.Name, appliedImpulse = forceVec.ToString() };
			}
			catch ( Exception e )
			{
				return new { error = $"Failed to apply impulse: {e.Message}" };
			}
		}, new { type = "object", properties = new { id = new { type = "string", description = "GUID of the GameObject" }, force = new { type = "string", description = "Force vector as 'x,y,z' or JSON object" } }, required = new[] { "id", "force" } }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_duplicate_gameobject", "Duplicate a GameObject in the scene by GUID.", args =>
		{
			var idStr  = args.GetProperty( "id" ).GetString();
			var newName = args.TryGetProperty( "name", out var np ) ? np.GetString() : null;
			var scene  = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };
			if ( !Guid.TryParse( idStr, out var guid ) ) return new { error = "Invalid GUID" };
			var go = scene.Directory.FindByGuid( guid );
			if ( !go.IsValid() ) return new { error = "GameObject not found" };
			var clone = go.Clone();
			if ( !string.IsNullOrEmpty( newName ) ) clone.Name = newName;
			return new { success = true, originalId = idStr, newId = clone.Id.ToString(), name = clone.Name };
		}, new { type = "object", properties = new { id = new { type = "string", description = "Source GameObject GUID" }, name = new { type = "string", description = "Optional name for the clone" } }, required = new[] { "id" } }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_rename_gameobject", "Rename a GameObject in the scene by GUID.", args =>
		{
			var idStr   = args.GetProperty( "id" ).GetString();
			var newName = args.GetProperty( "name" ).GetString();
			var scene   = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };
			if ( !Guid.TryParse( idStr, out var guid ) ) return new { error = "Invalid GUID" };
			var go = scene.Directory.FindByGuid( guid );
			if ( !go.IsValid() ) return new { error = "GameObject not found" };
			var oldName = go.Name;
			go.Name = newName;
			return new { success = true, id = idStr, oldName, newName };
		}, new { type = "object", properties = new { id = new { type = "string", description = "GameObject GUID" }, name = new { type = "string", description = "New name" } }, required = new[] { "id", "name" } }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_set_parent", "Set the parent of a GameObject. Pass null parentId to move to scene root.", args =>
		{
			var idStr       = args.GetProperty( "id" ).GetString();
			var parentIdStr = args.TryGetProperty( "parentId", out var pp ) ? pp.GetString() : null;
			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };
			if ( !Guid.TryParse( idStr, out var guid ) ) return new { error = "Invalid child GUID" };
			var go = scene.Directory.FindByGuid( guid );
			if ( !go.IsValid() ) return new { error = "Child GameObject not found" };

			if ( string.IsNullOrEmpty( parentIdStr ) || parentIdStr == "null" )
			{
				go.Parent = null;
				return new { success = true, id = idStr, name = go.Name, newParent = (string)null };
			}

			if ( !Guid.TryParse( parentIdStr, out var parentGuid ) ) return new { error = "Invalid parent GUID" };
			var parent = scene.Directory.FindByGuid( parentGuid );
			if ( !parent.IsValid() ) return new { error = "Parent GameObject not found" };
			go.Parent = parent;
			return new { success = true, id = idStr, name = go.Name, newParent = parent.Name, newParentId = parentIdStr };
		}, new { type = "object", properties = new { id = new { type = "string", description = "Child GameObject GUID" }, parentId = new { type = "string", description = "Parent GUID, or null for scene root" } }, required = new[] { "id" } }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_describe_scene", "Return a natural language summary of the active scene suitable for LLM context.", _ =>
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return new { description = "No active scene is loaded." };

			var allObjects = scene.GetAllObjects( true ).Where( g => g.IsValid() ).ToList();
			var byComponent = new Dictionary<string, int>();
			foreach ( var go in allObjects )
				foreach ( var c in go.Components.GetAll<Component>() )
				{
					var t = c.GetType().Name;
					byComponent[t] = byComponent.TryGetValue( t, out var n ) ? n + 1 : 1;
				}

			// Game state
			var gm   = scene.GetAllComponents<BlackFridayGameManager>().FirstOrDefault();
			var qm   = scene.GetAllComponents<QuotaManager>().FirstOrDefault();
			var alarm = scene.GetAllComponents<AlarmSystem>().FirstOrDefault();

			var parts = new List<string>
			{
				$"The active scene contains {allObjects.Count} GameObjects.",
				byComponent.Count > 0 ? "Component breakdown: " + string.Join( ", ", byComponent.OrderByDescending( p => p.Value ).Take( 10 ).Select( p => $"{p.Key}×{p.Value}" ) ) + "." : "",
			};

			if ( gm != null )
				parts.Add( $"Game state: Day {gm.CurrentDay}, phase '{gm.CurrentPhase}', {gm.PhaseTimeRemaining:F0}s remaining in phase." );
			if ( qm != null )
				parts.Add( $"Economy: personal cash ${qm.MyPersonalCash:F0} / quota ${qm.PersonalQuota:F0}. Shared pool ${qm.SharedPoolCurrent:F0} / ${qm.SharedPoolTarget:F0}." );
			if ( alarm != null )
				parts.Add( $"Alarm: level {alarm.CurrentAlarmLevel} ({alarm.GetAlarmLevelName()}), progress {alarm.AlarmProgress:F0}%." );

			return new { description = string.Join( " ", parts.Where( p => !string.IsNullOrEmpty( p ) ) ), objectCount = allObjects.Count, componentTypes = byComponent };
		}, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_watch_property", "Start watching a component property. When its value changes, all connected sessions receive a notification.", args =>
		{
			var goId   = args.GetProperty( "id" ).GetString();
			var comp   = args.GetProperty( "component" ).GetString();
			var prop   = args.GetProperty( "property" ).GetString();
			var watchId = $"{goId}::{comp}::{prop}";
			McpEditorServer._watchedProperties[watchId] = (new McpEditorServer.WatchEntry( goId, comp, prop ), "");
			return new { success = true, watchId, message = $"Now watching {comp}.{prop} on {goId}" };
		}, new { type = "object", properties = new { id = new { type = "string", description = "GameObject GUID" }, component = new { type = "string", description = "Component type name" }, property = new { type = "string", description = "Property name" } }, required = new[] { "id", "component", "property" } }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_unwatch_property", "Stop watching a previously watched property.", args =>
		{
			var watchId = args.GetProperty( "watchId" ).GetString();
			var removed = McpEditorServer._watchedProperties.TryRemove( watchId, out _ );
			return new { success = removed, watchId };
		}, new { type = "object", properties = new { watchId = new { type = "string", description = "The watchId returned by sbox_watch_property" } }, required = new[] { "watchId" } }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_list_watches", "List all currently active property watches.", _ =>
		{
			var list = McpEditorServer._watchedProperties.Select( kv => new
			{
				watchId   = kv.Key,
				gameObjectId = kv.Value.Watch.GameObjectId,
				component = kv.Value.Watch.ComponentType,
				property  = kv.Value.Watch.PropertyName,
				lastValue = kv.Value.LastValue
			} ).ToList();
			return new { count = list.Count, watches = list };
		}, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_scene_list", "List available scene files (.sbox) on disk", _ =>
		{
			var scenes = McpEditorServer.ListAssetsByExt( ".sbox" );
			var info = Game.ActiveScene?.GetAllComponents<SceneInformation>().FirstOrDefault();
			var current = info?.Title ?? "none";
			return new { currentScene = current, available = scenes, count = scenes.Count };
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterToolAsync( "sbox_scene_load", "Load a scene file by path. Uses runtime API discovery.", async args =>
		{
			var path = args.GetProperty( "path" ).GetString();
			try
			{
				var tScene = TypeLibrary.GetType( "Sandbox.Scene" );
				if ( tScene == null ) return new { error = "Sandbox.Scene type not found" };
				var loadMethods = tScene.Methods.Where( m => m.Name == "LoadFromFile" || m.Name == "Load" || m.Name == "Open" ).ToList();
				if ( loadMethods.Count == 0 ) return new { error = "No scene load method found", available = tScene.Methods.Select( m => m.Name ).Take( 20 ).ToList() };
				var result = loadMethods[0].Invoke( null, new object[] { path } );
				return new { success = result != null, method = loadMethods[0].Name, path };
			}
			catch ( Exception e ) { return new { error = e.Message }; }
		}, new { type = "object", properties = new { path = new { type = "string", description = "Path to .sbox scene file" } }, required = new[] { "path" } }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_scene_create", "Create a new empty scene with a title. Uses runtime API discovery.", args =>
		{
			var title = args.TryGetProperty( "title", out var t ) ? t.GetString() ?? "New Scene" : "New Scene";
			try
			{
				var tScene = TypeLibrary.GetType( "Sandbox.Scene" );
				if ( tScene == null ) return new { error = "Sandbox.Scene type not found" };
				var createMethods = tScene.Methods.Where( m => m.Name == "Create" || m.Name == "New" || m.Name == "CreateEmpty" ).ToList();
				if ( createMethods.Count == 0 ) return new { error = "No scene create method found", available = tScene.Methods.Select( m => m.Name ).Take( 20 ).ToList() };
				var result = createMethods[0].Invoke( null, null );
				if ( result is Scene scene )
				{
					var info = scene.GetAllComponents<SceneInformation>().FirstOrDefault();
					if ( info != null ) info.Title = title;
				}
				return new { success = result != null, method = createMethods[0].Name, title };
			}
			catch ( Exception e ) { return new { error = e.Message }; }
		}, new { type = "object", properties = new { title = new { type = "string", description = "Scene title" } }, required = Array.Empty<string>() }, annotations: new { destructiveHint = true }, runOnMainThread: true );

		McpEditorServer.RegisterTool( "sbox_scene_save", "Save the active scene to a file path.", args =>
		{
			var path = args.GetProperty( "path" ).GetString();
			var scene = Game.ActiveScene;
			if ( scene == null ) return new { error = "No active scene" };
			try
			{
				var tScene = TypeLibrary.GetType( typeof( Scene ) );
				var saveMethods = tScene.Methods.Where( m => m.Name == "SaveToFile" || m.Name == "Save" || m.Name == "SaveAs" ).ToList();
				if ( saveMethods.Count == 0 ) return new { error = "No scene save method found" };
				saveMethods[0].Invoke( scene, new object[] { path } );
				return new { success = true, method = saveMethods[0].Name, path };
			}
			catch ( Exception e ) { return new { error = e.Message }; }
		}, new { type = "object", properties = new { path = new { type = "string", description = "Path to save .sbox scene file (e.g. scenes/my_scene.sbox)" } }, required = new[] { "path" } }, annotations: new { destructiveHint = true }, runOnMainThread: true );
	}
}
