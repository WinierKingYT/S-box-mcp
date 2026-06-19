using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace McpBridge.Middleware;

public sealed class PipelineBuilder
{
	private readonly List<Func<IMiddleware>> _middlewareFactories = new();

	public PipelineBuilder Use<T>() where T : IMiddleware, new()
	{
		_middlewareFactories.Add( () => new T() );
		return this;
	}

	public PipelineBuilder Use( Func<IMiddleware> factory )
	{
		_middlewareFactories.Add( factory );
		return this;
	}

	public MiddlewareDelegate Build( MiddlewareDelegate terminal )
	{
		MiddlewareDelegate pipeline = terminal;
		for ( int i = _middlewareFactories.Count - 1; i >= 0; i-- )
		{
			var middleware = _middlewareFactories[i]();
			var next = pipeline;
			pipeline = () => middleware.InvokeAsync( new McpContext(), next );
		}
		return pipeline;
	}

	public Func<McpContext, Task> BuildContext( Func<McpContext, Task> terminal )
	{
		Func<McpContext, Task> pipeline = terminal;
		for ( int i = _middlewareFactories.Count - 1; i >= 0; i-- )
		{
			var factory = _middlewareFactories[i];
			var next = pipeline;
			pipeline = ctx =>
			{
				var middleware = factory();
				return middleware.InvokeAsync( ctx, () => next( ctx ) );
			};
		}
		return pipeline;
	}
}
