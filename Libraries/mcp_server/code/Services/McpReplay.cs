using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace McpBridge;

public static class McpReplay
{
	private static readonly List<ReplayRecord> _history = new();
	private static readonly List<ReplayRecord> _playbackQueue = new();
	private static readonly object _lock = new();
	private static bool _isPlaying;
	private const int MaxHistory = 500;

	public static void Record( string method, string input, string output, long durationMs, bool success = true )
	{
		lock ( _lock )
		{
			_history.Add( new ReplayRecord
			{
				Method = method,
				Input = input,
				Output = output,
				DurationMs = durationMs,
				Timestamp = DateTime.UtcNow,
				Success = success
			} );
			if ( _history.Count > MaxHistory )
				_history.RemoveAt( 0 );
		}
	}

	public static void Clear()
	{
		lock ( _lock )
		{
			_history.Clear();
			_playbackQueue.Clear();
			_isPlaying = false;
		}
	}

	public static List<ReplayRecord> GetHistory( int count = 50 )
	{
		lock ( _lock )
			return _history.TakeLast( count ).ToList();
	}

	public static void StartPlayback()
	{
		lock ( _lock )
		{
			_playbackQueue.Clear();
			_playbackQueue.AddRange( _history );
			_isPlaying = true;
		}
	}

	public static ReplayRecord NextPlayback()
	{
		lock ( _lock )
		{
			if ( !_isPlaying || _playbackQueue.Count == 0 )
				return null;

			var record = _playbackQueue[0];
			_playbackQueue.RemoveAt( 0 );
			return record;
		}
	}

	public static void StopPlayback()
	{
		lock ( _lock )
		{
			_isPlaying = false;
			_playbackQueue.Clear();
		}
	}

	public static bool ExportAsScript( string path )
	{
		List<ReplayRecord> snapshot;
		lock ( _lock )
			snapshot = _history.ToList();

		try
		{
			var lines = new List<string>();
			lines.Add( "// MCP Replay Script" );
			lines.Add( $"// Exported: {DateTime.UtcNow:O}" );
			lines.Add( $"// Records: {snapshot.Count}" );
			lines.Add( "" );

			foreach ( var record in snapshot )
			{
				lines.Add( $"// {record.Method} ({record.DurationMs}ms)" );
				lines.Add( $"{record.Method} {record.Input}" );
				lines.Add( "" );
			}

			FileSystem.Data.WriteAllText( path, string.Join( "\n", lines ) );
			return true;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[MCP] Export failed: {e.Message}" );
			return false;
		}
	}

	public static Dictionary<string, object> GetAnalytics()
	{
		List<ReplayRecord> snapshot;
		lock ( _lock )
			snapshot = _history.ToList();

		var byMethod = snapshot.GroupBy( r => r.Method )
			.ToDictionary( g => g.Key, g => new
			{
				count = g.Count(),
				avgDurationMs = Math.Round( g.Average( r => r.DurationMs ), 1 ),
				minDurationMs = g.Min( r => r.DurationMs ),
				maxDurationMs = g.Max( r => r.DurationMs ),
				errorCount = g.Count( r => !r.Success )
			} );

		return new()
		{
			["totalCalls"] = snapshot.Count,
			["uniqueMethods"] = byMethod.Count,
			["errorCount"] = snapshot.Count( r => !r.Success ),
			["byMethod"] = byMethod,
			["avgDurationMs"] = snapshot.Count > 0 ? Math.Round( snapshot.Average( r => r.DurationMs ), 1 ) : 0,
			["totalDurationMs"] = snapshot.Sum( r => r.DurationMs )
		};
	}
}
