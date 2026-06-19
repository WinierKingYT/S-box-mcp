using Sandbox;
using System;

namespace McpBridge;

public static class McpScene
{
	public static Scene Active { get; private set; }
	public static event Action<Scene> SceneChanged;

	public static void Update( Scene scene )
	{
		if ( Active == scene ) return;
		Active = scene;
		SceneChanged?.Invoke( scene );
	}
}
