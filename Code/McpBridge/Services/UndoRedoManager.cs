using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge;

public class UndoEntry
{
	public string Action { get; set; }
	public string Description { get; set; }
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;

	public Func<object> Undo { get; set; }
	public Func<object> Redo { get; set; }
}

public static class UndoRedoManager
{
	private static readonly List<UndoEntry> _undoStack = new();
	private static readonly List<UndoEntry> _redoStack = new();
	private const int MaxUndo = 100;

	public static void Record( string action, string description, Func<object> undoFn, Func<object> redoFn )
	{
		_undoStack.Add( new UndoEntry
		{
			Action = action,
			Description = description,
			Undo = undoFn,
			Redo = redoFn
		} );

		if ( _undoStack.Count > MaxUndo )
			_undoStack.RemoveAt( 0 );

		_redoStack.Clear();
	}

	public static object Undo()
	{
		if ( _undoStack.Count == 0 )
			return new { error = "Nothing to undo" };

		var entry = _undoStack[^1];
		_undoStack.RemoveAt( _undoStack.Count - 1 );

		var result = entry.Undo?.Invoke();

		_redoStack.Add( entry );

		return new { success = true, action = entry.Action, description = entry.Description, result };
	}

	public static object Redo()
	{
		if ( _redoStack.Count == 0 )
			return new { error = "Nothing to redo" };

		var entry = _redoStack[^1];
		_redoStack.RemoveAt( _redoStack.Count - 1 );

		var result = entry.Redo?.Invoke();

		_undoStack.Add( entry );

		return new { success = true, action = entry.Action, description = entry.Description, result };
	}

	public static List<object> GetHistory()
	{
		return _undoStack.Select( e => new
		{
			action = e.Action,
			description = e.Description,
			timestamp = e.Timestamp
		} as object ).ToList();
	}

	public static void Clear()
	{
		_undoStack.Clear();
		_redoStack.Clear();
	}
}
