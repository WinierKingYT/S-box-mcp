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
	public object RunSingle( string method, string paramsJson ) => Registry.RunSingle( method, paramsJson );
	public string GetToolsJson()
	{
		var result = new List<object>();
		foreach ( var t in Registry.ListAll() )
		{
			var schema = t.Method != null
				? Execution.SchemaGenerator.Generate( t.Method )
				: (object)new { type = "object", properties = new Dictionary<string, object>() };
			var entry = new Dictionary<string, object>
			{
				["name"] = t.Name,
				["description"] = t.Description,
				["group"] = t.Group,
				["inputSchema"] = schema
			};
			if ( t.Annotations != null )
				entry["annotations"] = t.Annotations;
			result.Add( entry );
		}
		return JsonSerializer.Serialize( result );
	}

	public string HealthSummary()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return "{}";

		object phase = null;
		int day = 0;
		double timeRemaining = 0;
		double personalCash = 0;
		string alarmLevel = "None";

		var gm = scene.GetAllComponents<Component>().FirstOrDefault( c => c.GetType().Name == "BlackFridayGameManager" );
		if ( gm != null )
		{
			var typeDesc = TypeLibrary.GetType( gm.GetType() );
			if ( typeDesc != null )
			{
				phase = typeDesc.Properties.FirstOrDefault( p => p.Name == "CurrentPhase" )?.GetValue( gm )?.ToString();
				var d = typeDesc.Properties.FirstOrDefault( p => p.Name == "CurrentDay" )?.GetValue( gm );
				if ( d is int valInt ) day = valInt;
				var rem = typeDesc.Properties.FirstOrDefault( p => p.Name == "PhaseTimeRemaining" )?.GetValue( gm );
				if ( rem is float valFloat ) timeRemaining = Math.Round( valFloat, 1 );
			}
		}

		var quota = scene.GetAllComponents<Component>().FirstOrDefault( c => c.GetType().Name == "QuotaManager" );
		if ( quota != null )
		{
			var typeDesc = TypeLibrary.GetType( quota.GetType() );
			if ( typeDesc != null )
			{
				var cash = typeDesc.Properties.FirstOrDefault( p => p.Name == "MyPersonalCash" )?.GetValue( quota );
				if ( cash is float valFloat ) personalCash = Math.Round( valFloat, 1 );
			}
		}

		var alarm = scene.GetAllComponents<Component>().FirstOrDefault( c => c.GetType().Name == "AlarmSystem" );
		if ( alarm != null )
		{
			var typeDesc = TypeLibrary.GetType( alarm.GetType() );
			if ( typeDesc != null )
			{
				alarmLevel = typeDesc.Methods.FirstOrDefault( m => m.Name == "GetAlarmLevelName" )?.Invoke( alarm, null )?.ToString() ?? "None";
			}
		}

		return JsonSerializer.Serialize( new
		{
			phase,
			day,
			timeRemaining,
			personalCash,
			alarmLevel
		} );
	}

	public async Task<string> Execute( string method, string argsJson )
	{
		EnsureInitialized();
		var meta = new Dictionary<string, object>();
		var pt = McpToolBridge.CurrentProgressToken;
		if ( !string.IsNullOrEmpty( pt ) )
			meta["progressToken"] = pt;
		var json = JsonSerializer.Serialize( new { jsonrpc = "2.0", method, @params = JsonDocument.Parse( argsJson ?? "{}" ).RootElement, _meta = meta.Count > 0 ? meta : null, id = 1 } );
		var result = await _runner.ExecuteAsync( json );
		return result?.response;
	}
}
