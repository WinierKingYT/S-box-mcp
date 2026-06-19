using McpBridge;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace McpBridge.Tools;

[McpToolGroup("Debug")]
public class DebugTools
{
	[McpTool("sbox_debug_inspect", "Returns ALL properties and fields of a GameObject or Component by GUID and optional component index.", OptionalParams = new[]{"componentIndex"})]
	public object DebugInspect( string guidStr, int componentIndex = -1 )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid GUID" };
		var go = scene.Directory.FindByGuid( guid );
		if ( !go.IsValid() ) return new { error = "GameObject not found" };

		if ( componentIndex >= 0 )
		{
			var allComps = go.Components.GetAll<Component>().ToList();
			if ( componentIndex >= allComps.Count ) return new { error = $"Component index {componentIndex} out of range (0-{allComps.Count - 1})" };
			return DumpComponent( allComps[componentIndex], go );
		}

		var result = new Dictionary<string, object>
		{
			["name"] = go.Name,
			["id"] = go.Id.ToString(),
			["position"] = new { x = go.WorldPosition.x, y = go.WorldPosition.y, z = go.WorldPosition.z },
			["rotation"] = new { pitch = go.WorldRotation.Pitch(), yaw = go.WorldRotation.Yaw(), roll = go.WorldRotation.Roll() },
			["scale"] = go.LocalScale,
			["active"] = go.IsValid(),
			["parent"] = go.Parent?.Name ?? "null",
			["children"] = go.Children.Select( c => c.Name ).ToList(),
			["tags"] = go.Tags?.ToList() ?? new List<string>(),
			["componentCount"] = go.Components.GetAll<Component>().Count()
		};

		var comps = new List<object>();
		foreach ( var c in go.Components.GetAll<Component>() )
		{
			try { comps.Add( DumpComponent( c, go ) ); } catch { comps.Add( new { type = c.GetType().Name, error = "Failed to read" } ); }
		}
		result["components"] = comps;
		return result;
	}

	private object DumpComponent( Component comp, GameObject go )
	{
		var td = TypeLibrary.GetType( comp.GetType() );
		if ( td == null ) return new { type = comp.GetType().Name, error = "No TypeDescription" };

		var props = new Dictionary<string, object>();
		foreach ( var p in td.Properties.Where( p => p.CanRead ) )
		{
			try
			{
				var val = p.GetValue( comp );
				if ( val != null )
				{
					if ( val is Vector3 v3 ) props[p.Name] = new { x = v3.x, y = v3.y, z = v3.z };
					else if ( val is Color col ) props[p.Name] = new { r = col.r, g = col.g, b = col.b, a = col.a };
					else if ( val is Angles ang ) props[p.Name] = new { pitch = ang.pitch, yaw = ang.yaw, roll = ang.roll };
					else if ( val is Enum ) props[p.Name] = val.ToString();
					else if ( val.GetType().IsValueType || val is string || val is int || val is float || val is bool || val is double )
						props[p.Name] = val;
				}
			}
			catch { props[p.Name] = "<error reading>"; }
		}
		return new { type = comp.GetType().Name, enabled = comp.Enabled, properties = props };
	}

	[McpTool("sbox_debug_stack", "Returns current call stack info.")]
	public object DebugStack()
	{
		return new { available = false, note = "Stack trace not available on game thread (s&box sandbox restriction)" };
	}

	[McpTool("sbox_debug_watch", "Watch a GameObject by GUID for N seconds. Uses GameTask.Delay to avoid blocking.", OptionalParams = new[]{"durationSeconds", "intervalMs"})]
	public async Task<object> DebugWatch( string guidStr, int durationSeconds = 5, int intervalMs = 1000 )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid GUID" };
		var go = scene.Directory.FindByGuid( guid );
		if ( !go.IsValid() ) return new { error = "GameObject not found" };

		var snapshots = new List<object>();
		var iterations = Math.Max( 1, (durationSeconds * 1000) / intervalMs );
		for ( int i = 0; i < iterations; i++ )
		{
			if ( !go.IsValid() ) { snapshots.Add( new { iteration = i, error = "GameObject destroyed" } ); break; }
			await GameTask.Delay( intervalMs );
			snapshots.Add( new { iteration = i, position = new { x = go.WorldPosition.x, y = go.WorldPosition.y, z = go.WorldPosition.z }, rotation = new { pitch = go.WorldRotation.Pitch(), yaw = go.WorldRotation.Yaw(), roll = go.WorldRotation.Roll() }, scale = go.LocalScale } );
		}
		return new { guid = guidStr, name = go.IsValid() ? go.Name : "destroyed", snapshots };
	}

	[McpTool("sbox_debug_list_components", "Lists all component types available in the game.", OptionalParams = new[]{"query"})]
	public object DebugListComponents( string query = "" )
	{
		var types = TypeLibrary.GetTypes<Component>();
		var result = types
			.Where( t => string.IsNullOrEmpty( query ) || t.Name.IndexOf( query, StringComparison.OrdinalIgnoreCase ) >= 0 )
			.Take( 100 )
			.Select( t => new { name = t.Name, fullName = t.FullName, isAbstract = t.IsAbstract } )
			.ToList();
		return new { total = types.Count(), shown = result.Count, components = result };
	}

	[McpTool("sbox_debug_type_info", "Lists all methods, properties, and fields of a TypeLibrary type. Use for API discovery.")]
	public object DebugTypeInfo( string typeName )
	{
		var td = TypeLibrary.GetType( typeName );
		if ( td == null )
		{
			var candidates = TypeLibrary.GetTypes<object>()
				.Where( t => t.Name.IndexOf( typeName, StringComparison.OrdinalIgnoreCase ) >= 0
					|| t.FullName.IndexOf( typeName, StringComparison.OrdinalIgnoreCase ) >= 0 )
				.Take( 10 )
				.Select( t => t.FullName )
				.ToList();
			return new { found = false, error = $"Type '{typeName}' not found", suggestions = candidates };
		}

		var methods = td.Methods.Select( m => new
		{
			name = m.Name,
			parameters = m.Parameters.Select( p => new { name = p.Name, type = p.ParameterType.Name } ).ToList(),
			returnType = m.ReturnType?.Name ?? "void"
		} ).ToList();

		var properties = td.Properties.Select( p => new
		{
			name = p.Name,
			type = p.PropertyType?.Name ?? "?",
			canRead = p.CanRead,
			canWrite = p.CanWrite
		} ).ToList();

		return new
		{
			found = true,
			fullName = td.FullName,
			name = td.Name,
			isAbstract = td.IsAbstract,
			isStatic = td.IsStatic,
			methodCount = methods.Count,
			propertyCount = properties.Count,
			methods,
			properties
		};
	}

	[McpTool("sbox_debug_search_types", "Search for registered types by name pattern. Useful for finding correct API types.", OptionalParams = new[]{"query", "category"})]
	public object DebugSearchTypes( string query = "", string category = "" )
	{
		if ( string.IsNullOrEmpty( query ) && string.IsNullOrEmpty( category ) )
			return new { error = "Provide a query or category" };

		var types = string.IsNullOrEmpty( category )
			? TypeLibrary.GetTypes<object>()
			: category.ToLower() switch
			{
				"component" => TypeLibrary.GetTypes<Component>().Cast<TypeDescription>(),
				"model" => TypeLibrary.GetTypes<Model>().Cast<TypeDescription>(),
				"go" or "gameobject" => TypeLibrary.GetTypes<GameObject>().Cast<TypeDescription>(),
				_ => TypeLibrary.GetTypes<object>()
			};

		var results = types
			.Where( t => string.IsNullOrEmpty( query )
				|| t.Name.IndexOf( query, StringComparison.OrdinalIgnoreCase ) >= 0
				|| t.FullName.IndexOf( query, StringComparison.OrdinalIgnoreCase ) >= 0 )
			.Take( 50 )
			.Select( t => new { name = t.Name, fullName = t.FullName, ns = t.Namespace, isAbstract = t.IsAbstract } )
			.OrderBy( t => t.name )
			.ToList();

		return new { query, category, total = results.Count, types = results };
	}
}
