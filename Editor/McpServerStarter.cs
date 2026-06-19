using Sandbox;
using System;

namespace Editor;

public static class McpServerStarter
{
	[Event( "editor.created" )]
	public static void OnEditorCreated()
	{
		try
		{
			StartServer();
			Log.Info( "[MCP] Editor server auto-started via editor.created" );
		}
		catch ( Exception e )
		{
			Log.Error( $"[MCP] Auto-start failed: {e.GetType().Name} {e.Message}" );
		}
	}

	[Menu( "Editor", "MCP Server/Start Server", "Start the MCP server on port 29016" )]
	public static void StartServer()
	{
		McpEditorServer.Start();
	}

	[Menu( "Editor", "MCP Server/Stop Server" )]
	public static void StopServer()
	{
		McpEditorServer.Stop();
	}
}
