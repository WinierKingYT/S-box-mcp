using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge;

public static class McpLogBridge
{
	public static event Action<string, string, string> OnLogMessage;
	public static bool IsEnabled { get; private set; }

	public static void Enable()
	{
		if ( IsEnabled ) return;
		IsEnabled = true;
		Capture( "info", "[MCP] Log bridge enabled" );
	}

	public static void Disable()
	{
		if ( !IsEnabled ) return;
		IsEnabled = false;
		Capture( "info", "[MCP] Log bridge disabled" );
	}

	public static void Forward( string level, string source, string message )
	{
		if ( !IsEnabled ) return;
		OnLogMessage?.Invoke( level, source, message );
	}

	private static readonly List<LogEntry> _logHistory = new();
	private static readonly object _logLock = new();
	private const int MaxHistory = 1000;

	public static void Capture( string level, string message )
	{
		var entry = new LogEntry { Level = level, Message = message, Time = DateTime.UtcNow };
		lock ( _logLock )
		{
			_logHistory.Add( entry );
			if ( _logHistory.Count > MaxHistory )
				_logHistory.RemoveRange( 0, _logHistory.Count - MaxHistory );
		}
	}

	public static void CaptureLog( string level, string message )
	{
		if ( !IsEnabled ) return;
		Capture( level, message );
	}

	public static List<LogEntry> GetRecent( int count = 50, string minLevel = null, string search = null )
	{
		lock ( _logLock )
		{
			var levels = new[] { "debug", "info", "notice", "warning", "error", "critical" };
			var minIdx = minLevel != null ? Array.IndexOf( levels, minLevel ) : 0;
			var filtered = _logHistory.AsEnumerable();
			if ( minIdx > 0 )
				filtered = filtered.Where( e => Array.IndexOf( levels, e.Level ) >= minIdx );
			if ( !string.IsNullOrEmpty( search ) )
				filtered = filtered.Where( e => e.Message.IndexOf( search, StringComparison.OrdinalIgnoreCase ) >= 0 );
			var list = filtered.ToList();
			var start = Math.Max( 0, list.Count - count );
			return list.GetRange( start, list.Count - start );
		}
	}

	public static void Clear()
	{
		lock ( _logLock )
		{
			_logHistory.Clear();
		}
	}

	public static int Count
	{
		get { lock ( _logLock ) { return _logHistory.Count; } }
	}
}

public class LogEntry
{
	public string Level { get; set; }
	public string Message { get; set; }
	public DateTime Time { get; set; }
}
