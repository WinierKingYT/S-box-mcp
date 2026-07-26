using Xunit;

namespace Editor;

public class ScreenshotPathPolicyTests
{
	[Fact]
	public void Resolve_AddsPngExtension()
	{
		var directory = Path.Combine( Path.GetTempPath(), "mcp-path-tests" );
		var result = ScreenshotPathPolicy.Resolve( directory, "capture" );

		Assert.Equal(
			Path.Combine( Path.GetFullPath( directory ), "capture.png" ),
			result );
	}

	[Theory]
	[InlineData( "../outside.png" )]
	[InlineData( "..\\outside.png" )]
	[InlineData( "C:\\outside.png" )]
	public void Resolve_StripsDirectoryComponents( string requestedName )
	{
		var directory = Path.Combine( Path.GetTempPath(), "mcp-path-tests" );
		var result = ScreenshotPathPolicy.Resolve( directory, requestedName );
		var expectedDirectory = Path.GetFullPath( directory )
			.TrimEnd( Path.DirectorySeparatorChar )
			+ Path.DirectorySeparatorChar;

		Assert.StartsWith(
			expectedDirectory,
			result,
			StringComparison.OrdinalIgnoreCase );
		Assert.DoesNotContain( "..", Path.GetFileName( result ) );
	}

	[Fact]
	public void Resolve_PreservesPngExtensionCaseInsensitively()
	{
		var directory = Path.Combine( Path.GetTempPath(), "mcp-path-tests" );
		Assert.EndsWith(
			"capture.PNG",
			ScreenshotPathPolicy.Resolve( directory, "capture.PNG" ),
			StringComparison.Ordinal );
	}

	[Fact]
	public void Resolve_RejectsInvalidWindowsFilename()
	{
		var directory = Path.Combine( Path.GetTempPath(), "mcp-path-tests" );
		Assert.Throws<ArgumentException>(
			() => ScreenshotPathPolicy.Resolve( directory, "bad:name.png" ) );
	}

	[Theory]
	[InlineData( "CON.png" )]
	[InlineData( "nul" )]
	[InlineData( "COM1.PNG" )]
	[InlineData( "lpt9.png" )]
	public void Resolve_RejectsReservedWindowsFilename( string filename )
	{
		var directory = Path.Combine( Path.GetTempPath(), "mcp-path-tests" );
		Assert.Throws<ArgumentException>(
			() => ScreenshotPathPolicy.Resolve( directory, filename ) );
	}
}
