using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Editor;

internal static class SceneMutationTools
{
	public static void Register(
		McpToolRegistry registry,
		IMcpSceneUndoService undoService )
	{
		registry.Register(
			"create_object",
			"Create a new GameObject",
			arguments => CreateObject( arguments, undoService ),
			new
			{
				type = "object",
				properties = new Dictionary<string, object>
				{
					{ "name", new { type = "string", description = "GameObject name" } },
					{ "position", new { type = "object", description = "Optional {x,y,z} vector" } }
				},
				required = new[] { "name" }
			},
			McpToolImpact.SceneMutation,
			supportsBatch: true );

		registry.Register(
			"delete_object",
			"Delete a GameObject by ID",
			arguments => DeleteObject( arguments, undoService ),
			new
			{
				type = "object",
				properties = new Dictionary<string, object>
				{
					{ "id", new { type = "string", description = "GUID of the object to delete" } }
				},
				required = new[] { "id" }
			},
			McpToolImpact.SceneMutation,
			supportsBatch: true );

		registry.Register(
			"update_object_properties",
			"Update the transform or properties of a specific GameObject",
			arguments => UpdateObjectProperties( arguments, undoService ),
			new
			{
				type = "object",
				properties = new Dictionary<string, object>
				{
					{ "id", new { type = "string", description = "GUID of the GameObject" } },
					{ "position", new { type = "object", description = "Optional {x,y,z}" } },
					{ "rotation", new { type = "object", description = "Optional {x,y,z,w} or {yaw,pitch,roll}" } },
					{ "scale", new { type = "object", description = "Optional Vector3 or number" } },
					{ "properties", new { type = "object", description = "Optional component property updates (e.g. {'Light.Brightness': 5})" } }
				},
				required = new[] { "id" }
			},
			McpToolImpact.SceneMutation,
			supportsBatch: true );
	}

	private static object CreateObject(
		JsonElement arguments,
		IMcpSceneUndoService undoService )
	{
		var name = arguments.GetProperty( "name" ).GetString();
		var position = arguments.TryGetProperty( "position", out var positionProperty )
			? JsonSerializer.Deserialize<Vector3>( positionProperty.GetRawText() )
			: Vector3.Zero;
		using var sceneScope = SceneEditorSession.Scope();
		if ( !undoService.IsAvailable )
			return new { success = false, message = "No active scene editor session is available" };

		return undoService.RecordCreation( "Create Object", () =>
		{
			var gameObject = new GameObject( true, name );
			gameObject.WorldPosition = position;
			return new { success = true, id = gameObject.Id.ToString(), name };
		} );
	}

	private static object DeleteObject(
		JsonElement arguments,
		IMcpSceneUndoService undoService )
	{
		var idText = arguments.GetProperty( "id" ).GetString();
		if ( Guid.TryParse( idText, out var id ) )
		{
			var gameObject = FindObject( id );
			if ( gameObject != null )
			{
				using var sceneScope = SceneEditorSession.Scope();
				if ( !undoService.IsAvailable )
					return new { success = false, message = "No active scene editor session is available" };
				var objectName = gameObject.Name;
				return undoService.RecordDeletion( "Delete Object", gameObject, () =>
				{
					gameObject.Destroy();
					return new { success = true, message = $"Deleted {objectName}" };
				} );
			}
		}

		return new { success = false, message = "Object not found" };
	}

	private static object UpdateObjectProperties(
		JsonElement arguments,
		IMcpSceneUndoService undoService )
	{
		var idText = arguments.GetProperty( "id" ).GetString();
		if ( !Guid.TryParse( idText, out var id ) )
			return new { success = false, message = "Invalid GUID format" };

		var gameObject = FindObject( id );
		if ( gameObject == null )
			return new { success = false, message = "GameObject not found" };

		var transform = ParseTransformMutation( arguments );
		var componentUpdates = ParseComponentMutations( gameObject, arguments );
		if ( !transform.HasChanges && componentUpdates.Count == 0 )
			return new { success = false, message = "No valid changes were supplied" };

		using var sceneScope = SceneEditorSession.Scope();
		if ( !undoService.IsAvailable )
			return new { success = false, message = "No active scene editor session is available" };

		return undoService.RecordChanges(
			"Update Object",
			gameObject,
			componentUpdates.Select( update => update.Component ),
			() =>
			{
				ApplyTransformMutation( gameObject, transform );
				ApplyComponentMutations( componentUpdates );
				return new { success = true, message = $"Updated {gameObject.Name}" };
			} );
	}

	private static TransformMutation ParseTransformMutation( JsonElement arguments )
	{
		var mutation = new TransformMutation();
		if ( arguments.TryGetProperty( "position", out var positionProperty ) )
		{
			mutation.HasPosition = true;
			mutation.Position = JsonSerializer.Deserialize<Vector3>(
				positionProperty.GetRawText() );
		}

		if ( arguments.TryGetProperty( "rotation", out var rotationProperty )
			&& rotationProperty.ValueKind == JsonValueKind.Object )
		{
			mutation.HasRotation = true;
			if ( rotationProperty.TryGetProperty( "w", out _ ) )
			{
				mutation.Rotation = JsonSerializer.Deserialize<Rotation>(
					rotationProperty.GetRawText() );
			}
			else
			{
				var yaw = rotationProperty.TryGetProperty( "yaw", out var yawProperty )
					? yawProperty.GetSingle()
					: 0f;
				var pitch = rotationProperty.TryGetProperty( "pitch", out var pitchProperty )
					? pitchProperty.GetSingle()
					: 0f;
				var roll = rotationProperty.TryGetProperty( "roll", out var rollProperty )
					? rollProperty.GetSingle()
					: 0f;
				mutation.Rotation = Rotation.From( pitch, yaw, roll );
			}
		}

		if ( arguments.TryGetProperty( "scale", out var scaleProperty ) )
		{
			mutation.HasScale = true;
			mutation.Scale = scaleProperty.ValueKind == JsonValueKind.Number
				? new Vector3( scaleProperty.GetSingle() )
				: JsonSerializer.Deserialize<Vector3>( scaleProperty.GetRawText() );
		}

		return mutation;
	}

	private static List<ComponentMutation> ParseComponentMutations(
		GameObject gameObject,
		JsonElement arguments )
	{
		var mutations = new List<ComponentMutation>();
		if ( !arguments.TryGetProperty( "properties", out var properties )
			|| properties.ValueKind != JsonValueKind.Object )
			return mutations;

		foreach ( var update in properties.EnumerateObject() )
		{
			var parts = update.Name.Split( '.' );
			if ( parts.Length != 2 ) continue;

			var component = gameObject.Components.GetAll().FirstOrDefault( candidate =>
				candidate.GetType().Name.Equals(
					parts[0],
					StringComparison.OrdinalIgnoreCase ) );
			var property = component?.GetType().GetProperty(
				parts[1],
				BindingFlags.Public | BindingFlags.Instance );
			if ( property == null || !property.CanWrite ) continue;

			var value = JsonSerializer.Deserialize(
				update.Value.GetRawText(),
				property.PropertyType );
			mutations.Add( new( component, property, value ) );
		}

		return mutations;
	}

	private static void ApplyTransformMutation(
		GameObject gameObject,
		TransformMutation mutation )
	{
		if ( mutation.HasPosition )
			gameObject.WorldPosition = mutation.Position;
		if ( mutation.HasRotation )
			gameObject.WorldRotation = mutation.Rotation;
		if ( mutation.HasScale )
			gameObject.WorldScale = mutation.Scale;
	}

	private static void ApplyComponentMutations(
		IEnumerable<ComponentMutation> mutations )
	{
		foreach ( var mutation in mutations )
			mutation.Property.SetValue( mutation.Component, mutation.Value );
	}

	private static GameObject FindObject( Guid id )
		=> Game.ActiveScene?.GetAllObjects( true )
			.FirstOrDefault( gameObject => gameObject.Id == id );

	private sealed class TransformMutation
	{
		public bool HasPosition { get; set; }
		public Vector3 Position { get; set; }
		public bool HasRotation { get; set; }
		public Rotation Rotation { get; set; }
		public bool HasScale { get; set; }
		public Vector3 Scale { get; set; }
		public bool HasChanges => HasPosition || HasRotation || HasScale;
	}

	private sealed record ComponentMutation(
		Component Component,
		PropertyInfo Property,
		object Value );
}
