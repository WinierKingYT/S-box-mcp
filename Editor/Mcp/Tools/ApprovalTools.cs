using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Editor;

internal static class ApprovalTools
{
	public static void Register(
		McpToolRegistry registry,
		McpApprovalService approvals,
		Func<object> serverStatus )
	{
		registry.Register(
			"submit_scene_changes",
			"Preview multiple scene mutations and submit them as one approval request",
			approvals.SubmitBatch,
			new
			{
				type = "object",
				properties = new Dictionary<string, object>
				{
					{ "summary", new { type = "string", description = "Human-readable batch summary" } },
					{
						"operations",
						new
						{
							type = "array",
							description = "Array of {tool, arguments} scene mutations",
							items = new
							{
								type = "object",
								properties = new Dictionary<string, object>
								{
									{ "tool", new { type = "string" } },
									{ "arguments", new { type = "object" } }
								},
								required = new[] { "tool", "arguments" }
							}
						}
					}
				},
				required = new[] { "operations" }
			},
			McpToolImpact.EditorSideEffect );

		registry.Register(
			"get_change_request",
			"Get the current state and result of an approval request",
			arguments => approvals.Get( RequiredString( arguments, "request_id" ) ),
			RequestIdSchema(),
			McpToolImpact.ReadOnly );

		registry.Register(
			"cancel_change_request",
			"Cancel a pending approval request",
			arguments => approvals.Cancel( RequiredString( arguments, "request_id" ) ),
			RequestIdSchema(),
			McpToolImpact.EditorSideEffect );

		registry.Register(
			"get_server_status",
			"Get MCP server, compatibility, history, and approval queue status",
			_ => serverStatus(),
			new
			{
				type = "object",
				properties = new Dictionary<string, object>(),
				additionalProperties = false
			},
			McpToolImpact.ReadOnly );
	}

	private static string RequiredString( JsonElement arguments, string name )
	{
		if ( arguments.ValueKind != JsonValueKind.Object
			|| !arguments.TryGetProperty( name, out var property )
			|| property.ValueKind != JsonValueKind.String
			|| string.IsNullOrWhiteSpace( property.GetString() ) )
			throw new ArgumentException( $"{name} is required" );
		return property.GetString();
	}

	private static object RequestIdSchema()
		=> new
		{
			type = "object",
			properties = new Dictionary<string, object>
			{
				{ "request_id", new { type = "string", description = "Approval request identifier" } }
			},
			required = new[] { "request_id" }
		};
}
