using McpBridge;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace McpBridge.Tools;

[McpToolGroup("Asset")]
public class AssetTools
{
	[McpTool("sbox_asset_save_prefab", "Saves a GameObject as a prefab file.", OptionalParams = new[]{"fileName"}, DestructiveHint = true)]
	public object SavePrefab( string guidStr, string fileName = null )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid GUID" };
		var go = scene.Directory.FindByGuid( guid );
		if ( !go.IsValid() ) return new { error = "GameObject not found" };

		try
		{
			var prefabType = TypeLibrary.GetType( "Sandbox.PrefabFile" );
			if ( prefabType == null )
			{
				var sceneUtilType = TypeLibrary.GetType( "Sandbox.SceneUtility" );
				if ( sceneUtilType != null )
				{
					var createMethod = sceneUtilType.Methods.FirstOrDefault( m => m.Name == "CreatePrefabScene" || m.Name == "CreatePrefab" || m.Name == "SavePrefab" );
					if ( createMethod != null )
					{
						var pfName = fileName ?? go.Name.Replace( " ", "_" ).Replace( "/", "_" );
						if ( !pfName.EndsWith( ".prefab" ) ) pfName += ".prefab";
						createMethod.Invoke( null, new object[] { go, $"prefabs/{pfName}" } );
						return new { success = true, fileName = pfName, path = $"prefabs/{pfName}", method = "SceneUtility" };
					}
					var sceneUtilMethods = sceneUtilType.Methods.Select( m => m.Name ).Distinct().ToList();
					return new { error = "SceneUtility found but no save method", tried = new[] { "CreatePrefabScene", "CreatePrefab", "SavePrefab" }, availableMethods = sceneUtilMethods };
				}
				var allTypes = TypeLibrary.GetTypes<object>().Where( t => t.Name.Contains( "Prefab" ) || t.Name.Contains( "Scene" ) ).Select( t => t.FullName ).Take( 20 ).ToList();
				return new { error = "PrefabFile/SceneUtility type not found", tried = new[] { "Sandbox.PrefabFile", "Sandbox.SceneUtility" }, similarTypes = allTypes };
			}

			var addMethod = prefabType.Methods.FirstOrDefault( m => m.Name == "Add" || m.Name == "AddObject" || m.Name == "AddGameObject" );
			var writeMethod = prefabType.Methods.FirstOrDefault( m => m.Name == "Write" || m.Name == "Save" || m.Name == "SaveToFile" );
			if ( addMethod == null || writeMethod == null )
			{
				var prefabMethods = prefabType.Methods.Select( m => m.Name ).Distinct().ToList();
				var likely = prefabMethods.Where( n => n.Contains( "Add" ) || n.Contains( "Save" ) || n.Contains( "Write" ) || n.Contains( "Export" ) ).ToList();
				return new { error = "PrefabFile save API mismatch", triedAdd = new[] { "Add", "AddObject", "AddGameObject" }, triedWrite = new[] { "Write", "Save", "SaveToFile" }, likelyMethods = likely, allMethods = prefabMethods };
			}

			var prefab = prefabType.Create<object>();
			addMethod.Invoke( prefab, new object[] { go } );
			var name = fileName ?? go.Name.Replace( " ", "_" ).Replace( "/", "_" );
			if ( !name.EndsWith( ".prefab" ) ) name += ".prefab";
			writeMethod.Invoke( prefab, new object[] { $"prefabs/{name}" } );
			return new { success = true, fileName = name, path = $"prefabs/{name}" };
		}
		catch ( Exception e )
		{
			return new { error = $"Failed to save prefab: {e.Message}" };
		}
	}

	[McpTool("sbox_asset_load_prefab", "Spawns a prefab file into the scene at a position.", OptionalParams = new[]{"x", "y", "z"}, DestructiveHint = true)]
	public object LoadPrefab( string prefabPath, float x = 0, float y = 0, float z = 0 )
	{
		try
		{
			var prefabType = TypeLibrary.GetType( "Sandbox.Prefab" );
			if ( prefabType == null ) return new { error = "Prefab type not found" };
			var loadMethod = prefabType.Methods.FirstOrDefault( m => m.Name == "Load" );
			var cloneMethod = prefabType.Methods.FirstOrDefault( m => m.Name == "Clone" );
			if ( loadMethod == null || cloneMethod == null )
				return new { error = "Prefab.Load/Clone methods not available" };

			var prefab = loadMethod.Invoke( null, new object[] { prefabPath } );
			if ( prefab == null ) return new { error = $"Prefab not found: {prefabPath}" };

			var cloneResult = cloneMethod.Invoke( prefab, new object[] { new Vector3( x, y, z ) } );
			if ( cloneResult is GameObject go && go.IsValid() )
				return new { success = true, id = go.Id.ToString(), name = go.Name, position = new { x, y, z } };

			return new { error = "Failed to clone prefab" };
		}
		catch ( Exception e )
		{
			return new { error = $"Failed to load prefab: {e.Message}" };
		}
	}

	[McpTool("sbox_asset_info", "Returns metadata about an asset file.", ReadOnlyHint = true)]
	public object AssetInfo( string path )
	{
		try
		{
			if ( FileSystem.Mounted.FileExists( path ) )
			{
				var dot = path.LastIndexOf( '.' ); var ext = dot >= 0 ? path.Substring( dot ).ToLower() : "";
				var text = FileSystem.Mounted.ReadAllText( path );
				return new { found = true, source = "Mounted", path, extension = ext, sizeBytes = text.Length, lines = text.Split( '\n' ).Length };
			}
			if ( FileSystem.Data.FileExists( path ) )
			{
				var dot = path.LastIndexOf( '.' ); var ext = dot >= 0 ? path.Substring( dot ).ToLower() : "";
				var text = FileSystem.Data.ReadAllText( path );
				return new { found = true, source = "Data", path, extension = ext, sizeBytes = text.Length, lines = text.Split( '\n' ).Length };
			}
			return new { found = false, error = "File not found" };
		}
		catch ( Exception e )
		{
			return new { error = e.Message };
		}
	}

	[McpTool("sbox_asset_duplicate", "Duplicates a GameObject in the scene (deep clone).", DestructiveHint = true)]
	public object DuplicateObject( string guidStr )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid GUID" };
		var go = scene.Directory.FindByGuid( guid );
		if ( !go.IsValid() ) return new { error = "GameObject not found" };

		try
		{
			var cloneGo = go.Clone();
			if ( cloneGo == null || !cloneGo.IsValid() ) return new { error = "Clone failed" };
			return new { success = true, originalId = guidStr, cloneId = cloneGo.Id.ToString(), cloneName = cloneGo.Name };
		}
		catch ( Exception e )
		{
			return new { error = $"Clone failed: {e.Message}" };
		}
	}

	[McpTool("sbox_asset_find_missing", "Scans all GameObjects for components with null/missing references.", ReadOnlyHint = true)]
	public object FindMissingReferences()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var results = new List<object>();
		foreach ( var go in scene.GetAllObjects( true ) )
		{
			foreach ( var comp in go.Components.GetAll<Component>() )
			{
				if ( comp == null || !comp.IsValid() )
					results.Add( new { gameObject = go.Name, id = go.Id.ToString(), componentType = "null/invalid" } );
			}
		}
		return new { count = results.Count, results };
	}

	[McpTool("sbox_asset_bulk_import", "Imports multiple prefabs from a JSON array. Each entry: { path, x?, y?, z? }", DestructiveHint = true)]
	public object BulkImport( string entries )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		try
		{
			var list = JsonSerializer.Deserialize<List<JsonElement>>( entries );
			if ( list == null ) return new { error = "Invalid JSON array" };
			var results = new List<object>();
			var imported = 0;
			foreach ( var entry in list )
			{
				var path = entry.GetProperty( "path" ).GetString();
				var x = entry.TryGetProperty( "x", out var xp ) ? xp.GetSingle() : 0f;
				var y = entry.TryGetProperty( "y", out var yp ) ? yp.GetSingle() : 0f;
				var z = entry.TryGetProperty( "z", out var zp ) ? zp.GetSingle() : 0f;
				try
				{
					var pfType = TypeLibrary.GetType( "Sandbox.PrefabFile" );
					if ( pfType == null ) return new { error = "PrefabFile API unavailable" };
					var resourceLibType = TypeLibrary.GetType( "Sandbox.ResourceLibrary" );
					var getMethod = resourceLibType?.Methods.FirstOrDefault( m => m.Name == "Get" );
					if ( getMethod == null ) return new { error = "ResourceLibrary.Get not found" };
					var prefab = getMethod.Invoke( null, new object[] { path } );
					if ( prefab == null ) { results.Add( new { path, status = "error", error = "Prefab not found" } ); continue; }
					var sceneUtilType = TypeLibrary.GetType( "Sandbox.SceneUtility" );
					var getSceneMethod = sceneUtilType?.Methods.FirstOrDefault( m => m.Name == "GetPrefabScene" );
					if ( getSceneMethod == null ) { results.Add( new { path, status = "error", error = "SceneUtility.GetPrefabScene not found" } ); continue; }
					var prefabScene = getSceneMethod.Invoke( null, new object[] { prefab } );
					if ( prefabScene == null ) { results.Add( new { path, status = "error", error = "GetPrefabScene returned null" } ); continue; }
					var psTd = TypeLibrary.GetType( prefabScene.GetType() );
					var cloneMethod = psTd?.Methods.FirstOrDefault( m => m.Name == "Clone" );
					if ( cloneMethod == null ) { results.Add( new { path, status = "error", error = "Clone method not found" } ); continue; }
					var go = cloneMethod.Invoke( prefabScene, Array.Empty<object>() ) as GameObject;
					if ( go != null && go.IsValid() ) { go.WorldPosition = new Vector3( x, y, z ); imported++; results.Add( new { path, status = "ok", id = go.Id.ToString(), name = go.Name } ); }
					else results.Add( new { path, status = "error", error = "Clone failed" } );
				}
				catch ( Exception ex ) { results.Add( new { path, status = "error", error = ex.Message } ); }
			}
			return new { success = true, total = list.Count, imported, results };
		}
		catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_asset_export_json", "Exports a GameObject and its children to JSON.", ReadOnlyHint = true)]
	public object ExportJson( string guidStr )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid GUID" };
		var go = scene.Directory.FindByGuid( guid );
		if ( !go.IsValid() ) return new { error = "GameObject not found" };

		try
		{
			var data = SerializeGameObject( go );
			var json = JsonSerializer.Serialize( data, new JsonSerializerOptions { WriteIndented = true } );
			return new { success = true, id = guidStr, name = go.Name, json };
		}
		catch ( Exception e )
		{
			return new { error = $"Export failed: {e.Message}" };
		}
	}

	private static object SerializeGameObject( GameObject go )
	{
		var comps = go.Components.GetAll<Component>().ToList();
		var compDataList = new List<object>();
		foreach ( var comp in comps )
		{
			if ( comp == null || !comp.IsValid() ) continue;
			var td = TypeLibrary.GetType( comp.GetType() );
			var compData = new Dictionary<string, object> { ["type"] = td?.Name ?? "unknown" };
			if ( td != null )
			{
				foreach ( var prop in td.Properties )
				{
					if ( !prop.CanRead ) continue;
					try
					{
						var val = prop.GetValue( comp );
						if ( val != null && val is string )
							compData[prop.Name] = val;
						else if ( val != null && val is not Component && val is not GameObject )
							compData[prop.Name] = val;
					}
					catch { }
				}
			}
			compDataList.Add( compData );
		}

		var children = new List<object>();
		foreach ( var child in go.Children )
		{
			if ( child.IsValid() )
				children.Add( SerializeGameObject( child ) );
		}

		return new
		{
			name = go.Name,
			id = go.Id.ToString(),
			position = new { x = go.WorldPosition.x, y = go.WorldPosition.y, z = go.WorldPosition.z },
			rotation = new { pitch = go.WorldRotation.Pitch(), yaw = go.WorldRotation.Yaw(), roll = go.WorldRotation.Roll() },
			scale = go.WorldScale,
			components = compDataList,
			children
		};
	}

	[McpTool("sbox_asset_import_json", "Imports a GameObject hierarchy from a JSON string. Recreates child GameObjects, attaches components, and parses properties.", DestructiveHint = true)]
	public object ImportJson( string json, string parentGuidStr = null )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };

		GameObject parentGo = null;
		if ( !string.IsNullOrEmpty( parentGuidStr ) && Guid.TryParse( parentGuidStr, out var parentGuid ) )
		{
			parentGo = scene.Directory.FindByGuid( parentGuid );
		}

		try
		{
			using var doc = JsonDocument.Parse( json );
			var root = doc.RootElement;
			var createdGo = DeserializeGameObject( root, scene, parentGo );
			if ( createdGo == null ) return new { error = "Failed to deserialize GameObject" };
			return new { success = true, id = createdGo.Id.ToString(), name = createdGo.Name };
		}
		catch ( Exception e )
		{
			return new { error = $"Import failed: {e.Message}" };
		}
	}

	private static GameObject DeserializeGameObject( JsonElement el, Scene scene, GameObject parent )
	{
		var name = el.TryGetProperty( "name", out var np ) ? np.GetString() : "Imported Object";
		var go = new GameObject( true, name );
		if ( parent != null )
		{
			go.SetParent( parent );
		}
		else
		{
			go.SetParent( scene );
		}

		// Set position
		if ( el.TryGetProperty( "position", out var posEl ) )
		{
			var px = posEl.TryGetProperty( "x", out var x ) ? x.GetSingle() : 0f;
			var py = posEl.TryGetProperty( "y", out var y ) ? y.GetSingle() : 0f;
			var pz = posEl.TryGetProperty( "z", out var z ) ? z.GetSingle() : 0f;
			go.WorldPosition = new Vector3( px, py, pz );
		}

		// Set rotation
		if ( el.TryGetProperty( "rotation", out var rotEl ) )
		{
			var pitch = rotEl.TryGetProperty( "pitch", out var p ) ? p.GetSingle() : 0f;
			var yaw = rotEl.TryGetProperty( "yaw", out var y ) ? y.GetSingle() : 0f;
			var roll = rotEl.TryGetProperty( "roll", out var r ) ? r.GetSingle() : 0f;
			go.WorldRotation = new Angles( pitch, yaw, roll ).ToRotation();
		}

		// Set scale
		if ( el.TryGetProperty( "scale", out var scaleEl ) )
		{
			if ( scaleEl.ValueKind == JsonValueKind.Number )
			{
				go.LocalScale = new Vector3( scaleEl.GetSingle() );
			}
			else if ( scaleEl.ValueKind == JsonValueKind.Object )
			{
				var sx = scaleEl.TryGetProperty( "x", out var x ) ? x.GetSingle() : 1f;
				var sy = scaleEl.TryGetProperty( "y", out var y ) ? y.GetSingle() : 1f;
				var sz = scaleEl.TryGetProperty( "z", out var z ) ? z.GetSingle() : 1f;
				go.LocalScale = new Vector3( sx, sy, sz );
			}
		}

		// Deserialize Components
		if ( el.TryGetProperty( "components", out var compsEl ) && compsEl.ValueKind == JsonValueKind.Array )
		{
			foreach ( var compEl in compsEl.EnumerateArray() )
			{
				var typeName = compEl.TryGetProperty( "type", out var t ) ? t.GetString() : null;
				if ( string.IsNullOrEmpty( typeName ) || typeName == "unknown" ) continue;

				var typeDesc = FindComponentType( typeName );
				if ( typeDesc == null ) continue;

				var existingComp = go.Components.Get( typeDesc.TargetType );
				var comp = existingComp.IsValid() ? existingComp : go.Components.Create( typeDesc );

				if ( comp.IsValid() )
				{
					foreach ( var prop in typeDesc.Properties )
					{
						if ( !prop.CanWrite || prop.Name == "Type" ) continue;
						if ( compEl.TryGetProperty( prop.Name, out var valEl ) )
						{
							try
							{
								var converted = ConvertValue( valEl, prop.PropertyType );
								if ( converted != null )
								{
									prop.SetValue( comp, converted );
								}
							}
							catch { }
						}
					}
				}
			}
		}

		// Deserialize Children
		if ( el.TryGetProperty( "children", out var childrenEl ) && childrenEl.ValueKind == JsonValueKind.Array )
		{
			foreach ( var childEl in childrenEl.EnumerateArray() )
			{
				DeserializeGameObject( childEl, scene, go );
			}
		}

		return go;
	}

	public static TypeDescription FindComponentType( string name )
	{
		var types = TypeLibrary.GetTypes<Component>();
		foreach ( var t in types )
		{
			if ( string.Equals( t.Name, name, StringComparison.OrdinalIgnoreCase ) || string.Equals( t.FullName, name, StringComparison.OrdinalIgnoreCase ) ) return t;
		}
		foreach ( var t in types )
		{
			if ( t.Name.IndexOf( name, StringComparison.OrdinalIgnoreCase ) >= 0 ) return t;
		}
		return null;
	}

	public static object ConvertValue( JsonElement element, Type targetType )
	{
		if ( element.ValueKind == JsonValueKind.Null ) return null;

		if ( targetType == typeof( string ) )
			return element.GetString();

		if ( targetType == typeof( int ) || targetType == typeof( int? ) )
		{
			if ( element.ValueKind == JsonValueKind.Number ) return element.GetInt32();
			if ( element.ValueKind == JsonValueKind.String && int.TryParse( element.GetString(), out var val ) ) return val;
			return null;
		}

		if ( targetType == typeof( float ) || targetType == typeof( float? ) )
		{
			if ( element.ValueKind == JsonValueKind.Number ) return element.GetSingle();
			if ( element.ValueKind == JsonValueKind.String && float.TryParse( element.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val ) ) return val;
			return null;
		}

		if ( targetType == typeof( double ) || targetType == typeof( double? ) )
		{
			if ( element.ValueKind == JsonValueKind.Number ) return element.GetDouble();
			if ( element.ValueKind == JsonValueKind.String && double.TryParse( element.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val ) ) return val;
			return null;
		}

		if ( targetType == typeof( bool ) || targetType == typeof( bool? ) )
		{
			if ( element.ValueKind == JsonValueKind.True ) return true;
			if ( element.ValueKind == JsonValueKind.False ) return false;
			if ( element.ValueKind == JsonValueKind.String && bool.TryParse( element.GetString(), out var val ) ) return val;
			return null;
		}

		if ( targetType == typeof( Guid ) || targetType == typeof( Guid? ) )
		{
			if ( element.ValueKind == JsonValueKind.String && Guid.TryParse( element.GetString(), out var g ) ) return g;
			return null;
		}

		if ( targetType == typeof( Vector3 ) || targetType == typeof( Vector3? ) )
		{
			if ( element.ValueKind == JsonValueKind.Object )
			{
				var x = element.TryGetProperty( "x", out var xp ) ? xp.GetSingle() : 0f;
				var y = element.TryGetProperty( "y", out var yp ) ? yp.GetSingle() : 0f;
				var z = element.TryGetProperty( "z", out var zp ) ? zp.GetSingle() : 0f;
				return new Vector3( x, y, z );
			}
			if ( element.ValueKind == JsonValueKind.String )
			{
				var parts = element.GetString().Split( ',' );
				if ( parts.Length == 3 &&
					 float.TryParse( parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var x ) &&
					 float.TryParse( parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var y ) &&
					 float.TryParse( parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var z ) )
				{
					return new Vector3( x, y, z );
				}
			}
			return Vector3.Zero;
		}

		if ( targetType == typeof( Rotation ) || targetType == typeof( Rotation? ) )
		{
			if ( element.ValueKind == JsonValueKind.Object )
			{
				var pitch = element.TryGetProperty( "pitch", out var pp ) ? pp.GetSingle() : 0f;
				var yaw = element.TryGetProperty( "yaw", out var yp ) ? yp.GetSingle() : 0f;
				var roll = element.TryGetProperty( "roll", out var rp ) ? rp.GetSingle() : 0f;
				return Rotation.From( pitch, yaw, roll );
			}
			return Rotation.Identity;
		}

		if ( targetType == typeof( Angles ) || targetType == typeof( Angles? ) )
		{
			if ( element.ValueKind == JsonValueKind.Object )
			{
				var pitch = element.TryGetProperty( "pitch", out var pp ) ? pp.GetSingle() : 0f;
				var yaw = element.TryGetProperty( "yaw", out var yp ) ? yp.GetSingle() : 0f;
				var roll = element.TryGetProperty( "roll", out var rp ) ? rp.GetSingle() : 0f;
				return new Angles( pitch, yaw, roll );
			}
			if ( element.ValueKind == JsonValueKind.String )
			{
				var parts = element.GetString().Split( ',' );
				if ( parts.Length == 3 &&
					 float.TryParse( parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p ) &&
					 float.TryParse( parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var y ) &&
					 float.TryParse( parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var r ) )
				{
					return new Angles( p, y, r );
				}
			}
			return Angles.Zero;
		}

		if ( targetType == typeof( Color ) || targetType == typeof( Color? ) )
		{
			if ( element.ValueKind == JsonValueKind.String )
			{
				var str = element.GetString();
				if ( !string.IsNullOrEmpty( str ) && Color.TryParse( str, out var color ) )
					return color;
			}
			return Color.White;
		}

		if ( targetType.IsEnum )
		{
			if ( element.ValueKind == JsonValueKind.String )
			{
				var str = element.GetString();
				if ( !string.IsNullOrEmpty( str ) && Enum.TryParse( targetType, str, true, out var result ) )
					return result;
			}
			else if ( element.ValueKind == JsonValueKind.Number )
			{
				return Enum.ToObject( targetType, element.GetInt32() );
			}
			return null;
		}

		try
		{
			return JsonSerializer.Deserialize( element.GetRawText(), targetType );
		}
		catch
		{
			return null;
		}
	}
}
