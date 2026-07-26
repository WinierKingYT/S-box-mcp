using System;

namespace Editor;

[AttributeUsage( AttributeTargets.Class )]
public class McpToolGroupAttribute : Attribute
{
	public string Name { get; }

	public McpToolGroupAttribute( string name )
	{
		Name = name;
	}
}

[AttributeUsage( AttributeTargets.Method )]
public class McpToolAttribute : Attribute
{
	public string Name { get; }
	public string Description { get; }
	public McpToolImpact Impact { get; }

	public McpToolAttribute(
		string name,
		string description,
		McpToolImpact impact = McpToolImpact.HighRisk )
	{
		Name = name;
		Description = description;
		Impact = impact;
	}
}
