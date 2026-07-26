using Xunit;

namespace Editor;

public class McpSceneUndoServiceTests
{
	[Theory]
	[InlineData( "[MCP] Create Object", true )]
	[InlineData( "[MCP] Update Object", true )]
	[InlineData( "Create Object", false )]
	[InlineData( "", false )]
	[InlineData( null, false )]
	public void IsMcpAction_RecognizesOnlyPrefixedHistoryEntries(
		string actionName,
		bool expected )
	{
		Assert.Equal(
			expected,
			SandboxMcpSceneUndoService.IsMcpAction( actionName ) );
	}
}
