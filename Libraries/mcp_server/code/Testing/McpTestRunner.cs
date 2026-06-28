using Sandbox;
using System;
using System.Collections.Generic;

namespace McpBridge.Testing;

public class McpTestRunner
{
	public class TestResult
	{
		public string Name { get; set; }
		public string Status { get; set; }
		public string Error { get; set; }
	}

	public List<TestResult> RunAll()
	{
		var results = new List<TestResult>();
		Run( results, "TestSpawningGameObjects", () => { var go = new GameObject(); go.Name = "MCP_Test_Object"; return go.IsValid(); } );
		Run( results, "TestRaycast", () => { var s = Game.ActiveScene; if ( s == null ) return false; var t = s.Trace.Ray( Vector3.Zero, Vector3.Up * 100 ).Run(); return t.Hit == false; } );
		Run( results, "TestComponentAddition", () => { var go = new GameObject(); var mc = go.Components.Create<ModelRenderer>(); return mc.IsValid(); } );
		Run( results, "TestPositionSetting", () => { var go = new GameObject(); go.WorldPosition = new Vector3( 100, 200, 300 ); return go.WorldPosition == new Vector3( 100, 200, 300 ); } );
		Run( results, "TestParenting", () => { var p = new GameObject(); p.Name = "Parent"; var c = new GameObject(); c.Parent = p; return c.Parent == p; } );
		Run( results, "TestDestroy", () => { var go = new GameObject(); go.Destroy(); return !go.IsValid(); } );
		Run( results, "TestGuid", () => { var go = new GameObject(); return go.Id != default; } );
		return results;
	}

	private void Run( List<TestResult> results, string name, Func<bool> test )
	{
		try
		{
			if ( test() )
				results.Add( new TestResult { Name = name, Status = "PASS" } );
			else
				results.Add( new TestResult { Name = name, Status = "FAIL" } );
		}
		catch ( Exception e )
		{
			results.Add( new TestResult { Name = name, Status = "FAIL", Error = e.ToString() } );
		}
	}
}
