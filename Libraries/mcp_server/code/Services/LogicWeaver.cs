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

			case "play_sound":
				if ( !string.IsNullOrEmpty( rule.ActionParams ) )
				{
					Sound.Play( rule.ActionParams, target.WorldPosition );
					Log.Info( $"[LogicWeaver] Played sound {rule.ActionParams} at {target.Name}" );
				}
				break;

			case "emit_particle":
				if ( !string.IsNullOrEmpty( rule.ActionParams ) )
				{
					var particlesType = TypeLibrary.GetType( "Sandbox.Particles" );
					if ( particlesType != null )
					{
						var createMethod = particlesType.Methods.FirstOrDefault( m => m.Name == "Create" );
						if ( createMethod != null )
						{
							createMethod.Invoke( null, new object[] { rule.ActionParams, target.WorldPosition } );
							Log.Info( $"[LogicWeaver] Emitted particle {rule.ActionParams} at {target.Name}" );
							break;
						}
					}
					var particleSysType = TypeLibrary.GetType( "Sandbox.ParticleSystem" );
					if ( particleSysType != null )
					{
						var go = new GameObject( true, "_weaver_particle_temp" );
						go.WorldPosition = target.WorldPosition;
						var comp = go.Components.Create( particleSysType );
						if ( comp.IsValid() )
						{
							Log.Info( $"[LogicWeaver] Created ParticleSystem component for {rule.ActionParams} at {target.Name}" );
						}
					}
				}
				break;

			case "apply_force":
				if ( !string.IsNullOrEmpty( rule.ActionParams ) )
				{
					var parts = rule.ActionParams.Split( ',' );
					if ( parts.Length == 3 && float.TryParse( parts[0], out var fx ) && float.TryParse( parts[1], out var fy ) && float.TryParse( parts[2], out var fz ) )
					{
						var rb = target.Components.Get<Rigidbody>();
						if ( rb.IsValid() )
						{
							rb.ApplyImpulse( new Vector3( fx, fy, fz ) );
							Log.Info( $"[LogicWeaver] Applied physics impulse ({fx},{fy},{fz}) to {target.Name}" );
						}
					}
				}
				break;

			case "add_nitro":
				var controller = target.Components.GetAll<Component>().FirstOrDefault( c => c.GetType().Name == "ShoppingCartController" );
				if ( controller.IsValid() )
				{
					var typeDesc = TypeLibrary.GetType( controller.GetType() );
					if ( typeDesc != null )
					{
						var prop = typeDesc.Properties.FirstOrDefault( p => p.Name == "HasNitro" && p.CanWrite );
						if ( prop != null )
						{
							prop.SetValue( controller, true );
							Log.Info( $"[LogicWeaver] Granted nitro to shopping cart on {target.Name}" );
						}
					}
				}
				break;

			case "set_property":
				if ( !string.IsNullOrEmpty( rule.ActionParams ) )
				{
					var parts = rule.ActionParams.Split( ';' );
					if ( parts.Length >= 3 )
					{
						var componentType = parts[0];
						var propertyName = parts[1];
						var valStr = parts[2];
						var typeDesc = TypeLibrary.GetType( componentType );
						if ( typeDesc != null )
						{
							var comp = target.Components.Get( typeDesc.TargetType );
							if ( comp.IsValid() )
							{
								var prop = typeDesc.Properties.FirstOrDefault( p => p.Name == propertyName && p.CanWrite );
								if ( prop != null )
								{
									try
									{
										var converted = ConvertValue( valStr, prop.PropertyType );
										prop.SetValue( comp, converted );
										Log.Info( $"[LogicWeaver] Set property {propertyName} on {componentType} of {target.Name} to {valStr}" );
									}
									catch ( Exception ex )
									{
										Log.Warning( $"[LogicWeaver] Failed to set property: {ex.Message}" );
									}
								}
							}
						}
					}
				}
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

	private static object ConvertValue( string str, Type targetType )
	{
		if ( targetType == typeof( string ) ) return str;
		if ( targetType == typeof( int ) ) return int.Parse( str );
		if ( targetType == typeof( float ) ) return float.Parse( str );
		if ( targetType == typeof( bool ) ) return bool.Parse( str );
		if ( targetType == typeof( Vector3 ) )
		{
			var p = str.Split( ',' );
			return new Vector3( float.Parse( p[0] ), float.Parse( p[1] ), float.Parse( p[2] ) );
		}
		if ( TypeLibrary.GetType( targetType )?.IsEnum == true )
			return Enum.Parse( targetType, str );
		if ( targetType == typeof( Angles ) )
		{
			var p = str.Split( ',' );
			return new Angles( float.Parse( p[0] ), float.Parse( p[1] ), float.Parse( p[2] ) );
		}
		return Convert.ChangeType( str, targetType );
	}
}
