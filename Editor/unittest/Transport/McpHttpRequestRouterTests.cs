using Xunit;

namespace Editor;

public class McpHttpRequestRouterTests
{
	private static readonly McpConfig Config = new() { ApiKey = "secret" };

	[Theory]
	[InlineData( "GET", "/health", null, "Health" )]
	[InlineData( "POST", "/health", null, "Health" )]
	[InlineData( "POST", "/mcp", null, "Unauthorized" )]
	[InlineData( "POST", "/mcp", "wrong", "Unauthorized" )]
	[InlineData( "POST", "/mcp", "secret", "StreamableMessage" )]
	[InlineData( "GET", "/mcp", "secret", "StreamableMethodNotAllowed" )]
	[InlineData( "DELETE", "/mcp", "secret", "StreamableMethodNotAllowed" )]
	[InlineData( "GET", "/sse", "secret", "LegacySse" )]
	[InlineData( "POST", "/messages", "secret", "LegacyMessage" )]
	[InlineData( "GET", "/missing", "secret", "NotFound" )]
	public void Resolve_ReturnsExpectedRoute(
		string method,
		string path,
		string apiKey,
		string expected )
	{
		Assert.Equal(
			expected,
			McpHttpRequestRouter.Resolve( method, path, apiKey, Config ).ToString() );
	}
}
