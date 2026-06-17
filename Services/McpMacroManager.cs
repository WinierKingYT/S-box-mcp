using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge;

public static class McpMacroManager
{
	private static readonly Dictionary<string, MacroData> _macros = new();
	private static readonly object _lock = new();
	private static readonly string StorageDir = "mcp_macros";
	private static bool _loaded;

	private static void EnsureLoaded()
	{
		if ( _loaded ) return;
		lock ( _lock )
		{
			if ( _loaded ) return;
			_loaded = true;
		}

		PersistenceStore.EnsureDirectory( StorageDir );

		foreach ( var file in FileSystem.Data.FindFile( StorageDir, "*.json", false ) )
		{
			try
			{
				var path = $"{StorageDir}/{file}";
				var macro = PersistenceStore.Load<MacroData>( path );
				if ( macro != null && !string.IsNullOrEmpty( macro.Name ) )
				{
					lock ( _lock )
						_macros[macro.Name] = macro;
				}
			}
			catch ( Exception e )
			{
				Log.Warning( $"[MCP] Failed to load macro {file}: {e.Message}" );
			}
		}
	}

	public static MacroData Get( string name )
	{
		EnsureLoaded();
		lock ( _lock )
		{
			_macros.TryGetValue( name, out var macro );
			return macro;
		}
	}

	public static void Save( MacroData macro )
	{
		EnsureLoaded();
		lock ( _lock )
			_macros[macro.Name] = macro;

		try
		{
			PersistenceStore.EnsureDirectory( StorageDir );
			var safeName = PersistenceStore.SafeFileName( macro.Name );
			PersistenceStore.Save( $"{StorageDir}/{safeName}.json", macro );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[MCP] Failed to save macro: {e.Message}" );
		}
	}

	public static void Delete( string name )
	{
		EnsureLoaded();
		lock ( _lock )
			_macros.Remove( name );

		try
		{
			var safeName = PersistenceStore.SafeFileName( name );
			PersistenceStore.Delete( $"{StorageDir}/{safeName}.json" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[MCP] Failed to delete macro: {e.Message}" );
		}
	}

	public static List<MacroData> List()
	{
		EnsureLoaded();
		lock ( _lock )
			return _macros.Values.ToList();
	}
}
