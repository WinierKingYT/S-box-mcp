using Sandbox;
using System;
using System.Text;
using System.Text.Json;

namespace McpBridge;

public static class PersistenceStore
{
	public static JsonSerializerOptions JsonOpts { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

	public static T Load<T>( string path ) where T : new()
	{
		try
		{
			if ( FileSystem.Data.FileExists( path ) )
			{
				var json = FileSystem.Data.ReadAllText( path );
				return JsonSerializer.Deserialize<T>( json, JsonOpts ) ?? new T();
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Persistence] Load failed '{path}': {e.Message}" );
		}
		return new T();
	}

	public static void Save<T>( string path, T data )
	{
		try
		{
			var json = JsonSerializer.Serialize( data, JsonOpts );
			FileSystem.Data.WriteAllText( path, json );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Persistence] Save failed '{path}': {e.Message}" );
		}
	}

	public static bool Exists( string path )
	{
		try { return FileSystem.Data.FileExists( path ); }
		catch { return false; }
	}

	public static void Delete( string path )
	{
		try { if ( FileSystem.Data.FileExists( path ) ) FileSystem.Data.DeleteFile( path ); }
		catch ( Exception e ) { Log.Warning( $"[Persistence] Delete failed '{path}': {e.Message}" ); }
	}

	public static void EnsureDirectory( string dir )
	{
		try { if ( !FileSystem.Data.DirectoryExists( dir ) ) FileSystem.Data.CreateDirectory( dir ); }
		catch ( Exception e ) { Log.Warning( $"[Persistence] Dir create failed '{dir}': {e.Message}" ); }
	}

	public static string SafeFileName( string name )
	{
		var safe = new StringBuilder();
		foreach ( var c in name )
		{
			if ( char.IsLetterOrDigit( c ) || c == '-' || c == '_' || c == '.' )
				safe.Append( c );
			else
				safe.Append( '_' );
		}
		return safe.ToString();
	}
}
