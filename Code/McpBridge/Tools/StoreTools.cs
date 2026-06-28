using Sandbox;

namespace McpBridge.Tools;

[McpToolGroup("Store")]
public class StoreTools
{
	[McpTool("sbox_game_summary", "Full game state: phase, day, economy, alarm, bots", ReadOnlyHint = true)]
	public object GameSummary()
	{
		return new { status = "Clean state - Coding in progress" };
	}
}
