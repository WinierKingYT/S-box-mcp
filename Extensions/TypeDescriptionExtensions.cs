using Sandbox;
using System;
using System.Linq;

namespace McpBridge.Extensions;

public static class TypeDescriptionExtensions
{
	public static bool TryGetProperty( this TypeDescription td, string name, out PropertyDescription prop )
	{
		prop = td.Properties.FirstOrDefault( p => p.Name == name );
		return prop != null;
	}

	public static bool IsNumeric( this TypeDescription td )
	{
		var t = td.TargetType;
		return t == typeof( int ) || t == typeof( float ) || t == typeof( double ) || t == typeof( long ) || t == typeof( decimal ) || t == typeof( short );
	}

	public static string GetSchemaType( this TypeDescription td )
	{
		var t = td.TargetType;
		if ( t == typeof( string ) ) return "string";
		if ( td.IsNumeric() ) return "number";
		if ( t == typeof( bool ) ) return "boolean";
		return "string";
	}
}
