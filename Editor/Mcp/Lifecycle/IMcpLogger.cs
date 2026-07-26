namespace Editor;

internal interface IMcpLogger
{
	void Info( string message );
	void Warning( string message );
	void Error( string message );
}
