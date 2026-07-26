using Xunit;

namespace Editor;

public class McpControlCenterViewModelTests
{
	[Fact]
	public void Build_OrdersNewestFirstAndSeparatesErrors()
	{
		var older = new McpActivityEntry(
			DateTimeOffset.UtcNow.AddMinutes( -1 ),
			"old",
			"succeeded",
			"",
			1,
			"Codex" );
		var newerError = new McpActivityEntry(
			DateTimeOffset.UtcNow,
			"new",
			"failed",
			"",
			2,
			"Codex",
			"boom" );

		var snapshot = McpControlCenterViewModel.Build(
			Array.Empty<McpApprovalPanelItem>(),
			new[] { older, newerError } );

		Assert.Equal( new[] { "new", "old" }, snapshot.History.Select( x => x.Tool ) );
		Assert.Equal( "new", Assert.Single( snapshot.Errors ).Tool );
	}
}
