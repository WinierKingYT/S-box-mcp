using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge;

public static class McpSnapshotManager
{
	private static readonly object _lock = new();
	private static readonly string StorageDir = "mcp_snapshots";

	static McpSnapshotManager()
	{
		PersistenceStore.EnsureDirectory( StorageDir );
	}

	public static string Save( string name, WorldSnapshot snapshot )
	{
		lock ( _lock )
		{
			try
			{
				var safeName = PersistenceStore.SafeFileName( name );
				var path = $"{StorageDir}/{safeName}.json";
				PersistenceStore.Save( path, snapshot );
				return path;
			}
			catch ( Exception e )
			{
				Log.Warning( $"[MCP] Snapshot save failed: {e.Message}" );
				return null;
			}
		}
	}

	public static WorldSnapshot Load( string name )
	{
		lock ( _lock )
		{
			try
			{
				var safeName = PersistenceStore.SafeFileName( name );
				var path = $"{StorageDir}/{safeName}.json";
				return PersistenceStore.Load<WorldSnapshot>( path );
			}
			catch ( Exception e )
			{
				Log.Warning( $"[MCP] Snapshot load failed: {e.Message}" );
				return null;
			}
		}
	}

	public static void Delete( string name )
	{
		lock ( _lock )
		{
			try
			{
				var safeName = PersistenceStore.SafeFileName( name );
				PersistenceStore.Delete( $"{StorageDir}/{safeName}.json" );
			}
			catch ( Exception e )
			{
				Log.Warning( $"[MCP] Snapshot delete failed: {e.Message}" );
			}
		}
	}

	public static List<string> List()
	{
		lock ( _lock )
		{
			try
			{
				if ( !FileSystem.Data.DirectoryExists( StorageDir ) )
					return new();

				return FileSystem.Data.FindFile( StorageDir, "*.json", false )
					.Select( f => f.Substring( 0, f.LastIndexOf( '.' ) ) )
					.ToList();
			}
			catch ( Exception e )
			{
				Log.Warning( $"[MCP] Snapshot list failed: {e.Message}" );
				return new();
			}
		}
	}
}
