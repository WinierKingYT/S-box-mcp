using McpBridge;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge.Tools;

[McpToolGroup("Server")]
public class ServerTools
{
	[McpTool("sbox_list_files", "Lists files in the game's data directory.", OptionalParams = new[]{"path"}, ReadOnlyHint = true)]
	public object ListFiles( string path = "" )
	{
		var dir = FileSystem.Data;
		if ( dir == null ) return new { error = "Data filesystem unavailable" };
		var files = new List<string>();
		try { foreach ( var f in dir.FindFile( path, "*.json", true ) ) files.Add( f.Replace( '\\', '/' ) ); } catch { }
		try { foreach ( var f in dir.FindFile( path, "*.txt", true ) ) files.Add( f.Replace( '\\', '/' ) ); } catch { }
		try { foreach ( var f in dir.FindFile( path, "*.xml", true ) ) files.Add( f.Replace( '\\', '/' ) ); } catch { }
		return new { files, count = files.Count };
	}

	[McpTool("sbox_read_file", "Reads file contents from the data directory.", ReadOnlyHint = true)]
	public object ReadFile( string filePath )
	{
		var dir = FileSystem.Data;
		if ( dir == null ) return new { error = "Data filesystem unavailable" };
		if ( !dir.FileExists( filePath ) ) return new { error = $"File not found: {filePath}" };
		var content = dir.ReadAllText( filePath ); return new { success = true, path = filePath, content };
	}

	[McpTool("sbox_write_file", "Writes content to a file in the data directory. Only .json, .txt, .xml allowed.", DestructiveHint = true)]
	public object WriteFile( string filePath, string content )
	{
		var dir = FileSystem.Data;
		if ( dir == null ) return new { error = "Data filesystem unavailable" };
		var dot = filePath.LastIndexOf( '.' ); var ext = dot >= 0 ? filePath.Substring( dot ).ToLower() : "";
		var allowed = new[] { ".json", ".txt", ".xml" };
		if ( !allowed.Contains( ext ) )
			return new { error = $"Extension '{ext}' not allowed. Allowed: {string.Join( ", ", allowed )}" };
		dir.WriteAllText( filePath, content ); return new { success = true, path = filePath };
	}

	[McpTool("sbox_delete_file", "Deletes a file from the data directory.", DestructiveHint = true)]
	public object DeleteFile( string filePath )
	{
		var dir = FileSystem.Data;
		if ( dir == null ) return new { error = "Data filesystem unavailable" };
		if ( !dir.FileExists( filePath ) ) return new { error = $"File not found: {filePath}" };
		dir.DeleteFile( filePath ); return new { success = true, path = filePath };
	}

	[McpTool("sbox_list_logs", "Retrieves recent engine log entries.", OptionalParams = new[]{"count"}, ReadOnlyHint = true)]
	public object ListLogs( string count = "50" )
	{
		int.TryParse( count, out var max ); if ( max < 1 ) max = 50;
		var logs = McpLogBridge.GetRecent( max ); return new { logs, count = logs.Count };
	}

}
