using Sandbox;
using System;
using System.Linq;
using System.Text.Json;

namespace Editor;

public static class McpServerStarter
{
	[Event( "editor.created" )]
	public static void OnEditorCreated()
	{
		StartServer();
	}

	[Menu( "Editor", "MCP Server/Start Server", "Start the MCP server on port 29016" )]
	public static void StartServer()
	{
		McpEditorServer.Start();

		McpEditorServer.RegisterTool( "get_game_state", "Get current day/phase/time", _ =>
		{
			var scene = Game.ActiveScene;
			if ( scene == null ) return "No active scene";
			var gm = scene.GetAllComponents<BlackFridayGameManager>().FirstOrDefault();
			if ( gm == null ) return "No game manager found";
			return new { day = gm.CurrentDay, phase = gm.CurrentPhase.ToString(), timeLeft = gm.PhaseTimeRemaining };
		} );
	}

	[Menu( "Editor", "MCP Server/Stop Server" )]
	public static void StopServer()
	{
		McpEditorServer.Stop();
	}
}
