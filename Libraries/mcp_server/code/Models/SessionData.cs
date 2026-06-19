using System;

namespace McpBridge;

public class SessionData
{
	public string Id { get; set; }
	public int ToolCallCount { get; set; }
	public string LastMethod { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
