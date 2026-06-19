using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox;

using McpBridge;

public sealed class McpBridgeComponent : Component
{
	public static McpBridgeComponent Instance { get; private set; }
	private const string BridgeName = "default";

	private struct Toast
	{
		public string Message;
		public string Type;
		public float Duration;
		public RealTimeSince TimeSinceCreated;
	}

	private readonly List<Toast> _toasts = new();

	public static void ShowToast( string message, string type, float duration )
	{
		var inst = Instance;
		if ( inst == null ) return;
		lock ( inst._toasts )
		{
			inst._toasts.Add( new Toast
			{
				Message = message,
				Type = type,
				Duration = duration,
				TimeSinceCreated = 0f
			} );
		}
	}

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

	protected override void OnUpdate()
	{
		lock ( _toasts )
		{
			_toasts.RemoveAll( t => t.TimeSinceCreated >= t.Duration );

			int index = 0;
			foreach ( var toast in _toasts )
			{
				var color = toast.Type.ToLower() switch
				{
					"success" => Color.Green,
					"warning" => Color.Yellow,
					"error" => Color.Red,
					_ => Color.White
				};

				var text = $"[{toast.Type.ToUpper()}] {toast.Message}";
				var screenPos = new Vector2( 20, 100 + index * 25 );
				DebugOverlay.ScreenText( screenPos, text, 0f, TextFlag.Left, color );
				index++;
			}
		}
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
