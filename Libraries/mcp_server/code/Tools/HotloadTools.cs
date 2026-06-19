using McpBridge;
using Sandbox;
using System;
using System.Linq;

namespace McpBridge.Tools;

[McpToolGroup("Hotload")]
public class HotloadTools
{
	[McpTool("sbox_hotload_trigger", "Triggers a script hot-reload.", DestructiveHint = true)]
	public object TriggerHotload()
	{
		try
		{
			var hotloadType = TypeLibrary.GetType( "Sandbox.Hotload" );
			if ( hotloadType == null )
			{
				hotloadType = TypeLibrary.GetType( "Sandbox.Editor.HotloadLibrary" );
				if ( hotloadType == null )
				{
					var globalNs = TypeLibrary.GetType( "Sandbox.Global.SystemNamespace" );
					if ( globalNs != null )
					{
						var hotloadMethod = globalNs.Methods.FirstOrDefault( m => m.Name == "Hotload" );
						if ( hotloadMethod != null )
						{
							hotloadMethod.Invoke( null, Array.Empty<object>() );
							return new { success = true, method = "Global.SystemNamespace.Hotload" };
						}
					}
					return new { error = "No Hotload API found" };
				}
			}
			var execMethod = hotloadType.Methods.FirstOrDefault( m => m.Name == "Execute" || m.Name == "Run" || m.Name == "Trigger" );
			if ( execMethod == null )
				return new { error = $"Hotload type found ({hotloadType.Name}) but no Execute/Run/Trigger method" };
			execMethod.Invoke( null, Array.Empty<object>() );
			return new { success = true, method = $"{hotloadType.Name}.{execMethod.Name}" };
		}
		catch ( Exception e )
		{
			return new { error = $"Hotload failed: {e.Message}" };
		}
	}

	[McpTool("sbox_hotload_status", "Checks if hotload API is available and returns detected types.", ReadOnlyHint = true)]
	public object HotloadStatus()
	{
		var results = new System.Collections.Generic.List<object>();
		foreach ( var name in new[] { "Sandbox.Hotload", "Sandbox.Editor.HotloadLibrary", "Sandbox.Global.SystemNamespace" } )
		{
			var t = TypeLibrary.GetType( name );
			if ( t != null )
			{
				var methods = t.Methods.Select( m => m.Name ).Distinct().ToList();
				results.Add( new { type = name, methods });
			}
		}
		return new { hotloadAvailable = results.Count > 0, found = results };
	}

	[McpTool("sbox_hotload_code", "Writes a code snippet to a file and triggers hot-reload.", DestructiveHint = true)]
	public object WriteAndHotload( string fileName, string code )
	{
		try
		{
			var path = $"code_override/{fileName}";
			FileSystem.Data.WriteAllText( path, code );
			Log.Info( $"[MCP] Written {path}, triggering hotload..." );
			return TriggerHotload();
		}
		catch ( Exception e )
		{
			return new { error = $"Write failed: {e.Message}" };
		}
	}
}
