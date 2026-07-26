using System;
using System.Text.Json;

namespace Editor;

public enum McpToolImpact
{
	ReadOnly,
	EditorSideEffect,
	SceneMutation,
	HighRisk
}

internal sealed record McpToolDefinition(
	string Name,
	string Description,
	Func<JsonElement, object> Handler,
	object InputSchema,
	McpToolImpact Impact,
	bool SupportsBatch );
