using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor;

internal sealed record McpUndoOperationResult(
	bool Success,
	string Message,
	string Action = null );

internal interface IMcpSceneUndoService
{
	bool IsAvailable { get; }
	object RecordCreation( string actionName, Func<object> action );
	object RecordDeletion( string actionName, GameObject target, Func<object> action );
	object RecordComponentCreation( string actionName, Func<object> action );
	object RecordComponentDeletion(
		string actionName,
		Component target,
		Func<object> action );
	object RecordChanges(
		string actionName,
		GameObject target,
		IEnumerable<Component> components,
		Func<object> action,
		GameObjectUndoFlags flags = GameObjectUndoFlags.Properties );
	McpUndoOperationResult Undo();
	McpUndoOperationResult Redo();
}

internal sealed class SandboxMcpSceneUndoService : IMcpSceneUndoService
{
	private const string Prefix = "[MCP] ";
	[ThreadStatic] private static int _batchDepth;

	public bool IsAvailable => SceneEditorSession.Active != null;

	public object RecordCreation( string actionName, Func<object> action )
	{
		if ( _batchDepth > 0 ) return action();
		var session = RequireSession();
		using ( session.UndoScope( Prefix + actionName )
			.WithGameObjectCreations()
			.Push() )
		{
			return action();
		}
	}

	public object RecordDeletion(
		string actionName,
		GameObject target,
		Func<object> action )
	{
		if ( _batchDepth > 0 ) return action();
		var session = RequireSession();
		using ( session.UndoScope( Prefix + actionName )
			.WithGameObjectDestructions( target )
			.Push() )
		{
			return action();
		}
	}

	public object RecordComponentCreation( string actionName, Func<object> action )
	{
		if ( _batchDepth > 0 ) return action();
		var session = RequireSession();
		using ( session.UndoScope( Prefix + actionName )
			.WithComponentCreations()
			.Push() )
		{
			return action();
		}
	}

	public object RecordComponentDeletion(
		string actionName,
		Component target,
		Func<object> action )
	{
		if ( _batchDepth > 0 ) return action();
		var session = RequireSession();
		using ( session.UndoScope( Prefix + actionName )
			.WithComponentDestructions( target )
			.Push() )
		{
			return action();
		}
	}

	public object RecordChanges(
		string actionName,
		GameObject target,
		IEnumerable<Component> components,
		Func<object> action,
		GameObjectUndoFlags flags = GameObjectUndoFlags.Properties )
	{
		if ( _batchDepth > 0 ) return action();
		var session = RequireSession();
		var scope = session.UndoScope( Prefix + actionName )
			.WithGameObjectChanges( target, flags );
		var componentArray = components?.Where( component => component != null )
			.Distinct()
			.ToArray() ?? Array.Empty<Component>();
		if ( componentArray.Length > 0 )
			scope = scope.WithComponentChanges( componentArray );

		using ( scope.Push() )
		{
			return action();
		}
	}

	public McpUndoOperationResult Undo()
	{
		var session = SceneEditorSession.Active;
		if ( session == null )
			return Unavailable();
		if ( !session.UndoSystem.Back.TryPeek( out var operation ) )
			return new( false, "There is no scene change to undo" );
		if ( !IsMcpAction( operation.Name ) )
			return new(
				false,
				"The latest scene change was not made through MCP and was not undone",
				operation.Name );

		return session.UndoSystem.Undo()
			? new( true, $"Undid {operation.Name}", operation.Name )
			: new( false, $"Could not undo {operation.Name}", operation.Name );
	}

	public McpUndoOperationResult Redo()
	{
		var session = SceneEditorSession.Active;
		if ( session == null )
			return Unavailable();
		if ( !session.UndoSystem.Forward.TryPeek( out var operation ) )
			return new( false, "There is no scene change to redo" );
		if ( !IsMcpAction( operation.Name ) )
			return new(
				false,
				"The next scene change was not made through MCP and was not redone",
				operation.Name );

		return session.UndoSystem.Redo()
			? new( true, $"Redid {operation.Name}", operation.Name )
			: new( false, $"Could not redo {operation.Name}", operation.Name );
	}

	internal static bool IsMcpAction( string actionName )
		=> actionName?.StartsWith( Prefix, StringComparison.Ordinal ) == true;

	internal static IDisposable BeginBatch( string actionName )
	{
		var session = RequireSession();
		var gameObjects = Game.ActiveScene?.GetAllObjects( true ).ToArray()
			?? Array.Empty<GameObject>();
		var components = gameObjects.SelectMany( gameObject =>
				gameObject.Components.GetAll() )
			.ToArray();
		var scope = session.UndoScope( Prefix + actionName )
			.WithGameObjectCreations()
			.WithComponentCreations();
		if ( gameObjects.Length > 0 )
		{
			scope = scope
				.WithGameObjectChanges( gameObjects, GameObjectUndoFlags.All )
				.WithGameObjectDestructions( gameObjects );
		}
		if ( components.Length > 0 )
		{
			scope = scope
				.WithComponentChanges( components )
				.WithComponentDestructions( components );
		}
		_batchDepth++;
		return new BatchScope( scope.Push() );
	}

	private static SceneEditorSession RequireSession()
		=> SceneEditorSession.Active
			?? throw new InvalidOperationException(
				"No active scene editor session is available" );

	private static McpUndoOperationResult Unavailable()
		=> new( false, "No active scene editor session is available" );

	private sealed class BatchScope : IDisposable
	{
		private IDisposable _scope;

		public BatchScope( IDisposable scope )
		{
			_scope = scope;
		}

		public void Dispose()
		{
			if ( _scope == null ) return;
			try
			{
				_scope.Dispose();
			}
			finally
			{
				_scope = null;
				_batchDepth = Math.Max( 0, _batchDepth - 1 );
			}
		}
	}
}
