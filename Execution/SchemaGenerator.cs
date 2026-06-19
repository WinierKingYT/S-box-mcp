using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace McpBridge.Execution;

public class SchemaGenerator
{
	private object _cachedDefs;

	public object GetDefs( Dictionary<string, MethodDescription> tools )
	{
		if ( _cachedDefs != null ) return _cachedDefs;
		_cachedDefs = new { tools = tools.Select( t =>
		{
			var attr = t.Value.GetCustomAttribute<McpToolAttribute>();
			return new
			{
				name = t.Key,
				description = attr?.Description ?? "",
				inputSchema = Generate( t.Value )
			};
		} ).ToList() };
		return _cachedDefs;
	}

	public void Invalidate() => _cachedDefs = null;

	private static string DescribeParam( string name )
	{
		return name switch
		{
			"guidStr" or "guid" or "id" or "idStr" => "GUID of the target GameObject",
			"x" or "y" or "z" => "World position component",
			"cx" or "cy" or "cz" => "Center position component",
			"pitch" or "yaw" or "roll" => "Rotation component in degrees",
			"dx" or "dy" or "dz" => "Direction vector component",
			"tx" or "ty" or "tz" => "Target position component",
			"ox" or "oy" or "oz" => "Origin position component",
			"go" or "gameObject" => "Target GameObject",
			"prefabPath" => "Prefab file path (e.g. prefabs/example.prefab)",
			"scale" => "Uniform scale factor",
			"radius" => "Radius in world units",
			"magnitude" => "Magnitude/strength of the effect",
			"filter" or "query" => "Optional text filter",
			"command" => "Console command to execute",
			"name" => "Display name for the GameObject",
			"color" => "Color hex string (e.g. #ff0000)",
			"fileName" or "filePath" or "path" => "File path relative to project root",
			"content" or "code" => "File content to write",
			"text" => "Text content to display",
			"width" => "Width in pixels",
			"height" => "Height in pixels",
			_ => $"Parameter '{name}'"
		};
	}

	public static object Generate( MethodDescription method )
	{
		var attr = method.GetCustomAttribute<McpToolAttribute>();
		var optional = attr?.OptionalParams ?? System.Array.Empty<string>();
		var optionalSet = new System.Collections.Generic.HashSet<string>( optional );
		var props = new Dictionary<string, object>();
		var required = new List<string>();
		foreach ( var p in method.Parameters )
		{
			var pt = p.ParameterType;
			var typeStr = "string";
			if ( pt == typeof( int ) || pt == typeof( float ) || pt == typeof( double ) || pt == typeof( long ) || pt == typeof( decimal ) )
				typeStr = "number";
			else if ( pt == typeof( bool ) )
				typeStr = "boolean";
			props[p.Name] = new Dictionary<string, object>
			{
				{ "type", typeStr },
				{ "description", DescribeParam( p.Name ) }
			};
			if ( !optionalSet.Contains( p.Name ) )
				required.Add( p.Name );
		}
		return new { type = "object", properties = props, required = required.ToArray() };
	}
}
