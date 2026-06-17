using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace McpBridge;

public sealed class McpToolDispatcher
{
	private static readonly Lazy<McpToolDispatcher> _instance = new( () => new McpToolDispatcher() );
	public static McpToolDispatcher Instance => _instance.Value;

	private readonly Execution.ToolRunner _runner;
	private readonly Execution.SchemaGenerator _schema;
	private readonly DateTime _startTime = DateTime.UtcNow;

	private McpToolDispatcher()
	{
		var registry = new Execution.ToolRegistry();
		_schema = new Execution.SchemaGenerator();
		var rateLimiter = new Middleware.RateLimiter();
		_runner = new Execution.ToolRunner( registry, _schema, rateLimiter );
	}

	public long TotalCalls => _runner.TotalCalls;
	public DateTime StartTime => _startTime;
	public Execution.ToolRegistry Registry => _runner.Registry;

	public void EnsureInitialized() => Registry.EnsureInitialized();
	public object RunSingle( string method, string paramsJson, string sessionId = null ) => Registry.RunSingle( method, paramsJson );
	public string GetToolsJson()
	{
		var result = new List<object>();
		var dict = Registry.GetToolsDict();
		foreach ( var t in Registry.ListAll() )
		{
			if ( dict.TryGetValue( t.name, out var mdesc ) )
				result.Add( new { name = t.name, description = t.description, group = t.group, inputSchema = Execution.SchemaGenerator.Generate( mdesc ) } );
			else
				result.Add( new { name = t.name, description = t.description, group = t.group, inputSchema = new { type = "object", properties = new Dictionary<string, object>() } } );
		}
		return JsonSerializer.Serialize( result );
	}

	public string HealthSummary()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return "{}";
		var gm = scene.GetAllComponents<BlackFridayGameManager>().FirstOrDefault();
		var quota = scene.GetAllComponents<QuotaManager>().FirstOrDefault();
		var alarm = scene.GetAllComponents<AlarmSystem>().FirstOrDefault();
		return JsonSerializer.Serialize( new
		{
			phase = gm?.CurrentPhase.ToString(),
			day = gm?.CurrentDay ?? 0,
			timeRemaining = gm != null ? Math.Round( gm.PhaseTimeRemaining, 1 ) : 0,
			personalCash = quota != null ? Math.Round( quota.MyPersonalCash, 1 ) : 0,
			alarmLevel = alarm?.GetAlarmLevelName() ?? "None"
		} );
	}

	public async Task<string> Execute( string method, string argsJson )
	{
		EnsureInitialized();
		var json = $"{{\"jsonrpc\":\"2.0\",\"method\":\"{method}\",\"params\":{argsJson},\"id\":1}}";
		var result = await _runner.ExecuteAsync( json );
		return result?.response;
	}
}
