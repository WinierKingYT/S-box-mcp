using Xunit;

namespace Editor;

public class McpProtocolVersionPolicyTests
{
	[Theory]
	[InlineData( null, true )]
	[InlineData( "", true )]
	[InlineData( "2025-11-25", true )]
	[InlineData( "2099-01-01", false )]
	public void IsAcceptedHeader_HandlesLegacySupportedAndFutureVersions(
		string version,
		bool expected )
	{
		Assert.Equal(
			expected,
			McpProtocolVersionPolicy.IsAcceptedHeader( version ) );
	}
}
