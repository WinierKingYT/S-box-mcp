using Xunit;

namespace Editor;

public class BuiltInToolCatalogTests
{
	[Fact]
	public void RegisterAll_PreservesBuiltInToolNames()
	{
		var registry = new McpToolRegistry();
		BuiltInToolCatalog.RegisterAll(
			registry,
			() => new McpConfig(),
			new FakeScreenshotCapture() );

		Assert.Equal(
			new[]
			{
				"add_component",
				"break_prefab",
				"capture_viewport_screenshot",
				"create_object",
				"delete_object",
				"duplicate_object",
				"execute_console_command",
				"focus_camera_on_object",
				"get_object_properties",
				"get_scene_hierarchy",
				"get_selection",
				"instantiate_prefab",
				"list_objects",
				"list_tools",
				"redo_scene_change",
				"remove_component",
				"set_parent",
				"undo_scene_change",
				"update_object_properties"
			},
			registry.Snapshot().Select( tool => tool.Name ) );
	}

	[Theory]
	[InlineData( "create_object" )]
	[InlineData( "delete_object" )]
	[InlineData( "get_object_properties" )]
	[InlineData( "update_object_properties" )]
	[InlineData( "focus_camera_on_object" )]
	[InlineData( "duplicate_object" )]
	[InlineData( "set_parent" )]
	[InlineData( "add_component" )]
	[InlineData( "remove_component" )]
	[InlineData( "instantiate_prefab" )]
	[InlineData( "break_prefab" )]
	public void RegisterAll_PreservesRequiredIdOrNameSchemas( string toolName )
	{
		var registry = new McpToolRegistry();
		BuiltInToolCatalog.RegisterAll(
			registry,
			() => new McpConfig(),
			new FakeScreenshotCapture() );

		Assert.True( registry.TryGet( toolName, out var definition ) );
		var json = System.Text.Json.JsonSerializer.Serialize( definition.InputSchema );
		Assert.Contains( "\"required\"", json, StringComparison.Ordinal );
	}

	private sealed class FakeScreenshotCapture : IEditorScreenshotCapture
	{
		public string Capture( string destinationPath )
			=> destinationPath;
	}

}
