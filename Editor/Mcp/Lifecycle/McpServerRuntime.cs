using Sandbox;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Editor;

internal sealed class McpServerRuntime
{
	private readonly object _lifecycleLock = new();
	private readonly McpToolRegistry _registry;
	private readonly McpMainThreadDispatcher _dispatcher;
	private readonly McpHttpServer _httpServer;
	private readonly McpAttributeToolDiscovery _discovery;
	private readonly IEditorScreenshotCapture _screenshotCapture;
	private readonly McpActivityStore _activity;
	private readonly McpApprovalService _approvals;
	private Task _listenTask;
	private bool _toolsRegistered;

	public McpServerRuntime()
	{
		_registry = new McpToolRegistry();
		_dispatcher = new McpMainThreadDispatcher();
		_activity = new McpActivityStore( () => McpConfig.Current );
		_approvals = new McpApprovalService(
			_registry,
			() => McpConfig.Current,
			_activity );
		var executor = new McpToolExecutor(
			_dispatcher,
			() => McpConfig.Current.RequestTimeoutMs,
			_approvals,
			_activity );
		var rateLimiter = new McpRateLimiter(
			() => McpConfig.Current.MaxRequestsPerSecond );
		var protocol = new McpProtocolHandler(
			_registry,
			executor,
			rateLimiter,
			new SandboxMcpLogger() );
		var sessions = new McpSseSessionManager( () => McpConfig.Current );
		_httpServer = new McpHttpServer(
			() => McpConfig.Current,
			protocol,
			sessions );
		_discovery = new McpAttributeToolDiscovery(
			_registry,
			() => McpConfig.Current );
		_screenshotCapture = new WindowsEditorScreenshotCapture();
	}

	public void Start()
	{
		lock ( _lifecycleLock )
		{
			if ( _listenTask != null && !_listenTask.IsCompleted ) return;
			if ( string.IsNullOrWhiteSpace( McpConfig.Current.ApiKey ) )
			{
				Log.Error(
					"[MCP] Server not started: set ApiKey in mcp_config.json or SBOX_MCP_API_KEY." );
				return;
			}
			if ( !McpConfig.Current.IsCompatible )
			{
				Log.Error(
					$"[MCP] Server not started: {McpConfig.Current.CompatibilityMessage}" );
				return;
			}

			EnsureToolsRegistered();
			_listenTask = _httpServer.RunAsync();
		}
	}

	public void Stop()
	{
		lock ( _lifecycleLock )
		{
			_httpServer.Stop();
			McpRuntimeState.SetServer( false, McpConfig.Current.Port );
		}
	}

	public void DrainMainThreadQueue()
		=> _dispatcher.Drain();

	public void RegisterTool(
		string name,
		string description,
		Func<JsonElement, object> handler,
		object inputSchema = null,
		McpToolImpact impact = McpToolImpact.HighRisk )
	{
		_registry.Register( name, description, handler, inputSchema, impact );
		Log.Info( $"[MCP] Tool registered: {name} — {description}" );
	}

	private void EnsureToolsRegistered()
	{
		if ( _toolsRegistered ) return;

		BuiltInToolCatalog.RegisterAll(
			_registry,
			() => McpConfig.Current,
			_screenshotCapture );
		ApprovalTools.Register(
			_registry,
			_approvals,
			BuildStatus );
		foreach ( var tool in _registry.Snapshot() )
			Log.Info( $"[MCP] Tool registered: {tool.Name} — {tool.Description}" );
		_discovery.Discover();
		_toolsRegistered = true;
	}

	public McpApprovalService Approvals => _approvals;
	public McpActivityStore Activity => _activity;
	public bool IsRunning => _httpServer.IsRunning;
	public int ActiveSseSessions => _httpServer.ActiveSseSessions;

	private object BuildStatus()
	{
		var runtime = McpRuntimeState.Snapshot();
		return new
		{
			running = _httpServer.IsRunning,
			endpoint = $"http://localhost:{McpConfig.Current.Port}/mcp",
			serverVersion = runtime.ServerVersion,
			configVersion = McpConfig.Current.ConfigVersion,
			configCompatible = McpConfig.Current.IsCompatible,
			approvalMode = McpConfig.Current.ApprovalMode,
			pendingApprovals = _approvals.PendingCount,
			activeSseSessions = _httpServer.ActiveSseSessions,
			lastClient = runtime.ClientName,
			clientVersion = runtime.ClientVersion,
			protocolVersion = runtime.ProtocolVersion,
			compatibility = runtime.Compatibility
		};
	}
}
