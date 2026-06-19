using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge.Testing;

public static class TestScenarioManager
{
	private static readonly Dictionary<string, TestScenario> _scenarios = new();
	private static readonly string StoragePath = "test_scenarios.json";

	static TestScenarioManager()
	{
		var list = PersistenceStore.Load<List<TestScenario>>( StoragePath );
		if ( list != null )
			foreach ( var s in list )
				_scenarios[s.Name] = s;
	}

	public static void Set( TestScenario scenario )
	{
		_scenarios[scenario.Name] = scenario;
		Save();
	}

	public static TestScenario Get( string name )
	{
		_scenarios.TryGetValue( name, out var scenario );
		return scenario;
	}

	public static void Delete( string name )
	{
		_scenarios.Remove( name );
		Save();
	}

	public static List<TestScenario> List()
	{
		return _scenarios.Values.ToList();
	}

	public static void Clear()
	{
		_scenarios.Clear();
		Save();
	}

	private static void Save()
	{
		PersistenceStore.Save( StoragePath, _scenarios.Values.ToList() );
	}
}
