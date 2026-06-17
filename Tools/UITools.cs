using McpBridge;
using Sandbox;
using System;
using System.Linq;

namespace McpBridge.Tools;

[McpToolGroup("UI")]
public class UITools
{
	[McpTool("sbox_ui_world_panel", "Creates a WorldPanel on a GameObject. Renders a Razor UI in 3D space.")]
	public object CreateWorldPanel( string guidStr = null, string name = "UI Panel", float width = 512, float height = 512, string razorPath = null )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };

		GameObject go;
		if ( !string.IsNullOrEmpty( guidStr ) && Guid.TryParse( guidStr, out var guid ) )
		{
			go = scene.Directory.FindByGuid( guid );
			if ( !go.IsValid() ) return new { error = "GameObject not found" };
		}
		else
		{
			go = new GameObject( true, name );
		}

		try
		{
			var wpType = TypeLibrary.GetType( "Sandbox.WorldPanel" );
			if ( wpType == null ) wpType = TypeLibrary.GetType( "Sandbox.UI.WorldPanel" );
			if ( wpType == null ) return new { error = "WorldPanel type not found" };

			var panel = go.Components.Create( wpType );
			var td = TypeLibrary.GetType( panel.GetType() );

			var widthProp = td?.Properties.FirstOrDefault( p => p.Name == "Width" || p.Name == "PanelWidth" );
			widthProp?.SetValue( panel, width );
			var heightProp = td?.Properties.FirstOrDefault( p => p.Name == "Height" || p.Name == "PanelHeight" );
			heightProp?.SetValue( panel, height );

			if ( !string.IsNullOrEmpty( razorPath ) )
			{
				var pathProp = td?.Properties.FirstOrDefault( p => p.Name == "PanelPath" || p.Name == "RazorPath" );
				pathProp?.SetValue( panel, razorPath );
			}

			return new { success = true, id = go.Id.ToString(), name = go.Name, componentType = "WorldPanel", width, height };
		}
		catch ( Exception e )
		{
			return new { error = $"Failed to create WorldPanel: {e.Message}" };
		}
	}

	[McpTool("sbox_ui_text_display", "Creates a TextDisplay on a GameObject. Shows text in 3D space.")]
	public object CreateTextDisplay( string guidStr = null, string name = "Text Display", string text = "Hello World", string color = "#ffffff", float size = 24 )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };

		GameObject go;
		if ( !string.IsNullOrEmpty( guidStr ) && Guid.TryParse( guidStr, out var guid ) )
		{
			go = scene.Directory.FindByGuid( guid );
			if ( !go.IsValid() ) return new { error = "GameObject not found" };
		}
		else
		{
			go = new GameObject( true, name );
		}

		try
		{
			var tdType = TypeLibrary.GetType( "Sandbox.TextDisplay" );
			if ( tdType == null ) tdType = TypeLibrary.GetType( "Sandbox.UI.TextDisplay" );
			if ( tdType == null ) return new { error = "TextDisplay type not found" };

			var display = go.Components.Create( tdType );
			var compTd = TypeLibrary.GetType( display.GetType() );

			var textProp = compTd?.Properties.FirstOrDefault( p => p.Name == "Text" || p.Name == "Content" );
			textProp?.SetValue( display, text );

			var colorTd = TypeLibrary.GetType( typeof( Color ) );
			var parseMethod = colorTd?.Methods.FirstOrDefault( m => m.Name == "Parse" || m.Name == "FromHex" );
			if ( parseMethod != null && !string.IsNullOrEmpty( color ) )
			{
				var parsedColor = parseMethod.Invoke( null, new object[] { color } );
				var colorProp = compTd?.Properties.FirstOrDefault( p => p.Name == "Color" || p.Name == "TextColor" );
				colorProp?.SetValue( display, parsedColor );
			}

			var sizeProp = compTd?.Properties.FirstOrDefault( p => p.Name == "FontSize" || p.Name == "Size" );
			sizeProp?.SetValue( display, size );

			return new { success = true, id = go.Id.ToString(), name = go.Name, componentType = "TextDisplay", text, color, size };
		}
		catch ( Exception e )
		{
			return new { error = $"Failed to create TextDisplay: {e.Message}" };
		}
	}

	[McpTool("sbox_ui_set_text", "Updates the text content of a TextDisplay component on a GameObject.")]
	public object SetText( string guidStr, string text )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		if ( !Guid.TryParse( guidStr, out var guid ) ) return new { error = "Invalid GUID" };
		var go = scene.Directory.FindByGuid( guid );
		if ( !go.IsValid() ) return new { error = "GameObject not found" };

		try
		{
			var tdType = TypeLibrary.GetType( "Sandbox.TextDisplay" );
			if ( tdType == null ) tdType = TypeLibrary.GetType( "Sandbox.UI.TextDisplay" );

			Component display = null;
			foreach ( var comp in go.Components.GetAll<Component>() )
			{
				var compTd = TypeLibrary.GetType( comp.GetType() );
				if ( compTd == tdType ) { display = comp; break; }
			}
			if ( display == null ) return new { error = "No TextDisplay component found on this GameObject" };

			var targetTd = TypeLibrary.GetType( display.GetType() );
			var textProp = targetTd?.Properties.FirstOrDefault( p => p.Name == "Text" || p.Name == "Content" );
			if ( textProp == null ) return new { error = "Text property not found on TextDisplay" };
			textProp.SetValue( display, text );
			return new { success = true, id = guidStr, name = go.Name, text };
		}
		catch ( Exception e )
		{
			return new { error = $"Failed to set text: {e.Message}" };
		}
	}
}
