using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Editor;

internal static class SceneAdvancedMutationTools
{
	public static void Register(
		McpToolRegistry registry,
		IMcpSceneUndoService undoService )
	{
		registry.Register(
			"duplicate_object",
			"Duplicate a GameObject, including its children and components",
			arguments => DuplicateObject( arguments, undoService ),
			Schema(
				new Dictionary<string, object>
				{
					{ "id", StringProperty( "GUID of the GameObject to duplicate" ) },
					{ "name", StringProperty( "Optional name for the duplicate" ) }
				},
				"id" ),
			McpToolImpact.SceneMutation,
			supportsBatch: true );

		registry.Register(
			"set_parent",
			"Reparent a GameObject while preserving its world transform by default",
			arguments => SetParent( arguments, undoService ),
			Schema(
				new Dictionary<string, object>
				{
					{ "id", StringProperty( "GUID of the GameObject to reparent" ) },
					{ "parent_id", StringProperty( "GUID of the new parent, or 'root'" ) },
					{
						"keep_world_transform",
						new
						{
							type = "boolean",
							description = "Preserve world position, rotation, and scale (default true)"
						}
					}
				},
				"id",
				"parent_id" ),
			McpToolImpact.SceneMutation,
			supportsBatch: true );

		registry.Register(
			"add_component",
			"Add a component to a GameObject and optionally initialize writable properties",
			arguments => AddComponent( arguments, undoService ),
			Schema(
				new Dictionary<string, object>
				{
					{ "id", StringProperty( "GUID of the target GameObject" ) },
					{ "type", StringProperty( "Component type name, such as ModelRenderer" ) },
					{
						"properties",
						new
						{
							type = "object",
							description = "Optional public writable component properties"
						}
					}
				},
				"id",
				"type" ),
			McpToolImpact.SceneMutation,
			supportsBatch: true );

		registry.Register(
			"remove_component",
			"Remove a component by component GUID, or by GameObject GUID and component type",
			arguments => RemoveComponent( arguments, undoService ),
			Schema(
				new Dictionary<string, object>
				{
					{ "id", StringProperty( "Component GUID, or GameObject GUID when type is supplied" ) },
					{ "type", StringProperty( "Component type when id identifies a GameObject" ) }
				},
				"id" ),
			McpToolImpact.SceneMutation,
			supportsBatch: true );

		registry.Register(
			"instantiate_prefab",
			"Instantiate a linked prefab asset in the active scene",
			arguments => InstantiatePrefab( arguments, undoService ),
			Schema(
				new Dictionary<string, object>
				{
					{ "path", StringProperty( "Project-relative .prefab asset path" ) },
					{ "name", StringProperty( "Optional instance name" ) },
					{ "parent_id", StringProperty( "Optional parent GUID, or 'root'" ) },
					{ "position", ObjectProperty( "Optional world {x,y,z} vector" ) },
					{ "rotation", ObjectProperty( "Optional {x,y,z,w} or {yaw,pitch,roll}" ) },
					{ "scale", new { description = "Optional Vector3 object or uniform number" } }
				},
				"path" ),
			McpToolImpact.SceneMutation,
			supportsBatch: true );

		registry.Register(
			"break_prefab",
			"Break a prefab instance link while keeping the instantiated hierarchy",
			arguments => BreakPrefab( arguments, undoService ),
			Schema(
				new Dictionary<string, object>
				{
					{ "id", StringProperty( "GUID of the prefab instance root" ) }
				},
				"id" ),
			McpToolImpact.SceneMutation,
			supportsBatch: true );
	}

	private static object DuplicateObject(
		JsonElement arguments,
		IMcpSceneUndoService undoService )
	{
		var source = ResolveObjectArgument( arguments, "id" );
		using var sceneScope = SceneEditorSession.Scope();
		if ( !undoService.IsAvailable ) return NoActiveSession();

		return undoService.RecordCreation( "Duplicate Object", () =>
		{
			var clone = source.Clone();
			clone.WorldTransform = source.WorldTransform;
			source.AddSibling( clone, false );

			if ( arguments.TryGetProperty( "name", out var nameProperty )
				&& !string.IsNullOrWhiteSpace( nameProperty.GetString() ) )
			{
				clone.Name = nameProperty.GetString();
			}

			return new
			{
				success = true,
				id = clone.Id.ToString(),
				name = clone.Name,
				sourceId = source.Id.ToString(),
				parentId = GetParentId( clone )
			};
		} );
	}

	private static object SetParent(
		JsonElement arguments,
		IMcpSceneUndoService undoService )
	{
		var target = ResolveObjectArgument( arguments, "id" );
		var parentText = arguments.GetProperty( "parent_id" ).GetString();
		var newParent = string.Equals( parentText, "root", StringComparison.OrdinalIgnoreCase )
			? target.Scene as GameObject
			: ResolveObject( parentText );
		if ( newParent == null )
			return new { success = false, message = "Parent GameObject not found" };
		if ( newParent == target || IsDescendantOf( newParent, target ) )
			return new { success = false, message = "A GameObject cannot be parented to itself or its descendant" };

		var keepWorldTransform = !arguments.TryGetProperty(
				"keep_world_transform",
				out var keepWorldProperty )
			|| keepWorldProperty.GetBoolean();

		using var sceneScope = SceneEditorSession.Scope();
		if ( !undoService.IsAvailable ) return NoActiveSession();

		return undoService.RecordChanges(
			"Set Parent",
			target,
			Array.Empty<Component>(),
			() =>
			{
				target.SetParent( newParent, keepWorldTransform );
				return new
				{
					success = true,
					id = target.Id.ToString(),
					parentId = GetParentId( target ),
					keepWorldTransform
				};
			},
			GameObjectUndoFlags.All );
	}

	private static object AddComponent(
		JsonElement arguments,
		IMcpSceneUndoService undoService )
	{
		var gameObject = ResolveObjectArgument( arguments, "id" );
		var typeName = arguments.GetProperty( "type" ).GetString();
		var componentType = ResolveComponentType( typeName );
		var propertyValues = ParseComponentProperties(
			componentType.TargetType,
			arguments );

		using var sceneScope = SceneEditorSession.Scope();
		if ( !undoService.IsAvailable ) return NoActiveSession();

		return undoService.RecordComponentCreation(
			$"Add {componentType.Name}",
			() =>
			{
				var component = gameObject.Components.Create( componentType, true );
				foreach ( var propertyValue in propertyValues )
					propertyValue.Property.SetValue( component, propertyValue.Value );

				return new
				{
					success = true,
					id = component.Id.ToString(),
					type = component.GetType().Name,
					gameObjectId = gameObject.Id.ToString()
				};
			} );
	}

	private static object RemoveComponent(
		JsonElement arguments,
		IMcpSceneUndoService undoService )
	{
		var idText = arguments.GetProperty( "id" ).GetString();
		var typeName = arguments.TryGetProperty( "type", out var typeProperty )
			? typeProperty.GetString()
			: null;
		var component = ResolveComponent( idText, typeName );
		if ( component == null )
			return new { success = false, message = "Component not found" };

		using var sceneScope = SceneEditorSession.Scope();
		if ( !undoService.IsAvailable ) return NoActiveSession();
		var componentName = component.GetType().Name;
		var objectId = component.GameObject?.Id.ToString();

		return undoService.RecordComponentDeletion(
			$"Remove {componentName}",
			component,
			() =>
			{
				component.Destroy();
				return new
				{
					success = true,
					type = componentName,
					gameObjectId = objectId
				};
			} );
	}

	private static object InstantiatePrefab(
		JsonElement arguments,
		IMcpSceneUndoService undoService )
	{
		var path = arguments.GetProperty( "path" ).GetString();
		if ( !IsSafePrefabPath( path ) )
			return new { success = false, message = "Prefab path must be a project-relative .prefab asset path" };

		var parent = ResolveOptionalParent( arguments, "parent_id" );
		var position = ParseVector3( arguments, "position", Vector3.Zero );
		var rotation = ParseRotation( arguments, "rotation", Rotation.Identity );
		var scale = ParseScale( arguments, "scale", Vector3.One );
		var name = arguments.TryGetProperty( "name", out var nameProperty )
			? nameProperty.GetString()
			: null;

		using var sceneScope = SceneEditorSession.Scope();
		if ( !undoService.IsAvailable ) return NoActiveSession();

		return undoService.RecordCreation( "Instantiate Prefab", () =>
		{
			var instance = GameObject.Clone(
				path,
				new Transform( position, rotation ),
				parent,
				name: string.IsNullOrWhiteSpace( name ) ? null : name );
			if ( instance == null )
				return new { success = false, message = $"Could not instantiate prefab '{path}'" };

			instance.WorldScale = scale;
			return new
			{
				success = true,
				id = instance.Id.ToString(),
				name = instance.Name,
				path,
				parentId = GetParentId( instance )
			};
		} );
	}

	private static object BreakPrefab(
		JsonElement arguments,
		IMcpSceneUndoService undoService )
	{
		var gameObject = ResolveObjectArgument( arguments, "id" );
		if ( !gameObject.IsPrefabInstance )
			return new { success = false, message = "GameObject is not a prefab instance" };

		using var sceneScope = SceneEditorSession.Scope();
		if ( !undoService.IsAvailable ) return NoActiveSession();

		return undoService.RecordChanges(
			"Break Prefab",
			gameObject,
			gameObject.Components.GetAll(),
			() =>
			{
				gameObject.BreakFromPrefab();
				return new
				{
					success = true,
					id = gameObject.Id.ToString(),
					name = gameObject.Name
				};
			},
			GameObjectUndoFlags.All );
	}

	private static GameObject ResolveObjectArgument(
		JsonElement arguments,
		string propertyName )
	{
		var value = arguments.GetProperty( propertyName ).GetString();
		return ResolveObject( value )
			?? throw new InvalidOperationException( "GameObject not found" );
	}

	private static GameObject ResolveObject( string idText )
	{
		if ( !Guid.TryParse( idText, out var id ) ) return null;
		return Game.ActiveScene?.GetAllObjects( true )
			.FirstOrDefault( gameObject => gameObject.Id == id );
	}

	private static GameObject ResolveOptionalParent(
		JsonElement arguments,
		string propertyName )
	{
		if ( !arguments.TryGetProperty( propertyName, out var parentProperty )
			|| string.IsNullOrWhiteSpace( parentProperty.GetString() )
			|| string.Equals(
				parentProperty.GetString(),
				"root",
				StringComparison.OrdinalIgnoreCase ) )
		{
			return null;
		}

		return ResolveObject( parentProperty.GetString() )
			?? throw new InvalidOperationException( "Parent GameObject not found" );
	}

	private static Component ResolveComponent( string idText, string typeName )
	{
		if ( !Guid.TryParse( idText, out var id ) ) return null;
		var objects = Game.ActiveScene?.GetAllObjects( true ) ?? Enumerable.Empty<GameObject>();
		var component = objects.SelectMany( gameObject => gameObject.Components.GetAll() )
			.FirstOrDefault( candidate => candidate.Id == id );
		if ( component != null ) return component;

		var gameObject = objects.FirstOrDefault( candidate => candidate.Id == id );
		if ( gameObject == null || string.IsNullOrWhiteSpace( typeName ) ) return null;
		var matches = gameObject.Components.GetAll().Where( candidate =>
				ComponentTypeMatches( candidate.GetType(), typeName ) )
			.ToArray();
		if ( matches.Length > 1 )
			throw new InvalidOperationException(
				$"Multiple '{typeName}' components exist; remove one by its component GUID" );
		return matches.SingleOrDefault();
	}

	private static TypeDescription ResolveComponentType( string typeName )
	{
		if ( string.IsNullOrWhiteSpace( typeName ) )
			throw new InvalidOperationException( "Component type is required" );
		var types = TypeLibrary.GetTypes<Component>()
			.Where( type => !type.IsAbstract && !type.IsInterface )
			.ToArray();
		var exact = types.FirstOrDefault( type =>
			string.Equals( type.Name, typeName, StringComparison.OrdinalIgnoreCase )
			|| string.Equals( type.ClassName, typeName, StringComparison.OrdinalIgnoreCase ) );
		if ( exact != null ) return exact;

		var matches = types.Where( type =>
				type.Name.Contains( typeName, StringComparison.OrdinalIgnoreCase ) )
			.ToArray();
		if ( matches.Length == 1 ) return matches[0];
		if ( matches.Length > 1 )
			throw new InvalidOperationException(
				$"Component type '{typeName}' is ambiguous: {string.Join( ", ", matches.Select( type => type.Name ).Order() )}" );
		throw new InvalidOperationException( $"Component type '{typeName}' was not found" );
	}

	private static List<ComponentPropertyValue> ParseComponentProperties(
		Type componentType,
		JsonElement arguments )
	{
		var values = new List<ComponentPropertyValue>();
		if ( !arguments.TryGetProperty( "properties", out var properties )
			|| properties.ValueKind == JsonValueKind.Null )
			return values;
		if ( properties.ValueKind != JsonValueKind.Object )
			throw new InvalidOperationException( "properties must be a JSON object" );

		foreach ( var item in properties.EnumerateObject() )
		{
			var property = componentType.GetProperty(
				item.Name,
				BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase );
			if ( property == null || !property.CanWrite || property.GetIndexParameters().Length != 0 )
				throw new InvalidOperationException(
					$"Property '{item.Name}' is not a public writable property of {componentType.Name}" );
			var value = JsonSerializer.Deserialize(
				item.Value.GetRawText(),
				property.PropertyType );
			values.Add( new( property, value ) );
		}

		return values;
	}

	private static bool ComponentTypeMatches( Type type, string typeName )
		=> string.Equals( type.Name, typeName, StringComparison.OrdinalIgnoreCase )
			|| string.Equals( type.FullName, typeName, StringComparison.OrdinalIgnoreCase );

	private static bool IsDescendantOf( GameObject candidate, GameObject ancestor )
	{
		for ( var current = candidate.Parent; current != null; current = current.Parent )
		{
			if ( current == ancestor ) return true;
		}
		return false;
	}

	internal static bool IsSafePrefabPath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path )
			|| Path.IsPathRooted( path )
			|| path.Contains( '\\' )
			|| path.Split( '/', StringSplitOptions.RemoveEmptyEntries ).Contains( ".." ) )
			return false;
		return path.EndsWith( ".prefab", StringComparison.OrdinalIgnoreCase );
	}

	private static Vector3 ParseVector3(
		JsonElement arguments,
		string propertyName,
		Vector3 fallback )
		=> arguments.TryGetProperty( propertyName, out var property )
			? JsonSerializer.Deserialize<Vector3>( property.GetRawText() )
			: fallback;

	private static Vector3 ParseScale(
		JsonElement arguments,
		string propertyName,
		Vector3 fallback )
	{
		if ( !arguments.TryGetProperty( propertyName, out var property ) )
			return fallback;
		return property.ValueKind == JsonValueKind.Number
			? new Vector3( property.GetSingle() )
			: JsonSerializer.Deserialize<Vector3>( property.GetRawText() );
	}

	private static Rotation ParseRotation(
		JsonElement arguments,
		string propertyName,
		Rotation fallback )
	{
		if ( !arguments.TryGetProperty( propertyName, out var property )
			|| property.ValueKind != JsonValueKind.Object )
			return fallback;
		if ( property.TryGetProperty( "w", out _ ) )
			return JsonSerializer.Deserialize<Rotation>( property.GetRawText() );
		var yaw = property.TryGetProperty( "yaw", out var yawValue )
			? yawValue.GetSingle()
			: 0f;
		var pitch = property.TryGetProperty( "pitch", out var pitchValue )
			? pitchValue.GetSingle()
			: 0f;
		var roll = property.TryGetProperty( "roll", out var rollValue )
			? rollValue.GetSingle()
			: 0f;
		return Rotation.From( pitch, yaw, roll );
	}

	private static string GetParentId( GameObject gameObject )
		=> gameObject.Parent == null || gameObject.Parent is Scene
			? null
			: gameObject.Parent.Id.ToString();

	private static object NoActiveSession()
		=> new { success = false, message = "No active scene editor session is available" };

	private static object Schema(
		Dictionary<string, object> properties,
		params string[] required )
		=> new { type = "object", properties, required };

	private static object StringProperty( string description )
		=> new { type = "string", description };

	private static object ObjectProperty( string description )
		=> new { type = "object", description };

	private sealed record ComponentPropertyValue(
		PropertyInfo Property,
		object Value );
}
