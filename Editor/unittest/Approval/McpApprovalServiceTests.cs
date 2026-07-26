using System.Text.Json;
using Xunit;

namespace Editor;

public class McpApprovalServiceTests
{
	[Fact]
	public void SubmitSingle_QueuesSceneMutationWithoutExecutingIt()
	{
		var executed = false;
		var fixture = CreateFixture();
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
		Assert.True( fixture.Registry.TryGet( "mutate", out var tool ) );

		var result = fixture.Service.SubmitSingle( tool, Json( """{"id":"target"}""" ) );
		var resultJson = JsonSerializer.Serialize( result );

		Assert.Contains( "\"status\":\"pending_approval\"", resultJson );
		Assert.False( executed );
		Assert.Equal( 1, fixture.Service.PendingCount );
	}

	[Fact]
	public void Reject_PreventsPendingRequestFromExecuting()
	{
		var fixture = CreateFixture();
		fixture.Registry.Register(
			"mutate",
			"Mutate",
			_ => "done",
			impact: McpToolImpact.SceneMutation,
			supportsBatch: true );
		Assert.True( fixture.Registry.TryGet( "mutate", out var tool ) );
		var submitted = JsonDocument.Parse(
			JsonSerializer.Serialize( fixture.Service.SubmitSingle( tool, Json( "{}" ) ) ) );
		var id = submitted.RootElement.GetProperty( "requestId" ).GetString();

		var rejected = JsonSerializer.Serialize( fixture.Service.Reject( id ) );

		Assert.Contains( "\"status\":\"rejected\"", rejected );
		Assert.Equal( 0, fixture.Service.PendingCount );
	}

	[Fact]
	public void SubmitBatch_RejectsReadOnlyTools()
	{
		var fixture = CreateFixture();
		fixture.Registry.Register(
			"query",
			"Query",
			_ => "ok",
			impact: McpToolImpact.ReadOnly );

		Assert.Throws<ArgumentException>( () => fixture.Service.SubmitBatch(
			Json( """{"operations":[{"tool":"query","arguments":{}}]}""" ) ) );
	}

	[Fact]
	public void SubmitBatch_QueuesMultipleSupportedSceneMutations()
	{
		var fixture = CreateFixture();
		fixture.Registry.Register(
			"mutate",
			"Mutate",
			_ => "ok",
			impact: McpToolImpact.SceneMutation,
			supportsBatch: true );

		var result = JsonSerializer.Serialize( fixture.Service.SubmitBatch(
			Json(
				"""{"summary":"Build room","operations":[{"tool":"mutate","arguments":{"id":"one"}},{"tool":"mutate","arguments":{"id":"two"}}]}""" ) ) );

		Assert.Contains( "\"status\":\"pending_approval\"", result );
		Assert.Contains( "Build room", result );
		Assert.Equal( 1, fixture.Service.PendingCount );
	}

	[Fact]
	public void Approve_ExecutesHighRiskRequestExactlyOnce()
	{
		var calls = 0;
		var fixture = CreateFixture();
		fixture.Registry.Register(
			"danger",
			"Danger",
			_ =>
			{
				calls++;
				return "ok";
			},
			impact: McpToolImpact.HighRisk );
		Assert.True( fixture.Registry.TryGet( "danger", out var tool ) );
		var submitted = JsonDocument.Parse(
			JsonSerializer.Serialize( fixture.Service.SubmitSingle( tool, Json( "{}" ) ) ) );
		var id = submitted.RootElement.GetProperty( "requestId" ).GetString();

		fixture.Service.Approve( id );
		fixture.Service.Approve( id );

		Assert.Equal( 1, calls );
		Assert.Contains(
			"\"status\":\"succeeded\"",
			JsonSerializer.Serialize( fixture.Service.Get( id ) ) );
	}

	[Fact]
	public void Cancel_RemovesRequestFromPendingQueue()
	{
		var fixture = CreateFixture();
		fixture.Registry.Register(
			"danger",
			"Danger",
			_ => "ok",
			impact: McpToolImpact.HighRisk );
		Assert.True( fixture.Registry.TryGet( "danger", out var tool ) );
		var id = RequestId( fixture.Service.SubmitSingle( tool, Json( "{}" ) ) );

		var cancelled = JsonSerializer.Serialize( fixture.Service.Cancel( id ) );

		Assert.Contains( "\"status\":\"cancelled\"", cancelled );
		Assert.Equal( 0, fixture.Service.PendingCount );
	}

	[Fact]
	public void SubmitSingle_RejectsWhenQueueLimitIsReached()
	{
		var config = new McpConfig
		{
			ApprovalMode = "AllMutations",
			MaxPendingApprovals = 1,
			PersistHistory = false
		};
		var registry = new McpToolRegistry();
		var activity = new McpActivityStore( () => config );
		var service = new McpApprovalService( registry, () => config, activity );
		registry.Register( "danger", "", _ => "ok", impact: McpToolImpact.HighRisk );
		Assert.True( registry.TryGet( "danger", out var tool ) );
		service.SubmitSingle( tool, Json( "{}" ) );

		Assert.Throws<InvalidOperationException>( () =>
			service.SubmitSingle( tool, Json( "{}" ) ) );
	}

	[Fact]
	public void PendingRequest_ExpiresAfterConfiguredDeadline()
	{
		var config = new McpConfig
		{
			ApprovalMode = "AllMutations",
			ApprovalTimeoutSeconds = -1,
			PersistHistory = false
		};
		var registry = new McpToolRegistry();
		var activity = new McpActivityStore( () => config );
		var service = new McpApprovalService( registry, () => config, activity );
		registry.Register( "danger", "", _ => "ok", impact: McpToolImpact.HighRisk );
		Assert.True( registry.TryGet( "danger", out var tool ) );
		var id = RequestId( service.SubmitSingle( tool, Json( "{}" ) ) );

		Assert.Contains(
			"\"status\":\"expired\"",
			JsonSerializer.Serialize( service.Get( id ) ) );
	}

	[Fact]
	public void Approve_MarksSceneRequestStaleWhenFingerprintChanges()
	{
		var fingerprint = "before";
		var config = new McpConfig { PersistHistory = false };
		var registry = new McpToolRegistry();
		var activity = new McpActivityStore( () => config );
		var service = new McpApprovalService(
			registry,
			() => config,
			activity,
			() => fingerprint );
		var executed = false;
		registry.Register(
			"mutate",
			"Mutate",
			_ =>
			{
				executed = true;
				return "ok";
			},
			impact: McpToolImpact.SceneMutation,
			supportsBatch: true );
		Assert.True( registry.TryGet( "mutate", out var tool ) );
		var id = RequestId( service.SubmitSingle( tool, Json( "{}" ) ) );
		fingerprint = "after";

		var result = JsonSerializer.Serialize( service.Approve( id ) );

		Assert.Contains( "\"status\":\"stale\"", result );
		Assert.False( executed );
	}

	[Theory]
	[InlineData( "AllMutations", McpToolImpact.SceneMutation, true )]
	[InlineData( "AllMutations", McpToolImpact.ReadOnly, false )]
	[InlineData( "HighRiskOnly", McpToolImpact.SceneMutation, false )]
	[InlineData( "HighRiskOnly", McpToolImpact.HighRisk, true )]
	[InlineData( "Disabled", McpToolImpact.HighRisk, false )]
	public void RequiresApproval_UsesConfiguredPolicy(
		string mode,
		McpToolImpact impact,
		bool expected )
	{
		var fixture = CreateFixture( mode );
		fixture.Registry.Register( "tool", "", _ => null, impact: impact );
		Assert.True( fixture.Registry.TryGet( "tool", out var tool ) );

		Assert.Equal( expected, fixture.Service.RequiresApproval( tool ) );
	}

	private static ApprovalFixture CreateFixture( string mode = "AllMutations" )
	{
		var config = new McpConfig
		{
			ApprovalMode = mode,
			PersistHistory = false,
			HistoryLimit = 50
		};
		var registry = new McpToolRegistry();
		var activity = new McpActivityStore( () => config );
		return new(
			registry,
			new McpApprovalService( registry, () => config, activity ) );
	}

	private static JsonElement Json( string json )
		=> JsonDocument.Parse( json ).RootElement.Clone();

	private static string RequestId( object result )
		=> JsonDocument.Parse( JsonSerializer.Serialize( result ) )
			.RootElement.GetProperty( "requestId" )
			.GetString();

	private sealed record ApprovalFixture(
		McpToolRegistry Registry,
		McpApprovalService Service );
}
