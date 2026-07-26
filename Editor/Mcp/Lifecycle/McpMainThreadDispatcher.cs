using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Editor;

internal sealed class McpMainThreadDispatcher
{
	private readonly ConcurrentQueue<DispatchWorkItem> _queue = new();

	public Task<object> InvokeAsync( Func<object> action, CancellationToken cancellationToken )
	{
		ArgumentNullException.ThrowIfNull( action );

		var completion = new TaskCompletionSource<object>(
			TaskCreationOptions.RunContinuationsAsynchronously );
		var workItem = new DispatchWorkItem( action, completion, cancellationToken );
		workItem.RegisterCancellation();
		_queue.Enqueue( workItem );
		return completion.Task;
	}

	public void Drain()
	{
		while ( _queue.TryDequeue( out var workItem ) )
			workItem.Execute();
	}

	private sealed class DispatchWorkItem
	{
		private readonly Func<object> _action;
		private readonly TaskCompletionSource<object> _completion;
		private readonly CancellationToken _cancellationToken;
		private CancellationTokenRegistration _registration;

		public DispatchWorkItem(
			Func<object> action,
			TaskCompletionSource<object> completion,
			CancellationToken cancellationToken )
		{
			_action = action;
			_completion = completion;
			_cancellationToken = cancellationToken;
		}

		public void RegisterCancellation()
		{
			if ( !_cancellationToken.CanBeCanceled ) return;
			_registration = _cancellationToken.Register(
				() => _completion.TrySetCanceled( _cancellationToken ) );
		}

		public void Execute()
		{
			try
			{
				if ( _cancellationToken.IsCancellationRequested ) return;
				_completion.TrySetResult( _action() );
			}
			catch ( Exception exception )
			{
				_completion.TrySetException( exception );
			}
			finally
			{
				_registration.Dispose();
			}
		}
	}
}
