using System;
using System.Collections.Generic;

namespace Editor;

internal static class McpProtocolConstants
{
	public const string LatestVersion = "2025-11-25";

	public static readonly HashSet<string> SupportedVersions = new( StringComparer.Ordinal )
	{
		"2024-11-05",
		"2025-03-26",
		"2025-06-18",
		LatestVersion
	};
}
