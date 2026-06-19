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

	public static string ToError( this int? id, int code, string message, object data = null )
	{
		if ( data != null )
			return JsonSerializer.Serialize( new { jsonrpc = "2.0", error = new { code, message, data }, id } );
		return JsonSerializer.Serialize( new { jsonrpc = "2.0", error = new { code, message }, id } );
	}

	public static string ParseError( this int? id ) => id.ToError( -32700, "Parse error" );
	public static string InvalidRequest( this int? id, string msg = null ) => id.ToError( -32600, msg ?? "Invalid request" );
	public static string MethodNotFound( this int? id, string method ) => id.ToError( -32601, $"Method not found: '{method}'" );
	public static string InvalidParams( this int? id, string msg ) => id.ToError( -32602, msg );
	public static string InternalError( this int? id, string msg ) => id.ToError( -32603, msg, new { retryable = false } );
	public static string RateLimited( this int? id ) => id.ToError( -32001, "Request cancelled: rate limited" );
	public static string ConnectionClosed( this int? id ) => id.ToError( -32000, "Connection closed" );
	public static string ToolError( this int? id, string tool, string message ) => id.ToError( -32002, $"Tool '{tool}' failed", new { tool, error = message } );
	public static string TimeoutError( this int? id, string tool ) => id.ToError( -32002, $"Tool '{tool}' timed out", new { tool } );
	public static string ResourceNotFound( this int? id, string uri ) => id.ToError( -32003, $"Resource not found: {uri}", new { uri } );
}
