using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor;

internal sealed record McpConfigMigrationResult(
	string Path,
	int FromVersion,
	int ToVersion,
	bool Persisted,
	string Message );

internal static class McpConfigMigration
{
	private static readonly object Sync = new();
	private static readonly List<McpConfigMigrationResult> Results = new();

	public static IReadOnlyList<McpConfigMigrationResult> Snapshot()
	{
		lock ( Sync ) return Results.ToArray();
	}

	public static JsonObject Migrate(
		JsonObject layer,
		string path,
		bool persist )
	{
		var version = ReadVersion( layer );
		if ( version > McpConfig.CurrentConfigVersion )
			throw new NotSupportedException(
				$"Config schema v{version} is newer than supported v{McpConfig.CurrentConfigVersion}" );
		if ( version == McpConfig.CurrentConfigVersion ) return layer;

		var originalVersion = version;
		while ( version < McpConfig.CurrentConfigVersion )
		{
			layer = version switch
			{
				1 => MigrateV1ToV2( layer ),
				_ => throw new NotSupportedException( $"No migration exists for config schema v{version}" )
			};
			version++;
		}

		var persisted = false;
		var message = "Migrated in memory";
		if ( persist )
		{
			try
			{
				PersistAtomically( path, layer, originalVersion );
				persisted = true;
				message = "Migrated and backed up";
			}
			catch ( Exception exception )
			{
				message = $"Migrated in memory; write failed: {exception.Message}";
			}
		}

		lock ( Sync )
		{
			Results.Add( new(
				path,
				originalVersion,
				McpConfig.CurrentConfigVersion,
				persisted,
				message ) );
		}
		return layer;
	}

	private static int ReadVersion( JsonObject layer )
	{
		foreach ( var property in layer )
		{
			if ( !string.Equals(
					property.Key,
					"ConfigVersion",
					StringComparison.OrdinalIgnoreCase ) )
				continue;
			if ( property.Value is JsonValue value
				&& value.TryGetValue<int>( out var version ) )
				return version;
			throw new JsonException( "ConfigVersion must be an integer" );
		}
		return 1;
	}

	private static JsonObject MigrateV1ToV2( JsonObject layer )
	{
		var migrated = (JsonObject)layer.DeepClone();
		migrated["ConfigVersion"] = 2;
		return migrated;
	}

	private static void PersistAtomically(
		string path,
		JsonObject layer,
		int originalVersion )
	{
		var fullPath = Path.GetFullPath( path );
		var directory = Path.GetDirectoryName( fullPath )
			?? throw new IOException( "Config directory could not be resolved" );
		var timestamp = DateTime.UtcNow.ToString( "yyyyMMddHHmmssfff" );
		var backupPath = $"{fullPath}.v{originalVersion}.backup-{timestamp}";
		var temporaryPath = Path.Combine(
			directory,
			$".{Path.GetFileName( fullPath )}.{Guid.NewGuid():N}.tmp" );
		try
		{
			File.WriteAllText(
				temporaryPath,
				layer.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) );
			File.Replace( temporaryPath, fullPath, backupPath );
		}
		finally
		{
			if ( File.Exists( temporaryPath ) ) File.Delete( temporaryPath );
		}
	}
}
