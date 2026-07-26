using System.Text.Json;
using Xunit;

namespace Editor;

public class McpToolRegistryTests
{
	[Fact]
	public void Register_ReplacesExistingDefinition()
	{
		var registry = new McpToolRegistry();
		registry.Register( "sample", "first", _ => 1 );
		registry.Register( "sample", "second", _ => 2 );

		Assert.True( registry.TryGet( "sample", out var definition ) );
		Assert.Equal( "second", definition.Description );
		Assert.Equal( 2, definition.Handler( default ) );
	}

	[Fact]
	public void Snapshot_IsSortedByOrdinalName()
	{
		var registry = new McpToolRegistry();
		registry.Register( "zeta", "", _ => null );
		registry.Register( "alpha", "", _ => null );

		Assert.Equal(
			new[] { "alpha", "zeta" },
			registry.Snapshot().Select( tool => tool.Name ) );
	}

	[Fact]
	public void Register_CreatesDefaultObjectSchema()
	{
		var registry = new McpToolRegistry();
		registry.Register( "sample", "", _ => null );

		Assert.True( registry.TryGet( "sample", out var definition ) );
		var json = JsonSerializer.Serialize( definition.InputSchema );
		using var document = JsonDocument.Parse( json );
		Assert.Equal(
			"object",
			document.RootElement.GetProperty( "type" ).GetString() );
	}

	[Fact]
	public void Register_DefaultsUnknownToolsToHighRisk()
	{
		var registry = new McpToolRegistry();
		registry.Register( "external", "", _ => null );

		Assert.True( registry.TryGet( "external", out var definition ) );
		Assert.Equal( McpToolImpact.HighRisk, definition.Impact );
		Assert.False( definition.SupportsBatch );
	}
}
