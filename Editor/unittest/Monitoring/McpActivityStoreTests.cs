using Xunit;

namespace Editor;

public class McpActivityStoreTests
{
	[Fact]
	public void Add_RedactsApiKeyAndKeepsConfiguredLimit()
	{
		var config = new McpConfig
		{
			ApiKey = "top-secret",
			PersistHistory = false,
			HistoryLimit = 2
		};
		var store = new McpActivityStore( () => config );

		store.Add( "one", "ok", "top-secret first" );
		store.Add( "two", "ok", "second" );
		store.Add( "three", "ok", "third" );

		var entries = store.Snapshot();
		Assert.Equal( 2, entries.Count );
		Assert.Equal( "two", entries[0].Tool );
		Assert.DoesNotContain( entries, entry =>
			entry.Summary.Contains( "top-secret", StringComparison.Ordinal ) );
	}

	[Fact]
	public void Constructor_LoadsValidJsonlAndSkipsDamagedLines()
	{
		var directory = Path.Combine(
			Path.GetTempPath(),
			$"mcp-history-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );
		try
		{
			File.WriteAllLines(
				Path.Combine( directory, "activity-2026-07-26.jsonl" ),
				new[]
				{
					"""{"Timestamp":"2026-07-26T10:00:00+00:00","Tool":"valid","Status":"ok","Summary":"loaded","DurationMs":1,"Client":"Codex"}""",
					"{bad-json"
				} );
			var config = new McpConfig
			{
				PersistHistory = false,
				HistoryLimit = 10,
				HistoryRetentionDays = 3650
			};

			var store = new McpActivityStore( () => config, directory );

			var entry = Assert.Single( store.Snapshot() );
			Assert.Equal( "valid", entry.Tool );
		}
		finally
		{
			Directory.Delete( directory, recursive: true );
		}
	}
}
