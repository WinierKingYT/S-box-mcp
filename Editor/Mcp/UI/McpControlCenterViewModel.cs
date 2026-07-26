using System.Collections.Generic;
using System.Linq;

namespace Editor;

internal sealed record McpControlCenterSnapshot(
	IReadOnlyList<McpApprovalPanelItem> Pending,
	IReadOnlyList<McpActivityEntry> History,
	IReadOnlyList<McpActivityEntry> Errors );

internal static class McpControlCenterViewModel
{
	public static McpControlCenterSnapshot Build(
		IEnumerable<McpApprovalPanelItem> pending,
		IEnumerable<McpActivityEntry> history )
	{
		var historyItems = (history ?? Enumerable.Empty<McpActivityEntry>())
			.TakeLast( 30 )
			.Reverse()
			.ToArray();
		return new(
			(pending ?? Enumerable.Empty<McpApprovalPanelItem>()).ToArray(),
			historyItems,
			historyItems.Where( entry =>
				!string.IsNullOrWhiteSpace( entry.Error ) ).ToArray() );
	}
}
