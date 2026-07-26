using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Editor;

internal sealed class McpToolExecutor
{
	private readonly McpMainThreadDispatcher _dispatcher;
	private readonly Func<int> _timeoutProvider;
	private readonly McpApprovalService _approvals;
	private readonly McpActivityStore _activity;

	public McpToolExecutor( McpMainThreadDispatcher dispatcher, Func<int> timeoutProvider )
		: this( dispatcher, timeoutProvider, null, null )
	{
	}

	public McpToolExecutor(
		McpMainThreadDispatcher dispatcher,
		Func<int> timeoutProvider,
		McpApprovalService approvals,
		McpActivityStore activity )
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException( nameof( dispatcher ) );
		_timeoutProvider = timeoutProvider ?? throw new ArgumentNullException( nameof( timeoutProvider ) );
		_approvals = approvals;
		_activity = activity;
	}

	public async Task<object> ExecuteAsync(
		string name,
		McpToolDefinition tool,
		JsonElement arguments )
	{
		var timeoutMs = Math.Max( 1, _timeoutProvider() );
		using var timeout = new CancellationTokenSource( timeoutMs );

		try
		{
			var stopwatch = System.Diagnostics.Stopwatch.StartNew();
			try
			{
				var result = await _dispatcher.InvokeAsync(
					() => _approvals?.RequiresApproval( tool ) == true
						? _approvals.SubmitSingle( tool, arguments )
						: tool.Handler( arguments ),
					timeout.Token );
				_activity?.Add(
					name,
					_approvals?.RequiresApproval( tool ) == true ? "pending" : "succeeded",
					tool.Description,
					stopwatch.ElapsedMilliseconds );
				return result;
			}
			catch ( Exception exception )
			{
				_activity?.Add(
					name,
					"failed",
					tool.Description,
					stopwatch.ElapsedMilliseconds,
					exception );
				throw;
			}
		}
		catch ( OperationCanceledException ) when ( timeout.IsCancellationRequested )
		{
			throw new TimeoutException( $"Tool '{name}' timed out after {timeoutMs} ms" );
		}
	}
}
