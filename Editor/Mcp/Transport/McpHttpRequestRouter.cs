namespace Editor;

internal enum McpHttpRoute
{
	Health,
	Unauthorized,
	StreamableMessage,
	StreamableMethodNotAllowed,
	LegacySse,
	LegacyMessage,
	NotFound
}

internal static class McpHttpRequestRouter
{
	public static McpHttpRoute Resolve(
		string method,
		string path,
		string suppliedApiKey,
		McpConfig config )
	{
		if ( path == "/health" )
			return McpHttpRoute.Health;
		if ( !McpHttpSecurity.IsAuthorized( suppliedApiKey, config.ApiKey ) )
			return McpHttpRoute.Unauthorized;
		if ( path == "/mcp" && method == "POST" )
			return McpHttpRoute.StreamableMessage;
		if ( path == "/mcp" )
			return McpHttpRoute.StreamableMethodNotAllowed;
		if ( path == "/sse" && method == "GET" )
			return McpHttpRoute.LegacySse;
		if ( path == "/messages" && method == "POST" )
			return McpHttpRoute.LegacyMessage;
		return McpHttpRoute.NotFound;
	}
}
