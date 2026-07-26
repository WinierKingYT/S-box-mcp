using System;
using System.IO;

namespace Editor;

internal static class ScreenshotPathPolicy
{
	public static string Resolve( string destinationDirectory, string requestedName )
	{
		var directory = Path.GetFullPath( destinationDirectory );
		var filename = string.IsNullOrWhiteSpace( requestedName )
			? $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png"
			: Path.GetFileName( requestedName );

		if ( string.IsNullOrWhiteSpace( filename )
			|| filename.IndexOfAny( Path.GetInvalidFileNameChars() ) >= 0 )
			throw new ArgumentException( "Invalid screenshot filename" );
		if ( !filename.EndsWith( ".png", StringComparison.OrdinalIgnoreCase ) )
			filename += ".png";
		var stem = Path.GetFileNameWithoutExtension( filename );
		if ( IsReservedWindowsName( stem ) )
			throw new ArgumentException( "Reserved Windows filename" );

		var path = Path.GetFullPath( Path.Combine( directory, filename ) );
		var prefix = directory.TrimEnd(
			Path.DirectorySeparatorChar,
			Path.AltDirectorySeparatorChar ) + Path.DirectorySeparatorChar;
		if ( !path.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidOperationException(
				"Screenshot path escapes the configured directory" );
		return path;
	}

	private static bool IsReservedWindowsName( string stem )
	{
		var normalized = stem.TrimEnd( ' ', '.' ).ToUpperInvariant();
		if ( normalized is "CON" or "PRN" or "AUX" or "NUL" )
			return true;
		if ( normalized.Length != 4 ) return false;
		return (normalized.StartsWith( "COM", StringComparison.Ordinal )
				|| normalized.StartsWith( "LPT", StringComparison.Ordinal ))
			&& normalized[3] is >= '1' and <= '9';
	}
}
