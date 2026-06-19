using Sandbox;
using System.Threading.Tasks;

namespace Sandbox;

using McpBridge;

public sealed class McpBridgeComponent : Component
{
	public static McpBridgeComponent Instance { get; private set; }
	private const string BridgeName = "default";

	protected override void OnStart()
	{
		Instance = this;

		McpToolDispatcher.Instance.EnsureInitialized();

		McpToolBridge.Register( new BridgeRegistration(
			BridgeName,
			( method, args ) => McpToolDispatcher.Instance.Execute( method, args ),
			() => Task.FromResult( McpToolDispatcher.Instance.GetToolsJson() ),
			() => Task.FromResult( McpToolDispatcher.Instance.HealthSummary() )
		) );

		Log.Info( "[MCP] Bridge registered: game tools available via Editor SSE" );
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
		{
			McpToolBridge.Unregister( BridgeName );
			Instance = null;
		}
	}
}
