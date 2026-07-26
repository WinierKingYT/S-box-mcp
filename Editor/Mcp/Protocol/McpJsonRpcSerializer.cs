using System.Text.Json;

namespace Editor;

internal static class McpJsonRpcSerializer
{
	public static string Success( object id, object result )
		=> JsonSerializer.Serialize( new { jsonrpc = "2.0", result, id } );

	public static string Error( object id, int code, string message )
		=> JsonSerializer.Serialize( new
		{
			jsonrpc = "2.0",
			error = new { code, message },
			id
		} );
}
