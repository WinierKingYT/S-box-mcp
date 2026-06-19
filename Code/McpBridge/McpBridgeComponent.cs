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
		var toolCount = McpToolDispatcher.Instance.Registry.ListAll().Count;

		McpToolBridge.Register( new BridgeRegistration(
			BridgeName,
			( method, args ) => McpToolDispatcher.Instance.Execute( method, args ),
			() => Task.FromResult( McpToolDispatcher.Instance.GetToolsJson() ),
			() => Task.FromResult( McpToolDispatcher.Instance.HealthSummary() ),
			ToolCount: toolCount,
			Capabilities: new[] { "tools", "resources", "state" }
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
