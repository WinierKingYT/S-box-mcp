using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Editor;

/// <summary>
/// MCP Server configuration loaded from mcp_config.json
/// </summary>
public class McpConfig
{
	public const int CurrentConfigVersion = 2;

	private static McpConfig _instance;
	private static readonly object _lock = new();

	public static McpConfig Current
	{
		get
		{
			if ( _instance != null ) return _instance;
			lock ( _lock )
			{
				_instance ??= McpConfigLoader.Load();
			}
			return _instance;
		}
	}

	[JsonPropertyName( "ApiKey" )]
	public string ApiKey { get; set; } = "";

	[JsonPropertyName( "ConfigVersion" )]
	public int ConfigVersion { get; set; } = CurrentConfigVersion;

	[JsonPropertyName( "ApprovalMode" )]
	public string ApprovalMode { get; set; } = "AllMutations";

	[JsonPropertyName( "ApprovalTimeoutSeconds" )]
	public int ApprovalTimeoutSeconds { get; set; } = 600;

	[JsonPropertyName( "MaxPendingApprovals" )]
	public int MaxPendingApprovals { get; set; } = 50;

	[JsonPropertyName( "MaxBatchOperations" )]
	public int MaxBatchOperations { get; set; } = 50;

	[JsonPropertyName( "PersistHistory" )]
	public bool PersistHistory { get; set; } = true;

	[JsonPropertyName( "HistoryLimit" )]
	public int HistoryLimit { get; set; } = 500;

	[JsonPropertyName( "HistoryRetentionDays" )]
	public int HistoryRetentionDays { get; set; } = 7;

	[JsonIgnore]
	public bool IsCompatible { get; internal set; } = true;

	[JsonIgnore]
	public string CompatibilityMessage { get; internal set; } = "";

	[JsonPropertyName( "Port" )]
	public int Port { get; set; } = 29017;

	[JsonPropertyName( "MaxRequestsPerSecond" )]
	public int MaxRequestsPerSecond { get; set; } = 20;

	[JsonPropertyName( "RequestTimeoutMs" )]
	public int RequestTimeoutMs { get; set; } = 30000;

	[JsonPropertyName( "ScreenshotDirectory" )]
	public string ScreenshotDirectory { get; set; } = "scratch";

	[JsonPropertyName( "ScreenshotDirectoryBase" )]
	public string ScreenshotDirectoryBase { get; set; } = "Desktop";

	[JsonPropertyName( "AutoLoadAssemblies" )]
	public List<string> AutoLoadAssemblies { get; set; } = new();

	[JsonPropertyName( "EnableSseKeepAlive" )]
	public bool EnableSseKeepAlive { get; set; } = true;

	[JsonPropertyName( "SseKeepAliveIntervalMs" )]
	public int SseKeepAliveIntervalMs { get; set; } = 1000;

	[JsonPropertyName( "CorsOrigins" )]
	public List<string> CorsOrigins { get; set; } = new();

	[JsonPropertyName( "EnableConsoleCommands" )]
	public bool EnableConsoleCommands { get; set; } = false;

	/// <summary>
	/// Resolves the screenshot directory path using known base directories.
	/// </summary>
	public string GetScreenshotDirectory()
		=> McpPathResolver.GetScreenshotDirectory( this );

	/// <summary>
	/// Attempts to find an assembly by name in common s&amp;box output directories.
	/// </summary>
	public string ResolveAssemblyPath( string assemblyName )
		=> McpPathResolver.ResolveAssemblyPath( assemblyName );

	public static void Reload()
	{
		lock ( _lock )
		{
			_instance = McpConfigLoader.Load();
		}
	}
}
