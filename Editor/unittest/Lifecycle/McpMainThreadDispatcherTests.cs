using Xunit;

namespace Editor;

public class McpMainThreadDispatcherTests
{
	[Fact]
	public async Task InvokeAsync_WaitsUntilDrain()
	{
		var dispatcher = new McpMainThreadDispatcher();
		var task = dispatcher.InvokeAsync( () => 42, CancellationToken.None );

		Assert.False( task.IsCompleted );
		dispatcher.Drain();
		Assert.Equal( 42, await task );
	}

	[Fact]
	public async Task CancelledWorkDoesNotExecute()
	{
		var dispatcher = new McpMainThreadDispatcher();
		var executed = false;
		using var cancellation = new CancellationTokenSource();
		var task = dispatcher.InvokeAsync(
			() =>
			{
				executed = true;
				return null;
			},
			cancellation.Token );
		cancellation.Cancel();
		dispatcher.Drain();

		await Assert.ThrowsAnyAsync<OperationCanceledException>( async () => await task );
		Assert.False( executed );
	}

	[Fact]
	public async Task HandlerExceptionPropagates()
	{
		var dispatcher = new McpMainThreadDispatcher();
		var task = dispatcher.InvokeAsync(
			() => throw new InvalidOperationException( "boom" ),
			CancellationToken.None );
		dispatcher.Drain();

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			async () => await task );
		Assert.Equal( "boom", exception.Message );
	}
}
