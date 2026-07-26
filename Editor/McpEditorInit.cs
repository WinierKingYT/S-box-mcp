using System;
using System.Runtime.CompilerServices;

namespace Editor;

internal static class McpEditorInit
{
	private static bool _started;

	[ModuleInitializer]
	internal static void Initialize()
	{
		if ( _started ) return;
		_started = true;
		McpEditorServer.Start();
	}

	[Menu( "Editor", "MCP Server/Start Server" )]
	public static void StartServer()
	{
		McpEditorServer.Start();
	}
}
