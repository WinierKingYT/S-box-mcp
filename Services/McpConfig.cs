using Sandbox;
using System;
using System.Collections.Generic;

namespace McpBridge;

public class McpConfig
{
	public string ApiKey { get; set; } = "sbox-ai-2026";
	public int Port { get; set; } = 29016;
	public int CallbackPort { get; set; } = 29018;
	public int TimeoutSeconds { get; set; } = 10;
	public bool UseHttps { get; set; } = false;
	public string CertPath { get; set; } = "";
	public string CertPassword { get; set; } = "";
	public bool AutoStartScheduler { get; set; } = true;
	public int CleanupDays { get; set; } = 7;
	public List<string> AutoRunMacros { get; set; } = new();

	private static McpConfig _cached;
	private static readonly object _lock = new();
	private static readonly string ConfigPath = "mcp_config.json";

	public static McpConfig Load()
	{
		if ( _cached != null ) return _cached;
		lock ( _lock )
		{
			if ( _cached != null ) return _cached;

			try
			{
				_cached = PersistenceStore.Load<McpConfig>( ConfigPath ) ?? new McpConfig();
				_cached.Validate();
			}
			catch ( Exception e )
			{
				Log.Warning( $"[MCP] Config load failed: {e.Message}" );
				_cached = new McpConfig();
			}

			return _cached;
		}
	}

	public static void Save( McpConfig config )
	{
		lock ( _lock )
		{
			try
			{
				config.Validate();
				PersistenceStore.Save( ConfigPath, config );
				_cached = config;
			}
			catch ( Exception e )
			{
				Log.Warning( $"[MCP] Config save failed: {e.Message}" );
			}
		}
	}

	public void Validate()
	{
		if ( Port < 1 || Port > 65535 )
		{
			Log.Warning( $"[MCP] Config Port {Port} invalid, resetting to 29016" );
			Port = 29016;
		}
		if ( CallbackPort < 1 || CallbackPort > 65535 )
		{
			Log.Warning( $"[MCP] Config CallbackPort {CallbackPort} invalid, resetting to 29018" );
			CallbackPort = 29018;
		}
		if ( TimeoutSeconds < 1 || TimeoutSeconds > 300 )
		{
			Log.Warning( $"[MCP] Config TimeoutSeconds {TimeoutSeconds} invalid, resetting to 10" );
			TimeoutSeconds = 10;
		}
		if ( CleanupDays < 0 || CleanupDays > 365 )
		{
			Log.Warning( $"[MCP] Config CleanupDays {CleanupDays} invalid, resetting to 7" );
			CleanupDays = 7;
		}
	}
}
