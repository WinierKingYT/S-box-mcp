using Xunit;

namespace Editor;

[CollectionDefinition( "Environment variables", DisableParallelization = true )]
public sealed class EnvironmentVariableCollection
{
}

[Collection( "Environment variables" )]
public class McpConfigLoaderTests
{
	[Fact]
	public void LoadFromPaths_MergesLayersInOrder()
	{
		var directory = Path.Combine(
			Path.GetTempPath(),
			$"mcp-config-tests-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );
		try
		{
			var basePath = Path.Combine( directory, "base.json" );
			var userPath = Path.Combine( directory, "user.json" );
			File.WriteAllText(
				basePath,
				"""{"Port":29017,"RequestTimeoutMs":5000,"CorsOrigins":["https://base.test"]}""" );
			File.WriteAllText(
				userPath,
				"""{"Port":31000,"CorsOrigins":["https://user.test"]}""" );

			var config = McpConfigLoader.LoadFromPaths( new[] { basePath, userPath } );

			Assert.Equal( 31000, config.Port );
			Assert.Equal( 5000, config.RequestTimeoutMs );
			Assert.Equal( new[] { "https://user.test" }, config.CorsOrigins );
		}
		finally
		{
			Directory.Delete( directory, recursive: true );
		}
	}

	[Fact]
	public void LoadFromPaths_MergesPropertyNamesCaseInsensitively()
	{
		var path = Path.Combine(
			Path.GetTempPath(),
			$"mcp-config-{Guid.NewGuid():N}.json" );
		try
		{
			File.WriteAllText( path, """{"port":31001,"requesttimeoutms":7000}""" );

			var config = McpConfigLoader.LoadFromPaths( new[] { path } );

			Assert.Equal( 31001, config.Port );
			Assert.Equal( 7000, config.RequestTimeoutMs );
		}
		finally
		{
			File.Delete( path );
		}
	}

	[Fact]
	public void LoadFromPaths_SkipsMalformedLayerAndNormalizesValues()
	{
		var directory = Path.Combine(
			Path.GetTempPath(),
			$"mcp-config-tests-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );
		try
		{
			var malformedPath = Path.Combine( directory, "bad.json" );
			var validPath = Path.Combine( directory, "valid.json" );
			File.WriteAllText( malformedPath, "{" );
			File.WriteAllText(
				validPath,
				"""{"Port":70000,"MaxRequestsPerSecond":0,"RequestTimeoutMs":-1}""" );

			var config = McpConfigLoader.LoadFromPaths(
				new[] { malformedPath, validPath } );

			Assert.Equal( 29017, config.Port );
			Assert.Equal( 20, config.MaxRequestsPerSecond );
			Assert.Equal( 30000, config.RequestTimeoutMs );
		}
		finally
		{
			Directory.Delete( directory, recursive: true );
		}
	}

	[Fact]
	public void ApplyEnvironmentOverrides_ReplacesApiKeyAndValidPort()
	{
		var originalKey = Environment.GetEnvironmentVariable( "SBOX_MCP_API_KEY" );
		var originalPort = Environment.GetEnvironmentVariable( "SBOX_MCP_PORT" );
		try
		{
			Environment.SetEnvironmentVariable( "SBOX_MCP_API_KEY", "test-secret" );
			Environment.SetEnvironmentVariable( "SBOX_MCP_PORT", "31000" );
			var config = new McpConfig { ApiKey = "file-secret", Port = 29017 };

			McpConfigLoader.ApplyEnvironmentOverrides( config );

			Assert.Equal( "test-secret", config.ApiKey );
			Assert.Equal( 31000, config.Port );
		}
		finally
		{
			Environment.SetEnvironmentVariable( "SBOX_MCP_API_KEY", originalKey );
			Environment.SetEnvironmentVariable( "SBOX_MCP_PORT", originalPort );
		}
	}

	[Theory]
	[InlineData( "0" )]
	[InlineData( "65536" )]
	[InlineData( "not-a-number" )]
	public void ApplyEnvironmentOverrides_IgnoresInvalidPort( string value )
	{
		var originalPort = Environment.GetEnvironmentVariable( "SBOX_MCP_PORT" );
		try
		{
			Environment.SetEnvironmentVariable( "SBOX_MCP_PORT", value );
			var config = new McpConfig { Port = 29017 };

			McpConfigLoader.ApplyEnvironmentOverrides( config );

			Assert.Equal( 29017, config.Port );
		}
		finally
		{
			Environment.SetEnvironmentVariable( "SBOX_MCP_PORT", originalPort );
		}
	}

	[Fact]
	public void LoadFromPaths_MigratesV1InMemoryAndPreservesUnknownFields()
	{
		var path = Path.Combine(
			Path.GetTempPath(),
			$"mcp-config-{Guid.NewGuid():N}.json" );
		try
		{
			File.WriteAllText(
				path,
				"""{"Port":31002,"FutureExtension":{"Enabled":true}}""" );

			var config = McpConfigLoader.LoadFromPaths( new[] { path } );

			Assert.Equal( McpConfig.CurrentConfigVersion, config.ConfigVersion );
			Assert.Equal( 31002, config.Port );
			Assert.DoesNotContain( "ConfigVersion", File.ReadAllText( path ) );
		}
		finally
		{
			File.Delete( path );
		}
	}

	[Fact]
	public void LoadFromPaths_PersistsMigrationWithVersionedBackup()
	{
		var directory = Path.Combine(
			Path.GetTempPath(),
			$"mcp-config-tests-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );
		try
		{
			var path = Path.Combine( directory, "mcp_config.json" );
			File.WriteAllText( path, """{"Port":31003}""" );

			var config = McpConfigLoader.LoadFromPaths(
				new[] { path },
				persistMigrations: true );

			Assert.Equal( 2, config.ConfigVersion );
			Assert.Contains( "\"ConfigVersion\": 2", File.ReadAllText( path ) );
			Assert.Single( Directory.GetFiles(
				directory,
				"mcp_config.json.v1.backup-*" ) );
		}
		finally
		{
			Directory.Delete( directory, recursive: true );
		}
	}

	[Fact]
	public void LoadFromPaths_MarksFutureSchemaIncompatible()
	{
		var path = Path.Combine(
			Path.GetTempPath(),
			$"mcp-config-{Guid.NewGuid():N}.json" );
		try
		{
			File.WriteAllText( path, """{"ConfigVersion":99,"Port":31004}""" );

			var config = McpConfigLoader.LoadFromPaths( new[] { path } );

			Assert.False( config.IsCompatible );
			Assert.Contains( "newer", config.CompatibilityMessage );
		}
		finally
		{
			File.Delete( path );
		}
	}
}
