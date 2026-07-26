using Sandbox;
using System;
using System.Text.Json;

namespace Editor;

/// <summary>
/// Backward-compatible public façade for the editor MCP server.
/// </summary>
public static class McpEditorServer
{
	private static readonly McpServerRuntime Runtime = new();

	[EditorEvent.Frame]
	public static void OnEditorUpdate()
	{
		Runtime.DrainMainThreadQueue();
		McpControlCenterPanel.RefreshOpenPanel();
	}

	public static void RegisterTool(
		string name,
		string description,
		Func<JsonElement, object> handler,
		object inputSchema = null,
		McpToolImpact impact = McpToolImpact.HighRisk )
	{
		Runtime.RegisterTool( name, description, handler, inputSchema, impact );
	}

	public static void Start()
	{
		Runtime.Start();
	}

	public static void Stop()
	{
		Runtime.Stop();
	}

	internal static McpApprovalService Approvals => Runtime.Approvals;
	internal static McpActivityStore Activity => Runtime.Activity;
	internal static bool IsRunning => Runtime.IsRunning;
	internal static int ActiveSseSessions => Runtime.ActiveSseSessions;
}
