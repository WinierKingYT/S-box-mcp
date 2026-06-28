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
	private static readonly JsonSerializerOptions IndentedJsonOpts = new() { WriteIndented = true };

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

	[McpTool("sbox_asset_load_prefab", "Spawns a prefab file into the scene at a position.", OptionalParams = new[]{"x", "y", "z", "useQueue"}, DestructiveHint = true)]
	public object LoadPrefab( string prefabPath, float x = 0, float y = 0, float z = 0, bool useQueue = false )
	{
		try
		{
			if ( useQueue )
			{
				McpReplicationQueue.Enqueue( () =>
				{
					var prefabType = TypeLibrary.GetType( "Sandbox.Prefab" );
					if ( prefabType == null ) return;
					var loadMethod = prefabType.Methods.FirstOrDefault( m => m.Name == "Load" );
					var cloneMethod = prefabType.Methods.FirstOrDefault( m => m.Name == "Clone" );
					if ( loadMethod == null || cloneMethod == null ) return;

					var prefab = loadMethod.Invoke( null, new object[] { prefabPath } );
					if ( prefab == null ) return;

					cloneMethod.Invoke( prefab, new object[] { new Vector3( x, y, z ) } );
				} );
				return new { success = true, queued = true, path = prefabPath, position = new { x, y, z } };
			}

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
			var json = JsonSerializer.Serialize( data, IndentedJsonOpts );
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

	public static Vector3? ParseVector3( string str )
	{
		if ( string.IsNullOrWhiteSpace( str ) ) return null;
		var parts = str.Split( ',' );
		if ( parts.Length == 3 &&
			 float.TryParse( parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var x ) &&
			 float.TryParse( parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var y ) &&
			 float.TryParse( parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var z ) )
		{
			return new Vector3( x, y, z );
		}
		return null;
	}

	public static Rotation? ParseRotation( string str )
	{
		if ( string.IsNullOrWhiteSpace( str ) ) return null;
		var parts = str.Split( ',' );
		if ( parts.Length == 3 &&
			 float.TryParse( parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pitch ) &&
			 float.TryParse( parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var yaw ) &&
			 float.TryParse( parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var roll ) )
		{
			return Rotation.From( pitch, yaw, roll );
		}
		return null;
	}

	public static Color? ParseColor( string str )
	{
		if ( string.IsNullOrWhiteSpace( str ) ) return null;
		var parts = str.Split( ',' );
		if ( parts.Length >= 3 &&
			 float.TryParse( parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var r ) &&
			 float.TryParse( parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var g ) &&
			 float.TryParse( parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var b ) )
		{
			var a = 1f;
			if ( parts.Length >= 4 && float.TryParse( parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedA ) )
			{
				a = parsedA;
			}
			return new Color( r, g, b, a );
		}
		if ( Color.TryParse( str, out var color ) )
		{
			return color;
		}
		return null;
	}

	[McpTool("sbox_generate_asset", "Procedurally generates wav audio or bmp texture assets and writes them atomically to the project directories to prevent IO kilitlenmeleri.", OptionalParams = new[]{"waveType", "frequency", "duration", "pattern", "width", "height", "colorHex"}, DestructiveHint = true)]
	public object GenerateAsset( string path, string type, string waveType = "sine", float frequency = 440f, float duration = 1.0f, string pattern = "solid", int width = 256, int height = 256, string colorHex = "#ffffff" )
	{
		try
		{
			if ( string.IsNullOrEmpty( path ) ) return new { error = "Path cannot be empty" };
			if ( path.Contains( ".." ) ) return new { error = "Path traversal not allowed" };

			byte[] assetBytes;
			var t = type.ToLowerInvariant();
			if ( t == "audio" || t == "sound" )
			{
				if ( !path.EndsWith( ".wav", StringComparison.OrdinalIgnoreCase ) )
					return new { error = "Audio assets must have a .wav extension" };
				assetBytes = GenerateWavBytes( waveType, frequency, duration );
			}
			else if ( t == "texture" || t == "image" )
			{
				if ( !path.EndsWith( ".bmp", StringComparison.OrdinalIgnoreCase ) && !path.EndsWith( ".png", StringComparison.OrdinalIgnoreCase ) )
					return new { error = "Texture assets generated procedurally must be written as .bmp or .png" };
				
				Color color = Color.White;
				if ( !string.IsNullOrEmpty( colorHex ) )
				{
					Color.TryParse( colorHex, out color );
				}
				assetBytes = GenerateBmpBytes( pattern, width, height, color );
			}
			else
			{
				return new { error = $"Unsupported asset type '{type}'. Choose 'audio' or 'texture'." };
			}

			var tempDir = "temp_assets";
			if ( !System.IO.Directory.Exists( tempDir ) )
			{
				System.IO.Directory.CreateDirectory( tempDir );
			}

			var tempFilePath = System.IO.Path.Combine( tempDir, Guid.NewGuid().ToString() + ".tmp" );
			System.IO.File.WriteAllBytes( tempFilePath, assetBytes );

			var destFullPath = System.IO.Path.Combine( System.IO.Directory.GetCurrentDirectory(), path.Replace( "/", System.IO.Path.DirectorySeparatorChar.ToString() ) );
			var destDir = System.IO.Path.GetDirectoryName( destFullPath );
			if ( !System.IO.Directory.Exists( destDir ) )
			{
				System.IO.Directory.CreateDirectory( destDir );
			}

			if ( System.IO.File.Exists( destFullPath ) )
			{
				System.IO.File.Delete( destFullPath );
			}
			System.IO.File.Move( tempFilePath, destFullPath );

			return new { success = true, path, type, sizeBytes = assetBytes.Length, note = "Asset generated and swapped atomically." };
		}
		catch ( Exception e )
		{
			return new { error = e.Message };
		}
	}

	private static byte[] GenerateWavBytes( string waveType, float frequency, float duration )
	{
		int sampleRate = 22050;
		int numSamples = (int)(sampleRate * duration);
		int subChunk2Size = numSamples * 2;
		int chunkSize = 36 + subChunk2Size;

		var ms = new System.IO.MemoryStream();
		using var bw = new System.IO.BinaryWriter( ms );

		bw.Write( System.Text.Encoding.ASCII.GetBytes( "RIFF" ) );
		bw.Write( chunkSize );
		bw.Write( System.Text.Encoding.ASCII.GetBytes( "WAVE" ) );

		bw.Write( System.Text.Encoding.ASCII.GetBytes( "fmt " ) );
		bw.Write( 16 );
		bw.Write( (short)1 );
		bw.Write( (short)1 );
		bw.Write( sampleRate );
		bw.Write( sampleRate * 2 );
		bw.Write( (short)2 );
		bw.Write( (short)16 );

		bw.Write( System.Text.Encoding.ASCII.GetBytes( "data" ) );
		bw.Write( subChunk2Size );

		var random = new Random();
		for ( int i = 0; i < numSamples; i++ )
		{
			double t = (double)i / sampleRate;
			double value = 0;
			var wt = waveType.ToLowerInvariant();
			if ( wt == "sine" )
				value = Math.Sin( 2.0 * Math.PI * frequency * t );
			else if ( wt == "square" )
				value = Math.Sign( Math.Sin( 2.0 * Math.PI * frequency * t ) );
			else if ( wt == "sawtooth" )
				value = 2.0 * (t * frequency - Math.Floor( 0.5 + t * frequency ));
			else
				value = random.NextDouble() * 2.0 - 1.0;

			bw.Write( (short)(value * 28000) );
		}

		return ms.ToArray();
	}

	private static byte[] GenerateBmpBytes( string pattern, int width, int height, Color color )
	{
		int rowSize = ((width * 24 + 31) / 32) * 4;
		int pixelDataSize = rowSize * height;
		int fileSize = 54 + pixelDataSize;

		var ms = new System.IO.MemoryStream();
		using var bw = new System.IO.BinaryWriter( ms );

		bw.Write( (byte)'B' );
		bw.Write( (byte)'M' );
		bw.Write( fileSize );
		bw.Write( (int)0 );
		bw.Write( 54 );

		bw.Write( 40 );
		bw.Write( width );
		bw.Write( height );
		bw.Write( (short)1 );
		bw.Write( (short)24 );
		bw.Write( 0 );
		bw.Write( pixelDataSize );
		bw.Write( 2835 );
		bw.Write( 2835 );
		bw.Write( 0 );
		bw.Write( 0 );

		byte r = (byte)(color.r * 255f);
		byte g = (byte)(color.g * 255f);
		byte b = (byte)(color.b * 255f);

		var random = new Random();

		for ( int y = 0; y < height; y++ )
		{
			int bytesWritten = 0;
			for ( int x = 0; x < width; x++ )
			{
				byte pr = r, pg = g, pb = b;
				var pat = pattern.ToLowerInvariant();
				if ( pat == "grid" )
				{
					if ( x % 32 == 0 || y % 32 == 0 )
					{
						pr = (byte)(255 - r);
						pg = (byte)(255 - g);
						pb = (byte)(255 - b);
					}
				}
				else if ( pat == "noise" )
				{
					float noise = (float)random.NextDouble() * 0.4f - 0.2f;
					pr = (byte)Math.Clamp( r + noise * 255f, 0, 255 );
					pg = (byte)Math.Clamp( g + noise * 255f, 0, 255 );
					pb = (byte)Math.Clamp( b + noise * 255f, 0, 255 );
				}

				bw.Write( pb );
				bw.Write( pg );
				bw.Write( pr );
				bytesWritten += 3;
			}
			while ( bytesWritten < rowSize )
			{
				bw.Write( (byte)0 );
				bytesWritten++;
			}
		}

		return ms.ToArray();
	}
}
