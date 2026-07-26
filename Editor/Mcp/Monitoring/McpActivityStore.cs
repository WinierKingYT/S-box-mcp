using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Editor;

internal sealed record McpActivityEntry(
	DateTimeOffset Timestamp,
	string Tool,
	string Status,
	string Summary,
	long DurationMs,
	string Client,
	string Error = null );

internal sealed class McpActivityStore
{
	private readonly object _sync = new();
	private readonly Func<McpConfig> _configProvider;
	private readonly List<McpActivityEntry> _entries = new();
	private readonly string _historyDirectory;

	public McpActivityStore(
		Func<McpConfig> configProvider,
		string historyDirectory = null )
	{
		_configProvider = configProvider;
		_historyDirectory = historyDirectory ?? GetDefaultHistoryDirectory();
		DeleteExpiredLogs();
		LoadRecentHistory();
	}

	public IReadOnlyList<McpActivityEntry> Snapshot()
	{
		lock ( _sync ) return _entries.ToArray();
	}

	public void Clear()
	{
		lock ( _sync )
		{
			_entries.Clear();
			try
			{
				if ( Directory.Exists( _historyDirectory ) )
				{
					foreach ( var path in Directory.EnumerateFiles(
						_historyDirectory,
						"activity-*.jsonl" ) )
						File.Delete( path );
				}
			}
			catch ( Exception exception )
			{
				Log.Warning( $"[MCP] Could not clear activity history: {exception.Message}" );
			}
		}
	}

	public void Add(
		string tool,
		string status,
		string summary,
		long durationMs = 0,
		Exception error = null )
	{
		var runtime = McpRuntimeState.Snapshot();
		var entry = new McpActivityEntry(
			DateTimeOffset.UtcNow,
			tool ?? "",
			status ?? "",
			Sanitize( summary ),
			durationMs,
			runtime.ClientName,
			Sanitize( error?.GetBaseException().Message ) );
		var config = _configProvider();
		lock ( _sync )
		{
			_entries.Add( entry );
			var excess = _entries.Count - config.HistoryLimit;
			if ( excess > 0 ) _entries.RemoveRange( 0, excess );
		}
		if ( config.PersistHistory ) Persist( entry );
	}

	internal string Sanitize( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) ) return value ?? "";
		var apiKey = _configProvider().ApiKey;
		return string.IsNullOrWhiteSpace( apiKey )
			? value
			: value.Replace( apiKey, "[REDACTED]", StringComparison.Ordinal );
	}

	private void Persist( McpActivityEntry entry )
	{
		try
		{
			Directory.CreateDirectory( _historyDirectory );
			var path = Path.Combine(
				_historyDirectory,
				$"activity-{DateTime.UtcNow:yyyy-MM-dd}.jsonl" );
			lock ( _sync )
			{
				File.AppendAllText(
					path,
					JsonSerializer.Serialize( entry ) + Environment.NewLine );
			}
		}
		catch ( Exception exception )
		{
			Log.Warning( $"[MCP] Could not persist activity history: {exception.Message}" );
		}
	}

	private void DeleteExpiredLogs()
	{
		try
		{
			if ( !Directory.Exists( _historyDirectory ) ) return;
			var cutoff = DateTime.UtcNow.AddDays( -_configProvider().HistoryRetentionDays );
			foreach ( var path in Directory.EnumerateFiles(
				_historyDirectory,
				"activity-*.jsonl" ) )
			{
				if ( File.GetLastWriteTimeUtc( path ) < cutoff ) File.Delete( path );
			}
		}
		catch
		{
			// Retention is best-effort and must not prevent server startup.
		}
	}

	private void LoadRecentHistory()
	{
		try
		{
			if ( !Directory.Exists( _historyDirectory ) ) return;
			var loaded = new List<McpActivityEntry>();
			foreach ( var path in Directory.EnumerateFiles(
				_historyDirectory,
				"activity-*.jsonl" ).OrderBy( path => path ) )
			{
				foreach ( var line in File.ReadLines( path ) )
				{
					if ( string.IsNullOrWhiteSpace( line ) ) continue;
					try
					{
						var entry = JsonSerializer.Deserialize<McpActivityEntry>( line );
						if ( entry != null ) loaded.Add( entry );
					}
					catch ( JsonException )
					{
						// Ignore an individual damaged record and continue loading.
					}
				}
			}
			var limit = _configProvider().HistoryLimit;
			lock ( _sync )
			{
				_entries.AddRange( loaded
					.OrderBy( entry => entry.Timestamp )
					.TakeLast( limit ) );
			}
		}
		catch ( Exception exception )
		{
			Log.Warning( $"[MCP] Could not load activity history: {exception.Message}" );
		}
	}

	private static string GetDefaultHistoryDirectory()
		=> Path.Combine(
			Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData ),
			"sbox-mcp",
			"history" );
}
