using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpBridge.Extensions;

public static class JsonRpcExtensions
{
	public static readonly JsonSerializerOptions SerializerOpts = new()
	{
		PropertyNameCaseInsensitive = true,
		NumberHandling = JsonNumberHandling.AllowReadingFromString,
		Converters = { new JsonStringEnumConverter() }
	};

	public static string ToOk( this int? id, string result )
	{
		var idVal = id.HasValue ? id.Value.ToString() : "null";
		return $"{{\"jsonrpc\":\"2.0\",\"result\":{result},\"id\":{idVal}}}";
	}

	public static string ToError( this int? id, int code, string message )
		=> JsonSerializer.Serialize( new { jsonrpc = "2.0", error = new { code, message }, id } );

	public static string ParseError( this int? id ) => id.ToError( -32700, "Parse error" );
	public static string MethodNotFound( this int? id, string method ) => id.ToError( -32601, $"Method '{method}' not found" );
	public static string InternalError( this int? id, string msg ) => id.ToError( -32603, msg );
	public static string RateLimited( this int? id ) => id.ToError( -32001, "Rate limited" );
	public static string NoScene( this int? id ) => id.ToError( -32000, "Active scene not found" );
	public static string Timeout( this int? id, string tool ) => id.ToError( -32002, $"Tool '{tool}' timed out" );
}
