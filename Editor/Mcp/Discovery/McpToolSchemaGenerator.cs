using System;
using System.Collections.Generic;
using System.Reflection;

namespace Editor;

internal static class McpToolSchemaGenerator
{
	public static object Generate( MethodInfo method )
	{
		var properties = new Dictionary<string, object>();
		var required = new List<string>();
		foreach ( var parameter in method.GetParameters() )
		{
			properties[parameter.Name] = new
			{
				type = GetJsonType( parameter.ParameterType ),
				description = parameter.Name
			};
			if ( !parameter.HasDefaultValue )
				required.Add( parameter.Name );
		}

		return new { type = "object", properties, required };
	}

	private static string GetJsonType( Type type )
	{
		if ( type == typeof( int ) || type == typeof( float ) || type == typeof( double ) )
			return "number";
		if ( type == typeof( bool ) ) return "boolean";
		if ( type.IsClass && type != typeof( string ) ) return "object";
		return "string";
	}
}
