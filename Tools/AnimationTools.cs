using McpBridge;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace McpBridge.Tools;

[McpToolGroup("Animation")]
public class AnimationTools
{
	[McpTool("sbox_anim_set_param", "Sets an AnimGraph parameter on a ModelRenderer. Supports float, bool, int, and trigger.")]
	public object SetAnimParam( string guidStr, string paramName, string value, string paramType = "float" )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid GUID" };
		var go = scene.Directory.FindByGuid( guid );
		if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var renderer = go.Components.Get<ModelRenderer>();
		if ( !renderer.IsValid() ) return new { error = "No ModelRenderer found" };

		try
		{
			var td = TypeLibrary.GetType( typeof( ModelRenderer ) );
			var setMethod = td?.Methods.FirstOrDefault( m => m.Name == "SetAnimParameter" || m.Name == "Set" || m.Name == "SetParameter" );
			if ( setMethod == null )
			{
				var allMethods = td?.Methods.Select( m => m.Name ).Distinct().ToList();
				var candidates = allMethods?.Where( n => n.StartsWith( "Set" ) || n.StartsWith( "Anim" ) || n.Contains( "Param" ) ).ToList();
				return new { error = "ModelRenderer anim set API not found", tried = new[] { "SetAnimParameter", "Set", "SetParameter" }, availableSetMethods = candidates ?? new List<string>() };
			}

			object val = paramType.ToLower() switch
			{
				"float" => float.Parse( value ),
				"bool" => bool.Parse( value ),
				"int" => int.Parse( value ),
				"trigger" => true,
				_ => (object)value
			};
			setMethod.Invoke( renderer, new object[] { paramName, val } );
			return new { success = true, name = go.Name, paramName, value, paramType };
		}
		catch ( Exception e )
		{
			return new { error = $"Failed to set param: {e.Message}" };
		}
	}

	[McpTool("sbox_anim_get_params", "Gets all readable AnimGraph parameters from a ModelRenderer.")]
	public object GetAnimParams( string guidStr )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid GUID" };
		var go = scene.Directory.FindByGuid( guid );
		if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var renderer = go.Components.Get<ModelRenderer>();
		if ( !renderer.IsValid() ) return new { error = "No ModelRenderer found" };

		try
		{
			var td = TypeLibrary.GetType( typeof( ModelRenderer ) );
			var graphProp = td?.Properties.FirstOrDefault( p => p.Name == "AnimGraph" && p.CanRead );
			if ( graphProp == null )
			{
				var props = td?.Properties.Where( p => p.CanRead ).Select( p => p.Name ).ToList();
				return new { error = "AnimGraph property not found on ModelRenderer", availableProperties = props ?? new List<string>() };
			}
			var graph = graphProp.GetValue( renderer );
			if ( graph == null ) return new { error = "No AnimGraph assigned" };

			var graphTd = TypeLibrary.GetType( graph.GetType() );
			var paramsProp = graphTd?.Properties.FirstOrDefault( p => p.Name == "Parameters" && p.CanRead );
			if ( paramsProp == null ) return new { error = "Parameters property not found" };
			var parameters = paramsProp.GetValue( graph ) as System.Collections.IEnumerable;
			if ( parameters == null ) return new { error = "No parameters" };

			var anims = new List<object>();
			foreach ( var p in parameters )
			{
				try
				{
					var pTd = TypeLibrary.GetType( p.GetType() );
					var name = pTd?.Properties.FirstOrDefault( x => x.Name == "Name" )?.GetValue( p )?.ToString() ?? "?";
					var type = pTd?.Properties.FirstOrDefault( x => x.Name == "Type" )?.GetValue( p )?.ToString() ?? "?";
					var value = pTd?.Properties.FirstOrDefault( x => x.Name == "Value" )?.GetValue( p )?.ToString() ?? "?";
					anims.Add( new { name, type, value } );
				}
				catch { }
			}
			return new { success = true, name = go.Name, paramCount = anims.Count, parameters = anims };
		}
		catch ( Exception e )
		{
			return new { error = e.Message };
		}
	}

	[McpTool("sbox_anim_play_sequence", "Plays an animation sequence on a ModelRenderer by name.")]
	public object PlaySequence( string guidStr, string sequenceName, bool loop = false )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid GUID" };
		var go = scene.Directory.FindByGuid( guid );
		if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var renderer = go.Components.Get<ModelRenderer>();
		if ( !renderer.IsValid() ) return new { error = "No ModelRenderer found" };

		try
		{
			var td = TypeLibrary.GetType( typeof( ModelRenderer ) );
			var currentSeqProp = td?.Properties.FirstOrDefault( p => p.Name == "CurrentSequence" && p.CanWrite );
			if ( currentSeqProp == null ) return new { error = "CurrentSequence property not available" };

			var modelTd = TypeLibrary.GetType( typeof( Model ) );
			var findSeqMethod = modelTd?.Methods.FirstOrDefault( m => m.Name == "FindSequence" );
			if ( findSeqMethod == null || renderer.Model == null )
				return new { error = "FindSequence API not available or no model" };

			var seq = findSeqMethod.Invoke( renderer.Model, new object[] { sequenceName } );
			if ( seq == null ) return new { error = $"Sequence '{sequenceName}' not found" };
			currentSeqProp.SetValue( renderer, seq );
			return new { success = true, name = go.Name, sequence = sequenceName, loop };
		}
		catch ( Exception e )
		{
			return new { error = e.Message };
		}
	}

	[McpTool("sbox_anim_list_sequences", "Lists all animation sequences on a ModelRenderer.")]
	public object ListSequences( string guidStr )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid GUID" };
		var go = scene.Directory.FindByGuid( guid );
		if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var renderer = go.Components.Get<ModelRenderer>();
		if ( !renderer.IsValid() ) return new { error = "No ModelRenderer found" };
		if ( renderer.Model == null ) return new { error = "No Model assigned" };

		try
		{
			var modelTd = TypeLibrary.GetType( typeof( Model ) );
			var sequencesProp = modelTd?.Properties.FirstOrDefault( p => p.Name == "Sequences" && p.CanRead );
			if ( sequencesProp == null ) return new { error = "Sequences property not found" };
			var seqs = sequencesProp.GetValue( renderer.Model ) as System.Collections.IEnumerable;
			if ( seqs == null ) return new { error = "No sequences" };

			var result = new List<object>();
			foreach ( var s in seqs )
			{
				try
				{
					var sTd = TypeLibrary.GetType( s.GetType() );
					var name = sTd?.Properties.FirstOrDefault( x => x.Name == "Name" )?.GetValue( s )?.ToString() ?? "?";
					var fps = sTd?.Properties.FirstOrDefault( x => x.Name == "FramesPerSecond" )?.GetValue( s );
					var frames = sTd?.Properties.FirstOrDefault( x => x.Name == "FrameCount" )?.GetValue( s );
					result.Add( new { name, fps, frameCount = frames } );
				}
				catch { }
			}
			return new { success = true, name = go.Name, count = result.Count, sequences = result };
		}
		catch ( Exception e )
		{
			return new { error = e.Message };
		}
	}

	[McpTool("sbox_anim_set_layer_weight", "Sets the blend weight (0-1) for an animation layer on a ModelRenderer.")]
	public object SetLayerWeight( string guidStr, int layerIndex, float weight )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid GUID" };
		var go = scene.Directory.FindByGuid( guid );
		if ( !go.IsValid() ) return new { error = "GameObject not found" };
		var renderer = go.Components.Get<ModelRenderer>();
		if ( !renderer.IsValid() ) return new { error = "No ModelRenderer found" };

		try
		{
			var td = TypeLibrary.GetType( typeof( ModelRenderer ) );
			var graphProp = td?.Properties.FirstOrDefault( p => p.Name == "AnimGraph" && p.CanRead );
			if ( graphProp == null ) return new { error = "AnimGraph property not found" };
			var graph = graphProp.GetValue( renderer );
			if ( graph == null ) return new { error = "No AnimGraph assigned" };

			var graphTd = TypeLibrary.GetType( graph.GetType() );
			var layersProp = graphTd?.Properties.FirstOrDefault( p => (p.Name == "Layers" || p.Name == "AnimationLayers") && p.CanRead );
			if ( layersProp == null ) return new { error = "Layers property not found on AnimGraph" };
			var layers = layersProp.GetValue( graph ) as System.Collections.IEnumerable;
			if ( layers == null ) return new { error = "No layers" };

			int idx = 0;
			foreach ( var layer in layers )
			{
				if ( idx == layerIndex )
				{
					var layerTd = TypeLibrary.GetType( layer.GetType() );
					layerTd?.Properties.FirstOrDefault( p => p.Name == "Weight" && p.CanWrite )?.SetValue( layer, Validation.Clamp( weight, 0f, 1f ) );
					return new { success = true, name = go.Name, layerIndex, weight = Validation.Clamp( weight, 0f, 1f ) };
				}
				idx++;
			}
			return new { error = $"Layer index {layerIndex} out of range (0-{idx - 1})" };
		}
		catch ( Exception e )
		{
			return new { error = e.Message };
		}
	}

	[McpTool("sbox_anim_trigger", "Triggers an AnimGraph event by name on a ModelRenderer.")]
	public object AnimTrigger( string guidStr, string eventName )
	{
		return SetAnimParam( guidStr, eventName, "true", "trigger" );
	}
}
