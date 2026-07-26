using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Editor;

internal static class SceneQueryTools
{
	public static void Register( McpToolRegistry registry )
	{
		registry.Register( "list_objects", "List GameObjects in the scene (max 50)", _ =>
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			return scene.GetAllObjects( true )
				.Take( 50 )
				.Select( gameObject => new { gameObject.Name, gameObject.Id } )
				.ToList();
		}, null, McpToolImpact.ReadOnly );

		registry.Register( "get_selection", "Get currently selected GameObjects", _ =>
		{
			var selection = SceneEditorSession.Active?.Selection;
			if ( selection == null ) return new List<object>();

			return selection.OfType<GameObject>().Select( gameObject => new
			{
				name = gameObject.Name,
				id = gameObject.Id,
				components = gameObject.Components.GetAll()
					.Select( component => component.GetType().Name )
					.ToList(),
				componentRefs = gameObject.Components.GetAll()
					.Select( component => new
					{
						id = component.Id.ToString(),
						type = component.GetType().Name
					} )
					.ToList()
			} ).ToList();
		}, null, McpToolImpact.ReadOnly );

		registry.Register( "get_scene_hierarchy", "Get the hierarchical tree of the active scene", _ =>
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";

			return scene.GetAllObjects( true )
				.Where( gameObject => gameObject.Parent == null || gameObject.Parent == scene )
				.Select( BuildHierarchyNode )
				.ToList();
		}, null, McpToolImpact.ReadOnly );

		registry.Register(
			"get_object_properties",
			"Get detailed properties of a specific GameObject",
			GetObjectProperties,
			new
			{
				type = "object",
				properties = new Dictionary<string, object>
				{
					{ "id", new { type = "string", description = "GUID of the GameObject to inspect" } }
				},
				required = new[] { "id" }
			},
			McpToolImpact.ReadOnly );
	}

	private static object GetObjectProperties( System.Text.Json.JsonElement arguments )
	{
		var idText = arguments.GetProperty( "id" ).GetString();
		if ( !Guid.TryParse( idText, out var id ) ) return "Invalid GUID format";

		var gameObject = Game.ActiveScene?.GetAllObjects( true )
			.FirstOrDefault( candidate => candidate.Id == id );
		if ( gameObject == null ) return "GameObject not found";

		var componentData = new List<object>();
		foreach ( var component in gameObject.Components.GetAll() )
		{
			var type = component.GetType();
			var properties = new Dictionary<string, object>();
			foreach ( var property in type.GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
			{
				try
				{
					if ( !property.CanRead || property.GetIndexParameters().Length != 0 ) continue;
					var value = property.GetValue( component );
					if ( value != null ) properties[property.Name] = value.ToString();
				}
				catch
				{
					// Some engine properties can throw while the editor is mutating the scene.
				}
			}

			foreach ( var field in type.GetFields( BindingFlags.Public | BindingFlags.Instance ) )
			{
				try
				{
					var value = field.GetValue( component );
					if ( value != null ) properties[field.Name] = value.ToString();
				}
				catch
				{
					// Continue collecting the remaining fields.
				}
			}

			componentData.Add( new
			{
				id = component.Id.ToString(),
				type = type.Name,
				properties
			} );
		}

		return new
		{
			name = gameObject.Name,
			id = gameObject.Id.ToString(),
			enabled = gameObject.Enabled,
			parent = gameObject.Parent?.Id.ToString(),
			transform = new
			{
				position = new
				{
					x = gameObject.WorldPosition.x,
					y = gameObject.WorldPosition.y,
					z = gameObject.WorldPosition.z
				},
				rotation = new
				{
					x = gameObject.WorldRotation.x,
					y = gameObject.WorldRotation.y,
					z = gameObject.WorldRotation.z,
					w = gameObject.WorldRotation.w
				},
				scale = new
				{
					x = gameObject.WorldScale.x,
					y = gameObject.WorldScale.y,
					z = gameObject.WorldScale.z
				}
			},
			components = componentData
		};
	}

	private static object BuildHierarchyNode( GameObject gameObject )
	{
		return new
		{
			name = gameObject.Name,
			id = gameObject.Id.ToString(),
			enabled = gameObject.Enabled,
			components = gameObject.Components.GetAll()
				.Select( component => component.GetType().Name )
				.ToList(),
			componentRefs = gameObject.Components.GetAll()
				.Select( component => new
				{
					id = component.Id.ToString(),
					type = component.GetType().Name
				} )
				.ToList(),
			children = gameObject.Children.Select( BuildHierarchyNode ).ToList()
		};
	}
}
