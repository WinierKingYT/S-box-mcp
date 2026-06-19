using McpBridge.Extensions;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace McpBridge.Execution;

public class ToolRegistry
{
	private readonly Dictionary<string, MethodDescription> _tools = new();
	private readonly Dictionary<string, object> _instances = new();
	private readonly Dictionary<string, string> _toolGroups = new();
	private bool _initialized;

	public void EnsureInitialized()
	{
		if ( _initialized ) return;
		try
		{
			RegisterAll();
			_initialized = true;
		}
		catch
		{
			Log.Error( "[MCP] Tool Registration Failed" );
		}
	}

	private void RegisterAll()
	{
		_tools.Clear();
		_instances.Clear();
		_toolGroups.Clear();
		var types = TypeLibrary.GetTypesWithAttribute<McpToolGroupAttribute>();
		foreach ( var entry in types )
		{
			try
			{
					var groupName = entry.Type.Name;
				if ( groupName.EndsWith( "Tools" ) ) groupName = groupName.Substring( 0, groupName.Length - 5 );
				object inst = null;
				if ( !entry.Type.IsAbstract )
					inst = entry.Type.Create<object>();

				foreach ( var m in entry.Type.Methods )
				{
					var attr = m.GetCustomAttribute<McpToolAttribute>();
					if ( attr != null )
					{
						_tools[attr.Name] = m;
						_toolGroups[attr.Name] = groupName;
						if ( inst != null )
							_instances[m.Name] = inst;
						McpToolBridge.RegisterGlobalToolName( attr.Name );
					}
				}
			}
			catch
			{
				Log.Warning( $"[MCP] Failed to register tool group {entry.Type.Name}" );
			}
		}
		Log.Info( $"[MCP] Registered {_tools.Count} tools" );
	}

	public bool TryGet( string name, out MethodDescription method, out object instance )
	{
		if ( _tools.TryGetValue( name, out var m ) )
		{
			method = m;
			instance = _instances.GetValueOrDefault( m.Name );
			return true;
		}
		method = null;
		instance = null;
		return false;
	}

	public object RunSingle( string method, string paramsJson )
	{
		EnsureInitialized();
		if ( !_tools.TryGetValue( method, out var mdesc ) )
			return new { error = $"Method '{method}' not found" };

		try
		{
			var args = new List<object>();
			using var doc = JsonDocument.Parse( paramsJson ?? "{}" );
			var root = doc.RootElement;
			foreach ( var p in mdesc.Parameters )
			{
				args.Add( root.TryGetProperty( p.Name, out var v )
					? JsonSerializer.Deserialize( v.GetRawText(), p.ParameterType, JsonRpcExtensions.SerializerOpts )
					: null );
			}
			var instance = _instances.GetValueOrDefault( mdesc.Name );
			return mdesc.Invoke( instance, args.ToArray() );
		}
		catch
		{
			return new { error = "Tool execution failed" };
		}
	}

	public record ToolInfo( string Name, string Description, string Group, MethodDescription Method );

	public List<ToolInfo> ListAll()
	{
		return _tools.Select( t =>
		{
			var attr = t.Value.GetCustomAttribute<McpToolAttribute>();
			return new ToolInfo( t.Key, attr?.Description ?? "", _toolGroups.GetValueOrDefault( t.Key, "" ), t.Value );
		} ).ToList();
	}

	public Dictionary<string, MethodDescription> GetToolsDict() => _tools;
}
