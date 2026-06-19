using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace McpBridge;

public static class Validation
{
	public static string ValidateArguments( JsonElement args, object inputSchema )
	{
		if ( inputSchema == null ) return null;

		JsonElement schemaEl;
		try
		{
			var schemaJson = JsonSerializer.Serialize( inputSchema );
			using var doc = JsonDocument.Parse( schemaJson );
			schemaEl = doc.RootElement.Clone();
		}
		catch
		{
			return null;
		}

		if ( schemaEl.ValueKind != JsonValueKind.Object ) return null;

		if ( schemaEl.TryGetProperty( "type", out var typeProp ) && typeProp.GetString() == "object" )
		{
			if ( args.ValueKind != JsonValueKind.Object && args.ValueKind != JsonValueKind.Undefined && args.ValueKind != JsonValueKind.Null )
			{
				return "Arguments must be a JSON object.";
			}
		}

		if ( schemaEl.TryGetProperty( "required", out var requiredProp ) && requiredProp.ValueKind == JsonValueKind.Array )
		{
			foreach ( var req in requiredProp.EnumerateArray() )
			{
				var reqName = req.GetString();
				if ( string.IsNullOrEmpty( reqName ) ) continue;

				if ( args.ValueKind != JsonValueKind.Object || !args.TryGetProperty( reqName, out var valProp ) || valProp.ValueKind == JsonValueKind.Null || valProp.ValueKind == JsonValueKind.Undefined )
				{
					return $"Missing required parameter: '{reqName}'";
				}
			}
		}

		if ( schemaEl.TryGetProperty( "properties", out var propertiesProp ) && propertiesProp.ValueKind == JsonValueKind.Object && args.ValueKind == JsonValueKind.Object )
		{
			foreach ( var prop in propertiesProp.EnumerateObject() )
			{
				var propName = prop.Name;
				var propSchema = prop.Value;

				if ( args.TryGetProperty( propName, out var argVal ) )
				{
					if ( argVal.ValueKind == JsonValueKind.Null || argVal.ValueKind == JsonValueKind.Undefined )
						continue;

					if ( propSchema.TryGetProperty( "type", out var expectedTypeProp ) )
					{
						var expectedType = expectedTypeProp.GetString();
						var validationError = ValidateType( argVal, expectedType, propName );
						if ( validationError != null )
						{
							return validationError;
						}
					}
				}
			}
		}

		return null;
	}

	private static string ValidateType( JsonElement val, string expectedType, string paramName )
	{
		switch ( expectedType )
		{
			case "string":
				if ( val.ValueKind != JsonValueKind.String )
					return $"Parameter '{paramName}' must be a string. Got {val.ValueKind}.";
				break;
			case "number":
				if ( val.ValueKind != JsonValueKind.Number )
				{
					if ( val.ValueKind == JsonValueKind.String && double.TryParse( val.GetString(), out _ ) )
						break;
					return $"Parameter '{paramName}' must be a number. Got {val.ValueKind}.";
				}
				break;
			case "boolean":
				if ( val.ValueKind != JsonValueKind.True && val.ValueKind != JsonValueKind.False )
				{
					if ( val.ValueKind == JsonValueKind.String && bool.TryParse( val.GetString(), out _ ) )
						break;
					return $"Parameter '{paramName}' must be a boolean. Got {val.ValueKind}.";
				}
				break;
			case "array":
				if ( val.ValueKind != JsonValueKind.Array )
					return $"Parameter '{paramName}' must be an array. Got {val.ValueKind}.";
				break;
			case "object":
				if ( val.ValueKind != JsonValueKind.Object )
					return $"Parameter '{paramName}' must be an object. Got {val.ValueKind}.";
				break;
		}
		return null;
	}

	public static Guid? ParseGuid( string guidStr )
	{
		if ( Guid.TryParse( guidStr, out var guid ) )
			return guid;
		return null;
	}

	public static object Error( string message ) => new { error = message };
	public static object Success( object data = null ) => data != null ? new { success = true, data } : new { success = true };

	public static object RequireScene( out Scene scene )
	{
		scene = Game.ActiveScene;
		if ( scene == null ) return Error( "No active scene" );
		return null;
	}

	public static object RequireGuid( string guidStr, out Guid guid )
	{
		if ( Guid.TryParse( guidStr, out guid ) )
			return null;
		return Error( $"Invalid GUID format: '{guidStr}'" );
	}

	public static object RequireObject( Scene scene, string guidStr, out GameObject go )
	{
		go = null;
		var guid = ParseGuid( guidStr );
		if ( guid == null ) return Error( $"Invalid GUID: '{guidStr}'" );
		go = scene.Directory.FindByGuid( guid.Value );
		if ( !go.IsValid() ) return Error( $"GameObject not found: '{guidStr}'" );
		return null;
	}

	public static object RequireValid( bool condition, string message )
	{
		return condition ? null : Error( message );
	}

	public static float Clamp( float val, float min, float max )
	{
		return Math.Max( min, Math.Min( max, val ) );
	}

	public static object RequireComponent<T>( GameObject go, out T comp ) where T : class
	{
		comp = go.Components.Get<T>();
		if ( comp == null ) return Error( $"Required component {typeof( T ).Name} not found on '{go.Name}'" );
		return null;
	}
}
