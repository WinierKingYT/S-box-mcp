using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SboxMcp.StdioBridge;

public static class Program
{
	public static async Task<int> Main( string[] args )
	{
		if ( !BridgeOptions.TryParse( args, out var options, out var error ) )
		{
			await Console.Error.WriteLineAsync( error );
			return 2;
		}

		var apiKey = Environment.GetEnvironmentVariable( options.ApiKeyEnvironmentVariable );
		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			await Console.Error.WriteLineAsync(
				$"Required environment variable '{options.ApiKeyEnvironmentVariable}' is not set." );
			return 2;
		}

		using var client = new HttpClient { Timeout = options.Timeout };
		var bridge = new McpStdioBridge( client, options.Url, () => apiKey );
		await bridge.RunAsync(
			Console.In,
			Console.Out,
			Console.Error,
			CancellationToken.None );
		return 0;
	}
}

public sealed record BridgeOptions(
	Uri Url,
	string ApiKeyEnvironmentVariable,
	TimeSpan Timeout )
{
	public static bool TryParse(
		IReadOnlyList<string> args,
		out BridgeOptions options,
		out string error )
	{
		var url = "http://localhost:29017/mcp";
		var apiKeyEnvironmentVariable = "SBOX_MCP_API_KEY";
		var timeoutSeconds = 35;
		for ( var index = 0; index < args.Count; index++ )
		{
			var argument = args[index];
			if ( argument is "--url" or "--api-key-env" or "--timeout-seconds" )
			{
				if ( index + 1 >= args.Count )
				return Fail( $"Missing value for {argument}", out options, out error );
				var value = args[++index];
				if ( argument == "--url" )
					url = value;
				else if ( argument == "--api-key-env" )
					apiKeyEnvironmentVariable = value;
				else if ( !int.TryParse( value, out timeoutSeconds )
					|| timeoutSeconds <= 0 )
					return Fail(
						"--timeout-seconds must be a positive integer",
						out options,
						out error );
			}
			else
			{
				return Fail(
					$"Unknown argument: {argument}",
					out options,
					out error );
			}
		}

		if ( !Uri.TryCreate( url, UriKind.Absolute, out var endpoint )
			|| endpoint.Scheme != Uri.UriSchemeHttp
			|| !endpoint.IsLoopback )
			return Fail(
				"--url must be an absolute loopback HTTP URL",
				out options,
				out error );
		if ( string.IsNullOrWhiteSpace( apiKeyEnvironmentVariable ) )
			return Fail(
				"--api-key-env cannot be empty",
				out options,
				out error );

		options = new(
			endpoint,
			apiKeyEnvironmentVariable,
			TimeSpan.FromSeconds( timeoutSeconds ) );
		error = "";
		return true;
	}

	private static bool Fail(
		string message,
		out BridgeOptions options,
		out string error )
	{
		options = null!;
		error = message;
		return false;
	}
}

public sealed class McpStdioBridge
{
	private readonly HttpClient _client;
	private readonly Uri _url;
	private readonly Func<string?> _apiKeyProvider;

	public McpStdioBridge(
		HttpClient client,
		Uri url,
		Func<string?> apiKeyProvider )
	{
		_client = client;
		_url = url;
		_apiKeyProvider = apiKeyProvider;
	}

	public async Task RunAsync(
		TextReader input,
		TextWriter output,
		TextWriter error,
		CancellationToken cancellationToken )
	{
		while ( !cancellationToken.IsCancellationRequested )
		{
			var line = await input.ReadLineAsync( cancellationToken );
			if ( line == null ) break;
			if ( string.IsNullOrWhiteSpace( line ) ) continue;

			try
			{
				using var request = new HttpRequestMessage( HttpMethod.Post, _url );
				request.Content = new StringContent(
					line,
					Encoding.UTF8,
					"application/json" );
				request.Headers.Accept.Add(
					new MediaTypeWithQualityHeaderValue( "application/json" ) );
				request.Headers.Accept.Add(
					new MediaTypeWithQualityHeaderValue( "text/event-stream" ) );
				request.Headers.TryAddWithoutValidation(
					"x-api-key",
					_apiKeyProvider() );
				request.Headers.TryAddWithoutValidation(
					"MCP-Protocol-Version",
					ReadProtocolVersion( line ) );

				using var response = await _client.SendAsync(
					request,
					HttpCompletionOption.ResponseContentRead,
					cancellationToken );
				if ( response.StatusCode == System.Net.HttpStatusCode.Accepted )
					continue;

				var responseBody = await response.Content.ReadAsStringAsync(
					cancellationToken );
				if ( response.IsSuccessStatusCode )
				{
					if ( !string.IsNullOrWhiteSpace( responseBody ) )
						await output.WriteLineAsync( responseBody );
					await output.FlushAsync( cancellationToken );
					continue;
				}

				await error.WriteLineAsync(
					$"MCP HTTP request failed: {(int)response.StatusCode} {response.ReasonPhrase}" );
				await output.WriteLineAsync(
					BuildErrorResponse(
						line,
						$"MCP server returned HTTP {(int)response.StatusCode}" ) );
				await output.FlushAsync( cancellationToken );
			}
			catch ( Exception exception ) when (
				exception is not OperationCanceledException
				|| !cancellationToken.IsCancellationRequested )
			{
				await error.WriteLineAsync(
					$"MCP bridge request failed: {exception.GetBaseException().Message}" );
				await output.WriteLineAsync(
					BuildErrorResponse(
						line,
						$"MCP bridge failed: {exception.GetBaseException().Message}" ) );
				await output.FlushAsync( cancellationToken );
			}
		}
	}

	private static string ReadProtocolVersion( string request )
	{
		try
		{
			using var document = JsonDocument.Parse( request );
			if ( document.RootElement.TryGetProperty( "params", out var parameters )
				&& parameters.TryGetProperty(
					"protocolVersion",
					out var protocolVersion )
				&& protocolVersion.ValueKind == JsonValueKind.String )
				return protocolVersion.GetString() ?? "2025-11-25";
		}
		catch ( JsonException )
		{
			// The server will return the canonical JSON-RPC parse error.
		}
		return "2025-11-25";
	}

	private static string BuildErrorResponse( string request, string message )
	{
		JsonElement? id = null;
		try
		{
			using var document = JsonDocument.Parse( request );
			if ( document.RootElement.TryGetProperty( "id", out var idProperty ) )
				id = idProperty.Clone();
		}
		catch ( JsonException )
		{
			// Keep a null id for malformed requests.
		}

		return JsonSerializer.Serialize( new
		{
			jsonrpc = "2.0",
			id,
			error = new { code = -32000, message }
		} );
	}
}
