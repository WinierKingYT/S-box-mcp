using System.Collections.Generic;

namespace Editor;

internal static class SceneUndoTools
{
	public static void Register(
		McpToolRegistry registry,
		IMcpSceneUndoService undoService )
	{
		registry.Register(
			"undo_scene_change",
			"Undo the latest scene change made through MCP without undoing newer manual edits",
			_ => Execute( undoService, redo: false ),
			EmptySchema(),
			McpToolImpact.EditorSideEffect );

		registry.Register(
			"redo_scene_change",
			"Redo the latest MCP scene change without redoing unrelated manual edits",
			_ => Execute( undoService, redo: true ),
			EmptySchema(),
			McpToolImpact.EditorSideEffect );
	}

	private static object Execute(
		IMcpSceneUndoService undoService,
		bool redo )
	{
		using var sceneScope = SceneEditorSession.Scope();
		return redo ? undoService.Redo() : undoService.Undo();
	}

	private static object EmptySchema()
		=> new
		{
			type = "object",
			properties = new Dictionary<string, object>(),
			additionalProperties = false
		};
}
