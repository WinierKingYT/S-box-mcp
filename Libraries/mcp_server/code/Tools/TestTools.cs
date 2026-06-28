using McpBridge.Testing;
using Sandbox;
using System.Linq;

namespace McpBridge.Tools;

[McpToolGroup( "Testing" )]
public static class TestTools
{
	[McpTool( "sbox_run_tests", "Run in-game tests" )]
	public static object RunTests()
	{
		var runner = new McpTestRunner();
		var results = runner.RunAll();

		bool allPassed = results.All( r => r.Status == "PASS" );
		var report = string.Join( "\n", results.Select( r => $"{r.Name}: {r.Status}" + (string.IsNullOrEmpty( r.Error ) ? "" : $" (Error: {r.Error})") ) );
		MemoryValidator.OnTestsFinished( allPassed, report );

		return results;
	}
}
