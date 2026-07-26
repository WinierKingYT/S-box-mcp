using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Editor;

public class McpToolSchemaGeneratorTests
{
	[Fact]
	public void Generate_MapsTypesAndRequiredParameters()
	{
		var method = typeof( SampleTools ).GetMethod(
			nameof( SampleTools.Sample ),
			BindingFlags.Public | BindingFlags.Static );

		using var document = JsonDocument.Parse(
			JsonSerializer.Serialize( McpToolSchemaGenerator.Generate( method ) ) );
		var properties = document.RootElement.GetProperty( "properties" );
		Assert.Equal( "number", properties.GetProperty( "count" ).GetProperty( "type" ).GetString() );
		Assert.Equal( "boolean", properties.GetProperty( "enabled" ).GetProperty( "type" ).GetString() );
		Assert.Equal( "string", properties.GetProperty( "name" ).GetProperty( "type" ).GetString() );
		Assert.Equal( "object", properties.GetProperty( "payload" ).GetProperty( "type" ).GetString() );

		var required = document.RootElement.GetProperty( "required" )
			.EnumerateArray()
			.Select( value => value.GetString() )
			.ToArray();
		Assert.Equal( new[] { "count", "enabled", "payload" }, required );
	}

	private static class SampleTools
	{
		public static void Sample(
			int count,
			bool enabled,
			SamplePayload payload,
			string name = "default" )
		{
		}
	}

	private sealed class SamplePayload
	{
	}
}
