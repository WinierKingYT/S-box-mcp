using McpBridge;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge.Tools;

[McpToolGroup("Scene")]
public class SceneTools
{
	[McpTool("sbox_find_lights", "Lists all light components in the scene.", ReadOnlyHint = true)]
	public object FindLights()
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		var lights = new List<object>();
		foreach ( var dl in scene.GetAllComponents<DirectionalLight>() ) if ( dl.IsValid() ) lights.Add( new { guid = dl.GameObject.Id, name = dl.GameObject.Name, type = "DirectionalLight", color = new { r = dl.LightColor.r, g = dl.LightColor.g, b = dl.LightColor.b }, shadows = dl.Shadows } );
		foreach ( var pl in scene.GetAllComponents<PointLight>() ) if ( pl.IsValid() ) lights.Add( new { guid = pl.GameObject.Id, name = pl.GameObject.Name, type = "PointLight", color = new { r = pl.LightColor.r, g = pl.LightColor.g, b = pl.LightColor.b }, radius = pl.Radius, shadows = pl.Shadows } );
		return new { lights };
	}

	[McpTool("sbox_set_light_color", "Sets color and optional properties on a light.", OptionalParams = new[]{"shadows", "shadowHardness", "fogStrength"}, DestructiveHint = true)]
	public object SetLightColor( string guidStr, string color, string shadows = null, string shadowHardness = null, string fogStrength = null )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var parts = color.Split( ',' ); if ( parts.Length < 3 || !float.TryParse( parts[0], out var r ) || !float.TryParse( parts[1], out var g ) || !float.TryParse( parts[2], out var b ) ) return new { error = "Invalid color" };
		var newColor = new Color( r, g, b );
		var dl = go.Components.Get<DirectionalLight>();
		if ( dl.IsValid() ) { dl.LightColor = newColor; if ( shadows != null && bool.TryParse( shadows, out var sv ) ) dl.Shadows = sv; if ( shadowHardness != null && float.TryParse( shadowHardness, out var sh ) ) dl.ShadowHardness = sh; if ( fogStrength != null && float.TryParse( fogStrength, out var fs ) ) dl.FogStrength = fs; return new { success = true, type = "DirectionalLight" }; }
		var pl = go.Components.Get<PointLight>();
		if ( pl.IsValid() ) { pl.LightColor = newColor; if ( shadows != null && bool.TryParse( shadows, out var sv ) ) pl.Shadows = sv; return new { success = true, type = "PointLight" }; }
		return new { error = "No light component found" };
	}

	[McpTool("sbox_set_fog", "Controls fog settings on the scene's DirectionalLight.", OptionalParams = new[]{"mode", "strength", "lightGuid"}, DestructiveHint = true)]
	public object SetFog( string mode = null, string strength = null, string lightGuid = null )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		var light = FindDirectionalLight( scene, lightGuid ); if ( light == null ) return new { error = "No DirectionalLight found" };
		if ( mode != null ) { try { var fogType = TypeLibrary.GetType( "Sandbox.FogMode" ); var lightType = TypeLibrary.GetType( typeof( DirectionalLight ) ); if ( fogType != null && lightType != null ) { var fp = lightType.Properties.FirstOrDefault( p => p.Name == "FogMode" && p.CanWrite ); if ( fp != null ) fp.SetValue( light, Enum.Parse( fogType.TargetType, mode, ignoreCase: true ) ); } } catch { return new { error = "Invalid fog mode" }; } }
		if ( strength != null && float.TryParse( strength, out var fs ) ) light.FogStrength = fs;
		return new { success = true, fogMode = light.FogMode.ToString(), fogStrength = light.FogStrength };
	}

	[McpTool("sbox_set_background", "Changes the main camera background color.", OptionalParams = new[]{"clearFlags"}, DestructiveHint = true)]
	public object SetBackground( string color, string clearFlags = null )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		var cam = scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c.IsMainCamera ); if ( cam == null ) return new { error = "No main camera" };
		var parts = color.Split( ',' ); if ( parts.Length < 3 || !float.TryParse( parts[0], out var r ) || !float.TryParse( parts[1], out var g ) || !float.TryParse( parts[2], out var b ) ) return new { error = "Invalid color" };
		cam.BackgroundColor = new Color( r, g, b );
		if ( clearFlags != null ) { var f = clearFlags.ToLowerInvariant(); if ( f == "all" ) cam.ClearFlags = ClearFlags.All; else if ( f == "color" ) cam.ClearFlags = ClearFlags.Color; else if ( f == "depth" ) cam.ClearFlags = ClearFlags.Depth; else if ( f == "none" ) cam.ClearFlags = ClearFlags.None; }
		return new { success = true, backgroundColor = cam.BackgroundColor.ToString(), clearFlags = cam.ClearFlags.ToString() };
	}

	[McpTool("sbox_set_exposure", "Set camera exposure compensation (EV, range -6 to +6).", OptionalParams = new[]{"autoExposure"}, DestructiveHint = true)]
	public object SetExposure( string ev100, bool autoExposure = false )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		var cam = scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c.IsMainCamera ); if ( cam == null ) return new { error = "No main camera" };
		if ( !float.TryParse( ev100, out var ev ) ) return new { error = "Invalid EV value, expected a number" };
		var clamped = Validation.Clamp( ev, -6f, 6f );
		try
		{
			var td = TypeLibrary.GetType( typeof( CameraComponent ) );
			var pp = td?.Properties.FirstOrDefault( p => p.Name == "ExposureCompensation" && p.CanWrite );
			if ( pp != null ) pp.SetValue( cam, clamped );
		}
		catch { return new { error = "Exposure property not accessible" }; }
		try
		{
			var td = TypeLibrary.GetType( typeof( CameraComponent ) );
			var pp = td?.Properties.FirstOrDefault( p => p.Name == "AutoExposure" && p.CanWrite );
			if ( pp != null ) pp.SetValue( cam, autoExposure );
		}
		catch { }
		var warning = Math.Abs( clamped - ev ) > 0.01f ? $"EV clamped to {clamped} (-6 to +6)" : null;
		return new { success = true, exposureCompensation = clamped, autoExposure, warning };
	}

	[McpTool("sbox_set_shadow_settings", "Sets shadow quality settings on the main directional light.", OptionalParams = new[]{"distance", "bias", "normalBias", "cascadeCount", "lightGuid"}, DestructiveHint = true)]
	public object SetShadowSettings( string distance = null, string bias = null, string normalBias = null, string cascadeCount = null, string lightGuid = null )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		var light = FindDirectionalLight( scene, lightGuid ); if ( light == null ) return new { error = "No DirectionalLight" };
		try
		{
			var td = TypeLibrary.GetType( typeof( DirectionalLight ) );
			if ( distance != null && float.TryParse( distance, out var d ) ) td?.Properties.FirstOrDefault( p => p.Name == "ShadowDistance" && p.CanWrite )?.SetValue( light, d );
			if ( bias != null && float.TryParse( bias, out var b ) ) td?.Properties.FirstOrDefault( p => p.Name == "ShadowBias" && p.CanWrite )?.SetValue( light, b );
			if ( normalBias != null && float.TryParse( normalBias, out var nb ) ) td?.Properties.FirstOrDefault( p => p.Name == "ShadowNormalBias" && p.CanWrite )?.SetValue( light, nb );
			if ( cascadeCount != null && int.TryParse( cascadeCount, out var cc ) ) td?.Properties.FirstOrDefault( p => p.Name == "ShadowCascadeCount" && p.CanWrite )?.SetValue( light, cc );
		}
		catch ( Exception e ) { return new { error = e.Message }; }
		var shadowDistProp = TypeLibrary.GetType( typeof( DirectionalLight ) )?.Properties.FirstOrDefault( p => p.Name == "ShadowDistance" && p.CanRead );
		var sd = shadowDistProp?.GetValue( light ) ?? "unknown";
		return new { success = true, shadowDistance = sd, shadowBias = light.ShadowBias };
	}

	[McpTool("sbox_set_skybox", "Creates or modifies a skybox. Auto-creates SkyBox2D if none exists.", OptionalParams = new[]{"materialPath", "intensity"}, DestructiveHint = true)]
	public object SetSkybox( string materialPath = null, string intensity = null )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		try
		{
			var skyType = TypeLibrary.GetType( "Sandbox.SceneSkyBox" ) ?? TypeLibrary.GetType( "Sandbox.SkyBox2D" );
			if ( skyType == null )
			{
				var candidates = TypeLibrary.GetTypes<Component>().Where( t => t.Name.Contains( "Sky" ) || t.Name.Contains( "sky" ) ).Select( t => t.FullName ).Take( 10 ).ToList();
				return new { error = "SkyComponent type not found", tried = new[] { "Sandbox.SceneSkyBox", "Sandbox.SkyBox2D" }, candidates };
			}
			Component sky = null;
			foreach ( var go in scene.GetAllObjects( true ) )
			{
				foreach ( var comp in go.Components.GetAll<Component>() )
				{
					if ( comp.IsValid() && TypeLibrary.GetType( comp.GetType() )?.FullName == skyType.FullName )
					{ sky = comp; break; }
				}
				if ( sky != null ) break;
			}
			if ( sky == null )
			{
				var skyGo = new GameObject( true, "MCP Skybox" );
				sky = skyGo.Components.Create( skyType );
				if ( !sky.IsValid() ) return new { error = "Failed to create skybox" };
			}
			var skyTd = TypeLibrary.GetType( sky.GetType() );
			if ( materialPath != null )
			{
				var mat = Material.Load( materialPath );
				if ( mat == null ) return new { error = $"Material not found: {materialPath}" };
				var matProp = skyTd?.Properties.FirstOrDefault( p => p.Name == "Material" && p.CanWrite );
				if ( matProp != null ) matProp.SetValue( sky, mat );
			}
			if ( intensity != null && float.TryParse( intensity, out var i ) )
			{
				skyTd?.Properties.FirstOrDefault( p => p.Name == "Intensity" && p.CanWrite )?.SetValue( sky, i );
			}
			var readMatProp = skyTd?.Properties.FirstOrDefault( p => p.Name == "Material" && p.CanRead );
			var matName = readMatProp?.GetValue( sky ) is Material m ? m.Name : "none";
			return new { success = true, material = matName, autoCreated = true };
		}
		catch
		{
			return new { error = "Failed to set skybox" };
		}
	}

	[McpTool("sbox_set_tonemapping", "Changes the tonemapping mode. Options: none, aces, reinhard, neutral, filmic.", DestructiveHint = true)]
	public object SetTonemapping( string mode )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		var cam = scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c.IsMainCamera ); if ( cam == null ) return new { error = "No main camera" };
		try
		{
			var td = TypeLibrary.GetType( typeof( CameraComponent ) );
			var pp = td?.Properties.FirstOrDefault( p => p.Name == "ToneMapping" && p.CanWrite );
			if ( pp == null ) return new { error = "Tonemapping property not found" };
			var toneType = TypeLibrary.GetType( "Sandbox.ToneMapping" );
			if ( toneType == null ) return new { error = "ToneMapping enum not found" };
			pp.SetValue( cam, Enum.Parse( toneType.TargetType, mode, ignoreCase: true ) );
			return new { success = true, mode };
		}
		catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_set_post_process", "Enables or disables post-processing on the main camera.", DestructiveHint = true)]
	public object SetPostProcess( string enabled )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		var cam = scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c.IsMainCamera ); if ( cam == null ) return new { error = "No main camera" };
		if ( !bool.TryParse( enabled, out var val ) ) return new { error = "Must be true or false" };
		cam.EnablePostProcessing = val; return new { success = true, enablePostProcessing = val };
	}

	[McpTool("sbox_set_tint", "Sets the tint color on a ModelRenderer.", DestructiveHint = true)]
	public object SetTint( string guidStr, string color )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var renderer = go.Components.Get<ModelRenderer>(); if ( !renderer.IsValid() ) return new { error = "No ModelRenderer" };
		var parts = color.Split( ',' ); if ( parts.Length < 3 || !float.TryParse( parts[0], out var r ) || !float.TryParse( parts[1], out var g ) || !float.TryParse( parts[2], out var b ) ) return new { error = "Invalid color" };
		renderer.Tint = new Color( r, g, b, parts.Length >= 4 && float.TryParse( parts[3], out var a ) ? a : 1f );
		return new { success = true, newColor = renderer.Tint.ToString() };
	}

	[McpTool("sbox_set_material", "Overrides the material on a ModelRenderer.", DestructiveHint = true)]
	public object SetMaterial( string guidStr, string materialPath )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var renderer = go.Components.Get<ModelRenderer>(); if ( !renderer.IsValid() ) return new { error = "No ModelRenderer" };
		var material = Material.Load( materialPath ); if ( material == null ) return new { error = $"Material not found: {materialPath}" };
		renderer.MaterialOverride = material; return new { success = true, material = materialPath };
	}

	[McpTool("sbox_reset_material", "Removes the material override.", DestructiveHint = true)]
	public object ResetMaterial( string guidStr )
	{
		var scene = Game.ActiveScene; if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid Guid" };
		var go = scene.Directory.FindByGuid( guid ); if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var renderer = go.Components.Get<ModelRenderer>(); if ( !renderer.IsValid() ) return new { error = "No ModelRenderer" };
		renderer.MaterialOverride = null; return new { success = true, message = "Material override removed" };
	}

	[McpTool("sbox_add_logic_rule", "Creates a new Logic Weaver rule.", OptionalParams = new[]{"actionParams"}, DestructiveHint = true)]
	public object AddLogicRule( string triggerType, string sourceGuid, string actionType, string targetGuid, string actionParams = null )
	{
		var id = LogicWeaver.AddRule( new LogicRule { TriggerType = triggerType, SourceGuid = sourceGuid, ActionType = actionType, TargetGuid = targetGuid, ActionParams = actionParams } );
		return new { success = true, ruleId = id };
	}

	[McpTool("sbox_remove_logic_rule", "Removes a Logic Weaver rule by ID.", DestructiveHint = true)] public object RemoveLogicRule( string ruleId ) { var r = LogicWeaver.RemoveRule( ruleId ); return r ? new { success = true } : new { error = $"Rule not found: {ruleId}" }; }
	[McpTool("sbox_list_logic_rules", "Lists all Logic Weaver rules.", ReadOnlyHint = true)] public object ListLogicRules() { var rules = LogicWeaver.ListRules().Select( r => new { id = r.Id, triggerType = r.TriggerType, sourceGuid = r.SourceGuid, actionType = r.ActionType, targetGuid = r.TargetGuid, actionParams = r.ActionParams, enabled = r.Enabled } ).ToList(); return new { rules, count = rules.Count }; }
	[McpTool("sbox_clear_logic_rules", "Clears all Logic Weaver rules.", DestructiveHint = true)] public object ClearLogicRules() { LogicWeaver.Clear(); return new { success = true }; }
	[McpTool("sbox_fire_logic_trigger", "Fires a trigger event on the Logic Weaver rules.", DestructiveHint = true)] public object FireLogicTrigger( string triggerType, string sourceGuid ) { LogicWeaver.Fire( triggerType, sourceGuid ); return new { success = true, triggerType, sourceGuid }; }


	[McpTool("sbox_navmesh_find_path", "Finds a path between two points on the navmesh.", ReadOnlyHint = true)]
	public object NavMeshFindPath( float fx, float fy, float fz, float tx, float ty, float tz )
	{
		if ( !NavMeshHelper.IsAvailable ) return new { error = "NavMesh not available" };
		try { var from = new Vector3( fx, fy, fz ); var to = new Vector3( tx, ty, tz ); var cf = NavMeshHelper.GetClosestPoint( from ); var ct = NavMeshHelper.GetClosestPoint( to ); if ( cf == null || ct == null ) return new { error = "No navmesh points" }; var path = NavMeshHelper.BuildPath( cf.Value, ct.Value ); if ( path == null || path.Count == 0 ) return new { error = "No path found" }; return new { success = true, from = cf.Value, to = ct.Value, pointCount = path.Count, points = path.Select( v => new { x = v.x, y = v.y, z = v.z } ).ToList(), totalDistance = NavMeshHelper.GetPathDistance( path ) }; } catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_navmesh_status", "Returns navmesh availability.", ReadOnlyHint = true)] public object NavMeshStatus() { try { return new { success = true, isAvailable = NavMeshHelper.IsAvailable, isLoaded = NavMeshHelper.IsLoaded }; } catch ( Exception e ) { return new { error = e.Message }; } }
	[McpTool("sbox_navmesh_random_point", "Returns a random point on the navmesh.", OptionalParams = new[]{"radius"}, ReadOnlyHint = true)] public object NavMeshRandomPoint( float cx, float cy, float cz, float radius = 500f ) { if ( !NavMeshHelper.IsAvailable ) return new { error = "NavMesh not available" }; try { var pt = NavMeshHelper.GetRandomPoint( new Vector3( cx, cy, cz ), radius ); return pt.HasValue ? new { success = true, point = new { x = pt.Value.x, y = pt.Value.y, z = pt.Value.z } } : new { error = "No point found" }; } catch ( Exception e ) { return new { error = e.Message }; } }

	[McpTool("sbox_set_wind", "Sets wind direction and speed. Note: WindComponent not available in this SDK version.", OptionalParams = new[]{"directionX", "directionY", "directionZ", "speed"}, DestructiveHint = true)]
	public object SetWind( float directionX = 0, float directionY = 1, float directionZ = 0, float speed = 100 )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		return new { success = false, available = false, note = "WindComponent type not found in this SDK version. Consider using PhysicsWorld gravity or environment settings instead.", direction = new { x = directionX, y = directionY, z = directionZ }, speed };
	}

	[McpTool("sbox_render_get_info", "Returns render/display information: resolution, shadow settings, quality levels.", ReadOnlyHint = true)]
	public object RenderGetInfo()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var cam = scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c.IsMainCamera );
		var light = scene.GetAllComponents<DirectionalLight>().FirstOrDefault();
		var camTd = cam.IsValid() ? TypeLibrary.GetType( typeof( CameraComponent ) ) : null;
		var lightTd = light.IsValid() ? TypeLibrary.GetType( typeof( DirectionalLight ) ) : null;
		var exposure = camTd?.Properties.FirstOrDefault( p => p.Name == "ExposureCompensation" && p.CanRead )?.GetValue( cam );
		var postProc = camTd?.Properties.FirstOrDefault( p => p.Name == "PostProcessEnabled" && p.CanRead )?.GetValue( cam );
		var normalBias = lightTd?.Properties.FirstOrDefault( p => p.Name == "ShadowNormalBias" && p.CanRead )?.GetValue( light );
		var cascadeCount = lightTd?.Properties.FirstOrDefault( p => p.Name == "ShadowCascadeCount" && p.CanRead )?.GetValue( light );
		return new
		{
			success = true,
			camera = cam.IsValid() ? new { fov = cam.FieldOfView, farClip = cam.ZFar, nearClip = cam.ZNear, exposure = exposure ?? "?", postProcess = postProc ?? "?" } : null,
			shadows = light.IsValid() ? new { enabled = light.Shadows, bias = light.ShadowBias, normalBias = normalBias ?? "?", cascadeCount = cascadeCount ?? "?" } : null
		};
	}

	[McpTool("sbox_terrain_query", "Queries terrain/ground height at a world position. Uses raycast downward.", ReadOnlyHint = true)]
	public object TerrainQuery( float x, float y )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		try
		{
			var start = new Vector3( x, y, 10000 );
			var end = new Vector3( x, y, -10000 );
			var tr = scene.Trace.Ray( start, end ).Run();
			if ( tr.Hit )
				return new { success = true, height = tr.EndPosition.z, hitPosition = new { x = tr.EndPosition.x, y = tr.EndPosition.y, z = tr.EndPosition.z }, normal = tr.Normal, distance = tr.Distance };
			return new { success = true, height = "none", message = "No ground hit" };
		}
		catch ( Exception e ) { return new { error = e.Message }; }
	}

	[McpTool("sbox_scene_settings", "Read or modify scene-wide settings: fog, lighting, physics via reflection.", OptionalParams = new[]{"fogColor", "fogStart", "fogEnd", "gravityX", "gravityY", "gravityZ"}, DestructiveHint = true)]
	public object SceneSettings( string fogColor = null, string fogStart = null, string fogEnd = null, string gravityX = null, string gravityY = null, string gravityZ = null )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };

		var changes = new System.Collections.Generic.List<string>();
		var sceneTd = TypeLibrary.GetType( scene.GetType() );

		if ( fogColor != null )
		{
			try
			{
				var colorProp = sceneTd?.Properties.FirstOrDefault( p => p.Name == "FogColor" && p.CanWrite );
				if ( colorProp != null )
				{
					var colorTd = TypeLibrary.GetType( typeof( Color ) );
				var parseMethod = colorTd?.Methods.FirstOrDefault( m => m.Name == "Parse" || m.Name == "FromString" || m.Name == "FromHex" );
					if ( parseMethod != null )
					{
						var fc = parseMethod.Invoke( null, new object[] { fogColor } );
						colorProp.SetValue( scene, fc );
						changes.Add( "fogColor" );
					}
				}
			}
			catch { }
		}
		if ( fogStart != null && float.TryParse( fogStart, out var fs ) )
		{
			try { sceneTd?.Properties.FirstOrDefault( p => p.Name == "FogStart" && p.CanWrite )?.SetValue( scene, fs ); changes.Add( "fogStart" ); } catch { }
		}
		if ( fogEnd != null && float.TryParse( fogEnd, out var fe ) )
		{
			try { sceneTd?.Properties.FirstOrDefault( p => p.Name == "FogEnd" && p.CanWrite )?.SetValue( scene, fe ); changes.Add( "fogEnd" ); } catch { }
		}
		if ( gravityX != null && float.TryParse( gravityX, out var gx )
			&& gravityY != null && float.TryParse( gravityY, out var gy )
			&& gravityZ != null && float.TryParse( gravityZ, out var gz ) )
		{
			try { scene.PhysicsWorld.Gravity = new Vector3( gx, gy, gz ); changes.Add( "gravity" ); } catch { }
		}

		var fogColorRead = sceneTd?.Properties.FirstOrDefault( p => p.Name == "FogColor" && p.CanRead )?.GetValue( scene )?.ToString() ?? "unknown";
		var fogStartRead = sceneTd?.Properties.FirstOrDefault( p => p.Name == "FogStart" && p.CanRead )?.GetValue( scene ) ?? "unknown";
		var fogEndRead = sceneTd?.Properties.FirstOrDefault( p => p.Name == "FogEnd" && p.CanRead )?.GetValue( scene ) ?? "unknown";

		return new
		{
			success = true,
			changes,
			scene = new
			{
				fogColor = fogColorRead,
				fogStart = fogStartRead,
				fogEnd = fogEndRead,
				gravity = scene.PhysicsWorld.Gravity
			}
		};
	}

	private static DirectionalLight FindDirectionalLight( Scene scene, string lightGuid = null )
	{
		if ( lightGuid != null && Guid.TryParse( lightGuid, out var guid ) ) { var go = scene.Directory.FindByGuid( guid ); if ( go.IsValid() ) return go.Components.Get<DirectionalLight>(); }
		return scene.GetAllComponents<DirectionalLight>().FirstOrDefault();
	}
}
