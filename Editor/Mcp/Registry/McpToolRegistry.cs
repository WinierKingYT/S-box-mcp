using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Editor;

internal sealed class McpToolRegistry
{
	private readonly ConcurrentDictionary<string, McpToolDefinition> _tools =
		new( StringComparer.Ordinal );

	public void Register(
		string name,
		string description,
		Func<JsonElement, object> handler,
		object inputSchema = null,
		McpToolImpact impact = McpToolImpact.HighRisk,
		bool supportsBatch = false )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		ArgumentNullException.ThrowIfNull( handler );

		var schema = inputSchema ?? new
		{
			type = "object",
			properties = new Dictionary<string, object>()
		};
		_tools[name] = new McpToolDefinition(
			name,
			description ?? "",
			handler,
			schema,
			impact,
			supportsBatch );
	}

	public bool TryGet( string name, out McpToolDefinition definition )
		=> _tools.TryGetValue( name, out definition );

	public IReadOnlyList<McpToolDefinition> Snapshot()
		=> _tools.Values.OrderBy( tool => tool.Name, StringComparer.Ordinal ).ToList();
}
