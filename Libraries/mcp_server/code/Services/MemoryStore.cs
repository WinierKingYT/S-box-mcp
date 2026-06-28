using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McpBridge;

public class MemoryEntry
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;
	public string Type { get; set; } // "pattern" or "antipattern"
	public string Path { get; set; }
	public string CodeSnippet { get; set; }
	public string Description { get; set; }
	public string Context { get; set; }
}

public static class MemoryStore
{
	private static readonly string MemoryFilePath = "memory.json";
	private static readonly List<MemoryEntry> _entries = new();
	private static readonly object _lock = new();

	static MemoryStore()
	{
		Load();
	}

	public static void Load()
	{
		lock ( _lock )
		{
			try
			{
				_entries.Clear();
				if ( File.Exists( MemoryFilePath ) )
				{
					var json = File.ReadAllText( MemoryFilePath );
					var list = JsonSerializer.Deserialize<List<MemoryEntry>>( json );
					if ( list != null )
					{
						_entries.AddRange( list );
					}
				}
			}
			catch ( Exception e )
			{
				Log.Warning( $"[MCP] Failed to load episodic memory: {e.Message}" );
			}
		}
	}

	public static void Save()
	{
		lock ( _lock )
		{
			try
			{
				var json = JsonSerializer.Serialize( _entries, new JsonSerializerOptions { WriteIndented = true } );
				var tempPath = MemoryFilePath + ".tmp";
				File.WriteAllText( tempPath, json );
				if ( File.Exists( MemoryFilePath ) )
				{
					File.Delete( MemoryFilePath );
				}
				File.Move( tempPath, MemoryFilePath );
			}
			catch ( Exception e )
			{
				Log.Warning( $"[MCP] Failed to save episodic memory: {e.Message}" );
			}
		}
	}

	public static void Add( string type, string path, string snippet, string description, string context )
	{
		lock ( _lock )
		{
			_entries.RemoveAll( e => e.Path == path && e.Context == context && e.Type == type );

			var entry = new MemoryEntry
			{
				Type = type,
				Path = path,
				CodeSnippet = snippet,
				Description = description,
				Context = context
			};
			_entries.Add( entry );
			if ( _entries.Count > 500 )
			{
				_entries.RemoveAt( 0 );
			}
			Save();
		}
	}

	public static List<MemoryEntry> Search( string query, string type = null, int maxResults = 10 )
	{
		lock ( _lock )
		{
			var filtered = _entries.AsEnumerable();
			if ( !string.IsNullOrEmpty( type ) )
			{
				filtered = filtered.Where( e => e.Type.Equals( type, StringComparison.OrdinalIgnoreCase ) );
			}

			if ( string.IsNullOrEmpty( query ) )
			{
				return filtered.OrderByDescending( e => e.Timestamp ).Take( maxResults ).ToList();
			}

			var queryTerms = query.ToLowerInvariant()
				.Split( new[] { ' ', ',', '.', '_', '(', ')', '{', '}' }, StringSplitOptions.RemoveEmptyEntries )
				.ToHashSet();

			var scored = filtered.Select( e =>
			{
				double score = 0;
				var textToSearch = $"{e.Path} {e.Context} {e.Description} {e.CodeSnippet}".ToLowerInvariant();
				
				foreach ( var term in queryTerms )
				{
					if ( textToSearch.Contains( term ) )
					{
						score += 1.0;
						if ( (e.Path ?? "").ToLowerInvariant().Contains( term ) ) score += 2.0;
						if ( (e.Context ?? "").ToLowerInvariant().Contains( term ) ) score += 2.0;
					}
				}
				return new { Entry = e, Score = score };
			} )
			.Where( x => x.Score > 0 )
			.OrderByDescending( x => x.Score )
			.ThenByDescending( x => x.Entry.Timestamp )
			.Select( x => x.Entry )
			.Take( maxResults )
			.ToList();

			return scored;
		}
	}

	public static void Clear()
	{
		lock ( _lock )
		{
			_entries.Clear();
			if ( File.Exists( MemoryFilePath ) )
			{
				try { File.Delete( MemoryFilePath ); } catch { }
			}
		}
	}

	public static void Remove( string id )
	{
		lock ( _lock )
		{
			_entries.RemoveAll( e => e.Id == id );
			Save();
		}
	}
}
