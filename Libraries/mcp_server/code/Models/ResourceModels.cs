using System.Collections.Generic;

namespace McpBridge;

public class ResourceInfo
{
	public string Uri { get; set; }
	public string Name { get; set; }
	public string Description { get; set; }
	public string MimeType { get; set; } = "application/json";
}

public class PromptInfo
{
	public string Name { get; set; }
	public string Description { get; set; }
	public List<PromptArgument> Arguments { get; set; } = new();
}

public class PromptArgument
{
	public string Name { get; set; }
	public string Description { get; set; }
	public bool Required { get; set; }
}
