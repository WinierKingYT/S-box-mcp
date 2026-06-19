using System;

namespace McpBridge;

public class ReplayRecord
{
	public string Method { get; set; }
	public string Input { get; set; }
	public string Output { get; set; }
	public long DurationMs { get; set; }
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;
	public bool Success { get; set; } = true;
}
