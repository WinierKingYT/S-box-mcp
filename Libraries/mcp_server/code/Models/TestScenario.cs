using System.Collections.Generic;

namespace McpBridge;

public class TestScenario
{
	public string Name { get; set; }
	public string Description { get; set; }
	public List<ScenarioStep> Steps { get; set; } = new();
}

public class ScenarioStep
{
	public string Type { get; set; }
	public string Method { get; set; }
	public string Params { get; set; }
	public double WaitSeconds { get; set; }
}
