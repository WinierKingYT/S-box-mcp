using Sandbox;
using System.Threading;
using System.Threading.Tasks;

namespace McpBridge;

public static class McpBridgeAutoInit
{
	private static int _started;

	public static void EnsureCreated()
	{
		if ( Interlocked.Exchange( ref _started, 1 ) != 0 ) return;
		_ = CreateAsync();
	}

	[Event( "game.active" )]
	public static void OnGameActive()
	{
		EnsureCreated();
	}

	private static async Task CreateAsync()
	{
		await GameTask.MainThread();
		while ( true )
		{
			try
			{
				await GameTask.Delay( 100 );

				if ( McpBridgeComponent.Instance != null )
					return;

				var scene = Game.ActiveScene;
				if ( scene == null )
					continue;

				var go = new GameObject();
				go.Name = "MCP Bridge";
				go.Components.Create<McpBridgeComponent>();
				Log.Info( "[MCP] Auto-created McpBridgeComponent" );
				return;
			}
			catch
			{
				await GameTask.Delay( 500 );
			}
		}
	}
}
