using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor;

internal static class McpConfigLoader
{
	public static McpConfig Load()
	{
		var config = LoadFromPaths( GetCandidatePaths(), persistMigrations: true );
		ApplyEnvironmentOverrides( config );
		Normalize( config );
		return config;
	}

	internal static McpConfig LoadFromPaths(
		IEnumerable<string> paths,
		bool persistMigrations = false )
	{
		var merged = JsonSerializer.SerializeToNode( new McpConfig() )!.AsObject();
		var isCompatible = true;
		var compatibilityMessage = "";
		foreach ( var path in paths )
		{
			try
			{
				var fullPath = Path.GetFullPath( path );
				if ( !File.Exists( fullPath ) ) continue;

				var layer = JsonNode.Parse( File.ReadAllText( fullPath ) ) as JsonObject;
				if ( layer == null )
					throw new JsonException( "Configuration root must be a JSON object" );
				try
				{
					layer = McpConfigMigration.Migrate(
						layer,
						fullPath,
						persistMigrations );
				}
				catch ( NotSupportedException exception )
				{
					isCompatible = false;
					compatibilityMessage = exception.Message;
					Log.Error( $"[MCP] {exception.Message}: {fullPath}" );
					continue;
				}
				Merge( merged, layer );
				Log.Info( $"[MCP] Config loaded from: {fullPath}" );
			}
			catch ( Exception exception )
			{
				Log.Warning( $"[MCP] Failed to load config '{path}': {exception.Message}" );
			}
		}

		var config = merged.Deserialize<McpConfig>(
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true } )
			?? new McpConfig();
		config.IsCompatible = isCompatible;
		config.CompatibilityMessage = compatibilityMessage;
		Normalize( config );
		return config;
	}

	internal static IReadOnlyList<string> GetCandidatePaths()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var paths = new List<string>();
		var projectRoot = Project.Current?.GetRootPath();
		if ( !string.IsNullOrWhiteSpace( projectRoot ) )
		{
			paths.Add( Path.Combine( projectRoot, "Editor", "mcp_config.json" ) );
			paths.Add( Path.Combine( projectRoot, "mcp_config.json" ) );
		}

		paths.Add( Path.Combine( currentDirectory, "Editor", "mcp_config.json" ) );
		paths.Add( Path.Combine( currentDirectory, "mcp_config.json" ) );
		paths.Add( Path.Combine( currentDirectory, "..", "mcp_config.json" ) );
		paths.Add( Path.Combine(
			Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData ),
			"sbox-mcp",
			"mcp_config.json" ) );
		return paths.Select( Path.GetFullPath )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToArray();
	}

	internal static void ApplyEnvironmentOverrides( McpConfig config )
	{
		var apiKey = Environment.GetEnvironmentVariable( "SBOX_MCP_API_KEY" );
		if ( !string.IsNullOrWhiteSpace( apiKey ) )
			config.ApiKey = apiKey;

		var port = Environment.GetEnvironmentVariable( "SBOX_MCP_PORT" );
		if ( int.TryParse( port, out var parsedPort ) && parsedPort is > 0 and <= 65535 )
			config.Port = parsedPort;
	}

	internal static void Normalize( McpConfig config )
	{
		if ( config.Port is <= 0 or > 65535 )
			config.Port = 29017;
		if ( config.MaxRequestsPerSecond <= 0 )
			config.MaxRequestsPerSecond = 20;
		if ( config.RequestTimeoutMs <= 0 )
			config.RequestTimeoutMs = 30000;
		if ( config.SseKeepAliveIntervalMs <= 0 )
			config.SseKeepAliveIntervalMs = 1000;
		if ( config.ApprovalTimeoutSeconds <= 0 )
			config.ApprovalTimeoutSeconds = 600;
		if ( config.MaxPendingApprovals <= 0 )
			config.MaxPendingApprovals = 50;
		if ( config.MaxBatchOperations <= 0 )
			config.MaxBatchOperations = 50;
		if ( config.HistoryLimit <= 0 )
			config.HistoryLimit = 500;
		if ( config.HistoryRetentionDays <= 0 )
			config.HistoryRetentionDays = 7;
		if ( config.ApprovalMode is not ("AllMutations" or "HighRiskOnly" or "Disabled") )
			config.ApprovalMode = "AllMutations";
		if ( string.IsNullOrWhiteSpace( config.ScreenshotDirectory ) )
			config.ScreenshotDirectory = "scratch";
		if ( string.IsNullOrWhiteSpace( config.ScreenshotDirectoryBase ) )
			config.ScreenshotDirectoryBase = "Desktop";

		config.AutoLoadAssemblies = (config.AutoLoadAssemblies ?? new())
			.Where( value => !string.IsNullOrWhiteSpace( value ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToList();
		config.CorsOrigins = (config.CorsOrigins ?? new())
			.Where( value => !string.IsNullOrWhiteSpace( value ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToList();
	}

	private static void Merge( JsonObject target, JsonObject layer )
	{
		foreach ( var property in layer )
		{
			var targetKey = target.Select( item => item.Key )
				.FirstOrDefault( key => string.Equals(
					key,
					property.Key,
					StringComparison.OrdinalIgnoreCase ) )
				?? property.Key;
			target[targetKey] = property.Value?.DeepClone();
		}
	}
}
