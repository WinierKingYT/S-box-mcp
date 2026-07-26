using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;

namespace Editor;

internal static class McpPathResolver
{
	public static string GetScreenshotDirectory( McpConfig config )
	{
		var baseDirectory = config.ScreenshotDirectoryBase?.ToLowerInvariant() switch
		{
			"desktop" => Environment.GetFolderPath( Environment.SpecialFolder.Desktop ),
			"documents" => Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ),
			"appdata" => Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData ),
			"localappdata" => Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ),
			"temp" => Path.GetTempPath(),
			_ => config.ScreenshotDirectoryBase
		};

		var path = Path.Combine( baseDirectory, config.ScreenshotDirectory );
		Directory.CreateDirectory( path );
		return path;
	}

	public static string ResolveAssemblyPath( string assemblyName )
	{
		if ( File.Exists( assemblyName ) )
			return Path.GetFullPath( assemblyName );

		foreach ( var candidate in GetAssemblyCandidates( assemblyName ) )
		{
			if ( File.Exists( candidate ) )
				return candidate;
		}

		return null;
	}

	private static IEnumerable<string> GetAssemblyCandidates( string assemblyName )
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		yield return Path.Combine( currentDirectory, assemblyName );
		yield return Path.Combine( currentDirectory, ".vs", "output", assemblyName );
		yield return Path.Combine( currentDirectory, "bin", "Debug", "net10.0", assemblyName );
		yield return Path.Combine( currentDirectory, "bin", "Release", "net10.0", assemblyName );

		var projectRoot = Project.Current?.GetRootPath();
		if ( !string.IsNullOrWhiteSpace( projectRoot ) )
		{
			yield return Path.Combine( projectRoot, assemblyName );
			yield return Path.Combine( projectRoot, "code", "bin", "Debug", "net10.0", assemblyName );
			yield return Path.Combine( projectRoot, "code", "bin", "Release", "net10.0", assemblyName );
		}

		var parent = Directory.GetParent( currentDirectory )?.FullName;
		if ( !string.IsNullOrEmpty( parent ) )
		{
			yield return Path.Combine( parent, "Code", "bin", "Debug", "net10.0", assemblyName );
			yield return Path.Combine( parent, "Code", "bin", "Release", "net10.0", assemblyName );
			yield return Path.Combine( parent, assemblyName );
		}

		var steamOutput = Path.Combine(
			Environment.GetFolderPath( Environment.SpecialFolder.ProgramFilesX86 ),
			"Steam",
			"steamapps",
			"common",
			"sbox",
			".vs",
			"output" );
		yield return Path.Combine( steamOutput, assemblyName );
	}
}
