using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Editor;

internal static class EditorUtilityTools
{
	public static void Register(
		McpToolRegistry registry,
		Func<McpConfig> configProvider,
		IEditorScreenshotCapture screenshotCapture )
	{
		registry.Register(
			"execute_console_command",
			"Execute a console command in S&box",
			arguments =>
			{
				if ( !configProvider().EnableConsoleCommands )
					return new
					{
						success = false,
						message = "Console commands are disabled by configuration"
					};

				var command = arguments.GetProperty( "command" ).GetString();
				ConsoleSystem.Run( command );
				return new
				{
					success = true,
					message = $"Command '{command}' executed"
				};
			},
			new
			{
				type = "object",
				properties = new Dictionary<string, object>
				{
					{ "command", new { type = "string", description = "The console command to run" } }
				},
				required = new[] { "command" }
			},
			McpToolImpact.HighRisk );

		registry.Register(
			"focus_camera_on_object",
			"Focuses the editor viewport camera onto a specific GameObject",
			FocusCamera,
			new
			{
				type = "object",
				properties = new Dictionary<string, object>
				{
					{ "id", new { type = "string", description = "GUID of the GameObject to focus on" } }
				},
				required = new[] { "id" }
			},
			McpToolImpact.EditorSideEffect );

		registry.Register(
			"capture_viewport_screenshot",
			"Capture a screenshot of the active s&box editor window",
			arguments =>
			{
				var requestedName = arguments.ValueKind == JsonValueKind.Object
					&& arguments.TryGetProperty( "filename", out var filenameProperty )
						? filenameProperty.GetString()
						: null;
				var path = ScreenshotPathPolicy.Resolve(
					configProvider().GetScreenshotDirectory(),
					requestedName );
				return new
				{
					message = screenshotCapture.Capture( path ),
					file_path = path
				};
			},
			new
			{
				type = "object",
				properties = new Dictionary<string, object>
				{
					{ "filename", new { type = "string", description = "Optional custom screenshot filename (e.g., custom_view.png)" } }
				}
			},
			McpToolImpact.EditorSideEffect );
	}

	private static object FocusCamera( JsonElement arguments )
	{
		var idText = arguments.GetProperty( "id" ).GetString();
		if ( !Guid.TryParse( idText, out var id ) )
			return new { success = false, message = "Invalid GUID format" };

		var gameObject = Game.ActiveScene?.GetAllObjects( true )
			.FirstOrDefault( candidate => candidate.Id == id );
		if ( gameObject == null )
			return new { success = false, message = "GameObject not found" };

		var viewport = SceneViewWidget.Current?.LastSelectedViewportWidget;
		if ( viewport == null )
			return new { success = false, message = "No active editor viewport found" };

		var bounds = new BBox(
			gameObject.WorldPosition - new Vector3( 100 ),
			gameObject.WorldPosition + new Vector3( 100 ) );
		viewport.GetType()
			.GetMethod( "FrameOn", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public )
			?.Invoke( viewport, new object[] { bounds } );
		viewport.GetType()
			.GetMethod( "UpdateCamera", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public )
			?.Invoke( viewport, null );
		return new
		{
			success = true,
			message = $"Focused camera on {gameObject.Name}"
		};
	}
}
