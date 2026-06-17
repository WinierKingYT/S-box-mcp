using System.Collections.Generic;

namespace McpBridge.Interfaces;

public interface IConfigStore
{
	int Port { get; }
	int TimeoutSeconds { get; }
	int CleanupDays { get; }
	int RateLimit { get; }
	int ToolTimeoutSeconds { get; }
	void Validate();
}
