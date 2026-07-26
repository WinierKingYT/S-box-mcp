using Xunit;

namespace Editor;

public class McpRateLimiterTests
{
	[Fact]
	public void TryAcquire_EnforcesConfiguredLimit()
	{
		var clock = new FakeTimeProvider();
		var limiter = new McpRateLimiter( () => 2, clock );

		Assert.True( limiter.TryAcquire() );
		Assert.True( limiter.TryAcquire() );
		Assert.False( limiter.TryAcquire() );
	}

	[Fact]
	public void TryAcquire_ResetsAfterOneSecond()
	{
		var clock = new FakeTimeProvider();
		var limiter = new McpRateLimiter( () => 1, clock );
		Assert.True( limiter.TryAcquire() );
		Assert.False( limiter.TryAcquire() );

		clock.Advance( TimeSpan.FromSeconds( 1 ) );
		Assert.True( limiter.TryAcquire() );
	}

	[Theory]
	[InlineData( 0 )]
	[InlineData( -1 )]
	public void TryAcquire_NonPositiveLimitDisablesLimiter( int limit )
	{
		var limiter = new McpRateLimiter( () => limit );
		for ( var index = 0; index < 100; index++ )
			Assert.True( limiter.TryAcquire() );
	}

	private sealed class FakeTimeProvider : TimeProvider
	{
		private DateTimeOffset _utcNow = DateTimeOffset.Parse( "2026-01-01T00:00:00Z" );

		public override DateTimeOffset GetUtcNow()
			=> _utcNow;

		public void Advance( TimeSpan duration )
			=> _utcNow += duration;
	}
}
