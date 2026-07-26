using System;
using System.Reflection;

namespace Editor;

internal sealed record McpRuntimeSnapshot(
	bool IsRunning,
	int Port,
	DateTimeOffset? LastRequestAt,
	string ClientName,
	string ClientVersion,
	string ProtocolVersion,
	string Compatibility,
	string ServerVersion );

internal static class McpRuntimeState
{
	private static readonly object Sync = new();
	private static bool _isRunning;
	private static int _port;
	private static DateTimeOffset? _lastRequestAt;
	private static string _clientName = "unknown";
	private static string _clientVersion = "";
	private static string _protocolVersion = "";
	private static string _compatibility = "No client connected";

	public static string ServerVersion
		=> typeof( McpRuntimeState ).Assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
			?.InformationalVersion?.Split( '+' )[0]
			?? typeof( McpRuntimeState ).Assembly.GetName().Version?.ToString()
			?? "1.2.0";

	public static void SetServer( bool running, int port )
	{
		lock ( Sync )
		{
			_isRunning = running;
			_port = port;
		}
	}

	public static void RecordRequest()
	{
		lock ( Sync ) _lastRequestAt = DateTimeOffset.UtcNow;
	}

	public static void RecordClient(
		string name,
		string version,
		string protocolVersion,
		string compatibility )
	{
		lock ( Sync )
		{
			_clientName = string.IsNullOrWhiteSpace( name ) ? "unknown" : name;
			_clientVersion = version ?? "";
			_protocolVersion = protocolVersion ?? "";
			_compatibility = compatibility ?? "";
			_lastRequestAt = DateTimeOffset.UtcNow;
		}
	}

	public static void RecordProtocolHeader( string protocolVersion )
	{
		lock ( Sync )
		{
			if ( !string.IsNullOrWhiteSpace( protocolVersion ) )
				_protocolVersion = protocolVersion;
			_compatibility = McpProtocolVersionPolicy.Describe( protocolVersion );
			_lastRequestAt = DateTimeOffset.UtcNow;
		}
	}

	public static McpRuntimeSnapshot Snapshot()
	{
		lock ( Sync )
		{
			return new(
				_isRunning,
				_port,
				_lastRequestAt,
				_clientName,
				_clientVersion,
				_protocolVersion,
				_compatibility,
				ServerVersion );
		}
	}
}
