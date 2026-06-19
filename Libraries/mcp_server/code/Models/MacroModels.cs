using System.Collections.Generic;

namespace McpBridge;

public class MacroData
{
	public string Name { get; set; }
	public string Description { get; set; }
	public List<MacroStep> Steps { get; set; } = new();
}

public class MacroStep
{
	public string Method { get; set; }
	public string Params { get; set; }
}
