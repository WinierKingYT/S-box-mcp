using Xunit;

namespace Editor;

public class McpHttpSecurityTests
{
	[Theory]
	[InlineData( "secret", "secret", true )]
	[InlineData( "secret", "wrong", false )]
	[InlineData( "", "secret", false )]
	[InlineData( "secret", "", false )]
	[InlineData( null, "secret", false )]
	public void IsAuthorized_ValidatesKeys(
		string supplied,
		string configured,
		bool expected )
	{
		Assert.Equal(
			expected,
			McpHttpSecurity.IsAuthorized( supplied, configured ) );
	}

	[Fact]
	public void IsOriginAllowed_AllowsNativeClients()
	{
		Assert.True( McpHttpSecurity.IsOriginAllowed(
			null,
			Array.Empty<string>() ) );
	}

	[Theory]
	[InlineData( "https://example.test", true )]
	[InlineData( "HTTPS://EXAMPLE.TEST", true )]
	[InlineData( "https://evil.test", false )]
	public void IsOriginAllowed_UsesConfiguredOrigins(
		string origin,
		bool expected )
	{
		Assert.Equal(
			expected,
			McpHttpSecurity.IsOriginAllowed(
				origin,
				new[] { "https://example.test" } ) );
	}

	[Fact]
	public void IsOriginAllowed_HonorsWildcard()
	{
		Assert.True( McpHttpSecurity.IsOriginAllowed(
			"https://anywhere.test",
			new[] { "*" } ) );
	}
}
