using McpBridge.Testing;
using Sandbox;

namespace McpBridge.Tools;

[McpToolGroup( "Testing" )]
public static class TestTools
{
	[McpTool( "sbox_run_tests", "Run in-game tests" )]
	public static object RunTests()
	{
		var runner = new McpTestRunner();
		return runner.RunAll();
	}
}
