using System.Text.Json;
using Xunit;

namespace Editor;

public class McpProtocolHandlerTests
{
	[Fact]
	public async Task Initialize_ReturnsRequestedSupportedVersion()
	{
		var fixture = new ProtocolFixture();
		var response = await fixture.Handler.ProcessAsync(
			"""{"jsonrpc":"2.0","id":"init","method":"initialize","params":{"protocolVersion":"2025-06-18"}}""" );

		using var document = JsonDocument.Parse( response );
		Assert.Equal(
			"2025-06-18",
			document.RootElement.GetProperty( "result" )
				.GetProperty( "protocolVersion" )
				.GetString() );
		Assert.Equal(
			"init",
			document.RootElement.GetProperty( "id" ).GetString() );
	}

	[Fact]
	public async Task Initialize_ReportsAssemblyVersionAndCapturesClientInfo()
	{
		var fixture = new ProtocolFixture();
		var response = await fixture.Handler.ProcessAsync(
			"""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","clientInfo":{"name":"Codex","version":"9.1"}}}""" );

		using var document = JsonDocument.Parse( response );
		var serverInfo = document.RootElement.GetProperty( "result" )
			.GetProperty( "serverInfo" );
		Assert.Equal( McpRuntimeState.ServerVersion, serverInfo.GetProperty( "version" ).GetString() );
		var runtime = McpRuntimeState.Snapshot();
		Assert.Equal( "Codex", runtime.ClientName );
		Assert.Equal( "9.1", runtime.ClientVersion );
		Assert.Equal( "Compatible", runtime.Compatibility );
	}

	[Fact]
	public async Task Initialize_FallsBackForUnknownVersion()
	{
		var fixture = new ProtocolFixture();
		var response = await fixture.Handler.ProcessAsync(
			"""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"future"}}""" );

		using var document = JsonDocument.Parse( response );
		Assert.Equal(
			McpProtocolConstants.LatestVersion,
			document.RootElement.GetProperty( "result" )
				.GetProperty( "protocolVersion" )
				.GetString() );
	}

	[Fact]
	public async Task Notification_ReturnsNoResponse()
	{
		var fixture = new ProtocolFixture();
		Assert.Null( await fixture.Handler.ProcessAsync(
			"""{"jsonrpc":"2.0","method":"notifications/initialized"}""" ) );
	}

	[Fact]
	public async Task ToolsList_ReturnsRegisteredTools()
	{
		var fixture = new ProtocolFixture();
		fixture.Registry.Register( "sample", "Sample tool", _ => "ok" );

		var response = await fixture.Handler.ProcessAsync(
			"""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""" );
		using var document = JsonDocument.Parse( response );
		var tools = document.RootElement.GetProperty( "result" ).GetProperty( "tools" );
		Assert.Single( tools.EnumerateArray() );
		Assert.Equal( "sample", tools[0].GetProperty( "name" ).GetString() );
		Assert.Equal(
			"HighRisk",
			tools[0].GetProperty( "_meta" )
				.GetProperty( "sbox/impact" )
				.GetString() );
	}

	[Fact]
	public async Task ToolsCall_UsesMainThreadAndReturnsStructuredContent()
	{
		var fixture = new ProtocolFixture();
		fixture.Registry.Register(
			"echo",
			"",
			arguments => new { value = arguments.GetProperty( "value" ).GetInt32() } );

		var responseTask = fixture.Handler.ProcessAsync(
			"""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"echo","arguments":{"value":42}}}""" );
		Assert.False( responseTask.IsCompleted );
		fixture.Dispatcher.Drain();

		using var document = JsonDocument.Parse( await responseTask );
		var result = document.RootElement.GetProperty( "result" );
		Assert.False( result.GetProperty( "isError" ).GetBoolean() );
		Assert.Equal(
			42,
			result.GetProperty( "structuredContent" )
				.GetProperty( "result" )
				.GetProperty( "value" )
				.GetInt32() );
	}

	[Fact]
	public async Task ToolsCall_UnknownToolReturnsInvalidParams()
	{
		var fixture = new ProtocolFixture();
		var response = await fixture.Handler.ProcessAsync(
			"""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"missing"}}""" );

		using var document = JsonDocument.Parse( response );
		Assert.Equal(
			-32602,
			document.RootElement.GetProperty( "error" ).GetProperty( "code" ).GetInt32() );
	}

	[Fact]
	public async Task ToolsCall_HandlerFailureReturnsToolError()
	{
		var fixture = new ProtocolFixture();
		fixture.Registry.Register(
			"explode",
			"",
			_ => throw new InvalidOperationException( "boom" ) );

		var responseTask = fixture.Handler.ProcessAsync(
			"""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"explode"}}""" );
		fixture.Dispatcher.Drain();

		using var document = JsonDocument.Parse( await responseTask );
		var result = document.RootElement.GetProperty( "result" );
		Assert.True( result.GetProperty( "isError" ).GetBoolean() );
		Assert.Equal(
			"boom",
			result.GetProperty( "content" )[0].GetProperty( "text" ).GetString() );
	}

	[Fact]
	public async Task ToolsCall_QueuesSceneMutationWhenApprovalIsEnabled()
	{
		var fixture = new ApprovalProtocolFixture();
		var executed = false;
		fixture.Registry.Register(
			"mutate",
			"Mutate scene",
			_ =>
			{
				executed = true;
				return "done";
			},
			impact: McpToolImpact.SceneMutation,
			supportsBatch: true );

		var responseTask = fixture.Handler.ProcessAsync(
			"""{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"mutate","arguments":{}}}""" );
		fixture.Dispatcher.Drain();

		using var document = JsonDocument.Parse( await responseTask );
		var queued = document.RootElement.GetProperty( "result" )
			.GetProperty( "structuredContent" )
			.GetProperty( "result" );
		Assert.Equal( "pending_approval", queued.GetProperty( "status" ).GetString() );
		Assert.False( executed );
	}

	[Fact]
	public async Task ToolsCall_ExecutesReadOnlyToolWithoutApproval()
	{
		var fixture = new ApprovalProtocolFixture();
		fixture.Registry.Register(
			"query",
			"Query",
			_ => new { value = 12 },
			impact: McpToolImpact.ReadOnly );

		var responseTask = fixture.Handler.ProcessAsync(
			"""{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"query","arguments":{}}}""" );
		fixture.Dispatcher.Drain();

		using var document = JsonDocument.Parse( await responseTask );
		Assert.Equal(
			12,
			document.RootElement.GetProperty( "result" )
				.GetProperty( "structuredContent" )
				.GetProperty( "result" )
				.GetProperty( "value" )
				.GetInt32() );
	}

	[Fact]
	public async Task LegacyDirectCallRemainsSupported()
	{
		var fixture = new ProtocolFixture();
		fixture.Registry.Register(
			"legacy",
			"",
			arguments => arguments.GetProperty( "value" ).GetInt32() );

		var responseTask = fixture.Handler.ProcessAsync(
			"""{"jsonrpc":"2.0","id":6,"method":"legacy","params":{"value":7}}""" );
		fixture.Dispatcher.Drain();

		using var document = JsonDocument.Parse( await responseTask );
		Assert.Equal( 7, document.RootElement.GetProperty( "result" ).GetInt32() );
	}

	[Fact]
	public async Task InvalidJsonReturnsParseError()
	{
		var fixture = new ProtocolFixture();
		var response = await fixture.Handler.ProcessAsync( "{" );

		using var document = JsonDocument.Parse( response );
		Assert.Equal(
			-32700,
			document.RootElement.GetProperty( "error" ).GetProperty( "code" ).GetInt32() );
	}

	private sealed class ProtocolFixture
	{
		public McpToolRegistry Registry { get; } = new();
		public McpMainThreadDispatcher Dispatcher { get; } = new();
		public McpProtocolHandler Handler { get; }

		public ProtocolFixture()
		{
			var executor = new McpToolExecutor( Dispatcher, () => 1000 );
			Handler = new McpProtocolHandler(
				Registry,
				executor,
				new McpRateLimiter( () => 100 ),
				new TestLogger() );
		}
	}

	private sealed class TestLogger : IMcpLogger
	{
		public void Info( string message )
		{
		}

		public void Warning( string message )
		{
		}

		public void Error( string message )
		{
		}
	}

	private sealed class ApprovalProtocolFixture
	{
		public McpToolRegistry Registry { get; } = new();
		public McpMainThreadDispatcher Dispatcher { get; } = new();
		public McpProtocolHandler Handler { get; }

		public ApprovalProtocolFixture()
		{
			var config = new McpConfig
			{
				ApprovalMode = "AllMutations",
				PersistHistory = false
			};
			var activity = new McpActivityStore( () => config );
			var approvals = new McpApprovalService(
				Registry,
				() => config,
				activity,
				() => "stable" );
			var executor = new McpToolExecutor(
				Dispatcher,
				() => 1000,
				approvals,
				activity );
			Handler = new McpProtocolHandler(
				Registry,
				executor,
				new McpRateLimiter( () => 100 ),
				new TestLogger() );
		}
	}
}
