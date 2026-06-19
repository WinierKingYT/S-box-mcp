using System;

namespace McpBridge;

[AttributeUsage( AttributeTargets.Method )]
public sealed class McpToolAttribute( string name, string description ) : Attribute
{
	public string Name { get; } = name;
	public string Description { get; } = description;
	public string[] OptionalParams { get; init; } = System.Array.Empty<string>();
}

[AttributeUsage( AttributeTargets.Class )]
public sealed class McpToolGroupAttribute( string groupName ) : Attribute
{
	public string GroupName { get; } = groupName;
}


