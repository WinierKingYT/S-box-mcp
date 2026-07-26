using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Editor;

internal static class McpHttpSecurity
{
	public static bool IsAuthorized( string suppliedKey, string configuredKey )
	{
		if ( string.IsNullOrEmpty( suppliedKey ) || string.IsNullOrEmpty( configuredKey ) )
			return false;

		var suppliedBytes = Encoding.UTF8.GetBytes( suppliedKey );
		var configuredBytes = Encoding.UTF8.GetBytes( configuredKey );
		return suppliedBytes.Length == configuredBytes.Length
			&& CryptographicOperations.FixedTimeEquals( suppliedBytes, configuredBytes );
	}

	public static bool IsOriginAllowed(
		string origin,
		IReadOnlyCollection<string> allowedOrigins )
	{
		if ( string.IsNullOrWhiteSpace( origin ) ) return true;
		return allowedOrigins.Any( configured =>
			configured == "*"
			|| string.Equals( configured, origin, StringComparison.OrdinalIgnoreCase ) );
	}
}
