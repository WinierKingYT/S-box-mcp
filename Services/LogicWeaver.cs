using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge;

public class LogicRule
{
	public string Id { get; set; }
	public string TriggerType { get; set; }
	public string SourceGuid { get; set; }
	public string ActionType { get; set; }
	public string TargetGuid { get; set; }
	public string ActionParams { get; set; }
	public bool Enabled { get; set; } = true;
}

public static class LogicWeaver
{
	private static readonly Dictionary<string, LogicRule> _rules = new();
	private static int _nextId;
	private static readonly string StoragePath = "logic_rules.json";

	static LogicWeaver()
	{
		var rules = PersistenceStore.Load<List<LogicRule>>( StoragePath );
		if ( rules != null )
		{
			foreach ( var r in rules )
			{
				_rules[r.Id] = r;
				var idNum = int.Parse( r.Id.Replace( "rule_", "" ) );
				if ( idNum > _nextId ) _nextId = idNum;
			}
		}
	}

	public static string AddRule( LogicRule rule )
	{
		var id = $"rule_{++_nextId}";
		rule.Id = id;
		_rules[id] = rule;
		Log.Info( $"[LogicWeaver] Rule added: {id} ({rule.TriggerType} -> {rule.ActionType})" );
		Save();
		return id;
	}

	public static bool RemoveRule( string id )
	{
		var ok = _rules.Remove( id );
		if ( ok ) Save();
		return ok;
	}

	public static LogicRule GetRule( string id )
	{
		_rules.TryGetValue( id, out var rule );
		return rule;
	}

	public static List<LogicRule> ListRules()
	{
		return _rules.Values.ToList();
	}

	public static void Clear()
	{
		_rules.Clear();
		Save();
	}

	public static void Fire( string triggerType, string sourceGuid )
	{
		foreach ( var rule in _rules.Values )
		{
			if ( !rule.Enabled ) continue;
			if ( rule.TriggerType != triggerType ) continue;
			if ( rule.SourceGuid != sourceGuid ) continue;

			ExecuteAction( rule );
		}
	}

	private static void Save()
	{
		PersistenceStore.Save( StoragePath, _rules.Values.ToList() );
	}

	private static void ExecuteAction( LogicRule rule )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return;

		if ( !Guid.TryParse( rule.TargetGuid, out var targetGuid ) ) return;
		var target = scene.Directory.FindByGuid( targetGuid );
		if ( !target.IsValid() ) return;

		switch ( rule.ActionType )
		{
			case "destroy":
				target.Destroy();
				Log.Info( $"[LogicWeaver] Destroyed {target.Name}" );
				break;

			case "toggle_enabled":
				target.Enabled = !target.Enabled;
				Log.Info( $"[LogicWeaver] Toggled {target.Name} enabled={target.Enabled}" );
				break;

			case "set_color":
				SetTintColor( target, rule.ActionParams );
				break;

			default:
				Log.Warning( $"[LogicWeaver] Unknown action type: {rule.ActionType}" );
				break;
		}
	}

	private static void SetTintColor( GameObject go, string colorStr )
	{
		if ( string.IsNullOrEmpty( colorStr ) ) return;

		var parts = colorStr.Split( ',' );
		if ( parts.Length < 3 ) return;

		if ( float.TryParse( parts[0], out var r ) &&
			float.TryParse( parts[1], out var g ) &&
			float.TryParse( parts[2], out var b ) )
		{
			var color = new Color( r, g, b, parts.Length > 3 && float.TryParse( parts[3], out var a ) ? a : 1f );
			var renderer = go.Components.Get<ModelRenderer>();
			if ( renderer.IsValid() )
				renderer.Tint = color;
		}
	}
}
