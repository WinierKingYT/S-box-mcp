using System.Net;
using System.Text;
using SboxMcp.StdioBridge;
using Xunit;

namespace SboxMcp.StdioBridge.Tests;

public class McpStdioBridgeTests
{
	[Fact]
	public async Task RunAsync_ForwardsApiKeyAndResponse()
	{
		var handler = new FakeHandler( request =>
		{
			Assert.Equal( "secret", request.Headers.GetValues( "x-api-key" ).Single() );
			Assert.Equal( "/mcp", request.RequestUri!.AbsolutePath );
			return new HttpResponseMessage( HttpStatusCode.OK )
			{
				Content = new StringContent(
					"""{"jsonrpc":"2.0","id":1,"result":{}}""",
					Encoding.UTF8,
					"application/json" )
			};
		} );
		using var client = new HttpClient( handler );
		var bridge = new McpStdioBridge(
			client,
			new Uri( "http://localhost:29017/mcp" ),
			() => "secret" );
		var output = new StringWriter();

		await bridge.RunAsync(
			new StringReader(
				"""{"jsonrpc":"2.0","id":1,"method":"initialize"}""" ),
			output,
			new StringWriter(),
			CancellationToken.None );

		Assert.Contains( "\"result\"", output.ToString(), StringComparison.Ordinal );
	}

	[Fact]
	public async Task RunAsync_DoesNotWriteForAcceptedNotification()
	{
		using var client = new HttpClient(
			new FakeHandler( _ => new HttpResponseMessage( HttpStatusCode.Accepted ) ) );
		var bridge = new McpStdioBridge(
			client,
			new Uri( "http://localhost:29017/mcp" ),
			() => "secret" );
		var output = new StringWriter();

		await bridge.RunAsync(
			new StringReader(
				"""{"jsonrpc":"2.0","method":"notifications/initialized"}""" ),
			output,
			new StringWriter(),
			CancellationToken.None );

		Assert.Equal( "", output.ToString() );
	}

	[Fact]
	public async Task RunAsync_ConvertsHttpFailureToJsonRpcError()
	{
		using var client = new HttpClient(
			new FakeHandler( _ => new HttpResponseMessage( HttpStatusCode.Unauthorized ) ) );
		var bridge = new McpStdioBridge(
			client,
			new Uri( "http://localhost:29017/mcp" ),
			() => "wrong" );
		var output = new StringWriter();
		var error = new StringWriter();

		await bridge.RunAsync(
			new StringReader(
				"""{"jsonrpc":"2.0","id":"auth","method":"ping"}""" ),
			output,
			error,
			CancellationToken.None );

		Assert.Contains( "\"id\":\"auth\"", output.ToString(), StringComparison.Ordinal );
		Assert.Contains( "\"code\":-32000", output.ToString(), StringComparison.Ordinal );
		Assert.Contains( "401", error.ToString(), StringComparison.Ordinal );
	}

	[Theory]
	[InlineData( "https://example.test/mcp" )]
	[InlineData( "http://192.168.1.2/mcp" )]
	public void TryParse_RejectsNonLoopbackUrls( string url )
	{
		Assert.False( BridgeOptions.TryParse(
			new[] { "--url", url },
			out _,
			out _ ) );
	}

	private sealed class FakeHandler : HttpMessageHandler
	{
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

		public FakeHandler( Func<HttpRequestMessage, HttpResponseMessage> handler )
			=> _handler = handler;

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken )
			=> Task.FromResult( _handler( request ) );
	}
}
