using Sandbox;
using System;
using System.Collections.Generic;

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

	public static List<LogEntry> GetRecentLogs( int count = 50 )
	{
		return GetRecent( count );
	}

	private static readonly List<LogEntry> _logHistory = new();
	private static readonly object _logLock = new();
	private const int MaxHistory = 500;

	public static void Capture( string level, string message )
	{
		var entry = new LogEntry { Level = level, Message = message, Time = DateTime.UtcNow };
		lock ( _logLock )
		{
			_logHistory.Add( entry );
			if ( _logHistory.Count > MaxHistory )
				_logHistory.RemoveAt( 0 );
		}
	}

	public static void CaptureLog( string level, string message )
	{
		if ( !IsEnabled ) return;
		Capture( level, message );
	}

	public static List<LogEntry> GetRecent( int count = 50 )
	{
		lock ( _logLock )
		{
			var start = Math.Max( 0, _logHistory.Count - count );
			return _logHistory.GetRange( start, _logHistory.Count - start );
		}
	}
}

public class LogEntry
{
	public string Level { get; set; }
	public string Message { get; set; }
	public DateTime Time { get; set; }
}
