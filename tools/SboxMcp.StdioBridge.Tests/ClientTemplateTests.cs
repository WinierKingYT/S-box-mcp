using System.Text.Json;
using Xunit;

namespace SboxMcp.StdioBridge.Tests;

public class ClientTemplateTests
{
	[Theory]
	[InlineData( "clients/cursor/mcp.json.example" )]
	[InlineData( "clients/windsurf/mcp_config.json.example" )]
	[InlineData( "clients/claude-desktop/claude_desktop_config.json.example" )]
	public void JsonTemplates_AreValidAndContainNoSecret( string relativePath )
	{
		var content = File.ReadAllText(
			Path.Combine( FindRepositoryRoot(), relativePath ) );

		using var document = JsonDocument.Parse( content );
		Assert.True(
			document.RootElement.GetProperty( "mcpServers" )
				.TryGetProperty( "sbox", out _ ) );
		Assert.DoesNotContain(
			"secret",
			content,
			StringComparison.OrdinalIgnoreCase );
		Assert.Contains( "localhost:29017", content, StringComparison.Ordinal );
	}

	[Fact]
	public void CodexTemplate_UsesEnvironmentBackedHeader()
	{
		var content = File.ReadAllText(
			Path.Combine(
				FindRepositoryRoot(),
				"clients/codex/config.toml.example" ) );

		Assert.Contains( "[mcp_servers.sbox]", content, StringComparison.Ordinal );
		Assert.Contains(
			"env_http_headers",
			content,
			StringComparison.Ordinal );
		Assert.Contains( "SBOX_MCP_API_KEY", content, StringComparison.Ordinal );
		Assert.DoesNotMatch( "[a-fA-F0-9]{64}", content );
	}

	private static string FindRepositoryRoot()
	{
		var candidates = new[]
		{
			Directory.GetCurrentDirectory(),
			AppContext.BaseDirectory
		};
		foreach ( var candidate in candidates )
		{
			var directory = new DirectoryInfo( candidate );
			while ( directory != null )
			{
				if ( File.Exists( Path.Combine( directory.FullName, "mcp_server.slnx" ) ) )
					return directory.FullName;
				directory = directory.Parent;
			}
		}

		throw new DirectoryNotFoundException( "Repository root was not found" );
	}
}
