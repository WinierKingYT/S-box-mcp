using System.Collections.Generic;
using System.Linq;

namespace Editor;

internal static class BuiltInToolCatalog
{
	public static void RegisterAll(
		McpToolRegistry registry,
		System.Func<McpConfig> configProvider,
		IEditorScreenshotCapture screenshotCapture )
		=> RegisterAll(
			registry,
			configProvider,
			screenshotCapture,
			new SandboxMcpSceneUndoService() );

	internal static void RegisterAll(
		McpToolRegistry registry,
		System.Func<McpConfig> configProvider,
		IEditorScreenshotCapture screenshotCapture,
		IMcpSceneUndoService undoService )
	{
		registry.Register( "list_tools", "List all available tools", _ =>
			registry.Snapshot().Select( tool => new
			{
				name = tool.Name,
				description = tool.Description,
				inputSchema = tool.InputSchema,
				impact = tool.Impact.ToString(),
				supportsBatch = tool.SupportsBatch
			} ).ToList(),
			new
			{
				type = "object",
				properties = new Dictionary<string, object>()
			},
			McpToolImpact.ReadOnly );

		SceneQueryTools.Register( registry );
		SceneMutationTools.Register( registry, undoService );
		SceneAdvancedMutationTools.Register( registry, undoService );
		SceneUndoTools.Register( registry, undoService );
		EditorUtilityTools.Register( registry, configProvider, screenshotCapture );
	}
}
