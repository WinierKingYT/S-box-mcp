using Sandbox;
using System;
using System.IO;
using System.Linq;

namespace McpBridge;

public static class MemoryValidator
{
	private static string _lastModifiedPath;
	private static string _lastModifiedSnippet;
	private static string _lastModifiedContext;

	public static void RecordCodeModification( string path, string snippet, string context )
	{
		_lastModifiedPath = path;
		_lastModifiedSnippet = snippet;
		_lastModifiedContext = context;
	}

	public static void OnCompilationFinished( bool success, string buildOutput )
	{
		if ( string.IsNullOrEmpty( _lastModifiedPath ) ) return;

		if ( !success )
		{
			MemoryStore.Add( "antipattern", _lastModifiedPath, _lastModifiedSnippet, $"Compile Failure:\n{buildOutput}", _lastModifiedContext ?? "compile_failure" );
		}
	}

	public static void OnTestsFinished( bool allPassed, string testReport )
	{
		if ( string.IsNullOrEmpty( _lastModifiedPath ) ) return;

		if ( allPassed )
		{
			MemoryStore.Add( "pattern", _lastModifiedPath, _lastModifiedSnippet, $"Successful test run:\n{testReport}", _lastModifiedContext ?? "test_success" );
		}
		else
		{
			MemoryStore.Add( "antipattern", _lastModifiedPath, _lastModifiedSnippet, $"Test Failure:\n{testReport}", _lastModifiedContext ?? "test_failure" );
		}
	}
}
