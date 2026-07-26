using Sandbox;

namespace Editor;

internal sealed class SandboxMcpLogger : IMcpLogger
{
	public void Info( string message )
		=> Log.Info( message );

	public void Warning( string message )
		=> Log.Warning( message );

	public void Error( string message )
		=> Log.Error( message );
}
