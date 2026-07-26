namespace Editor;

internal static class McpProtocolVersionPolicy
{
	public static bool IsAcceptedHeader( string protocolVersion )
		=> string.IsNullOrWhiteSpace( protocolVersion )
			|| McpProtocolConstants.SupportedVersions.Contains( protocolVersion );

	public static string Describe( string protocolVersion )
		=> string.IsNullOrWhiteSpace( protocolVersion )
			? "Legacy client: MCP-Protocol-Version header missing"
			: IsAcceptedHeader( protocolVersion )
				? "Compatible"
				: $"Unsupported MCP protocol version: {protocolVersion}";
}
