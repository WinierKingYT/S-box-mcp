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
	private readonly Dictionary<string, Func<JsonElement, object>> _staticInvokers = new();
	private bool _initialized;
	private readonly object _initLock = new();

	public void EnsureInitialized()
	{
		if ( _initialized ) return;
		lock ( _initLock )
		{
			if ( _initialized ) return;
			try
			{
				RegisterAll();
				_initialized = true;
			}
			catch ( Exception e )
			{
				Log.Error( $"[MCP] Tool Registration Failed: {e.Message}" );
			}
		}
	}

	private void RegisterAll()
	{
		_tools.Clear();
		_instances.Clear();
		_toolGroups.Clear();
		_staticInvokers.Clear();

		var genType = TypeLibrary.GetType( "McpBridge.Execution.McpGeneratedTools" );
		if ( genType != null )
		{
			try
			{
				var toolsField = genType.TargetType.GetField( "Tools", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static );
				var genTools = toolsField?.GetValue( null ) as System.Collections.IDictionary;
				if ( genTools != null )
				{
					foreach ( System.Collections.DictionaryEntry kv in genTools )
					{
						var toolName = (string)kv.Key;
						var boundTool = kv.Value;
						if ( boundTool == null ) continue;

						var tType = boundTool.GetType();
						var desc = (string)tType.GetProperty( "Description" )?.GetValue( boundTool ) ?? "";
						var group = (string)tType.GetProperty( "Group" )?.GetValue( boundTool ) ?? "";
						var invokeDelegate = tType.GetProperty( "Invoke" )?.GetValue( boundTool ) as Func<JsonElement, object>;

						if ( invokeDelegate != null )
						{
							_staticInvokers[toolName] = invokeDelegate;
							_toolGroups[toolName] = group;

							var matchedType = TypeLibrary.GetTypesWithAttribute<McpToolGroupAttribute>()
								.FirstOrDefault( t => t.Type != null && t.Type.Name.StartsWith( group ) );
							
							var mdesc = matchedType.Type?.Methods.FirstOrDefault( m => m.GetCustomAttribute<McpToolAttribute>()?.Name == toolName );
							if ( mdesc != null )
							{
								_tools[toolName] = mdesc;
								McpToolBridge.RegisterGlobalToolName( toolName );
							}
						}
					}
					Log.Info( $"[MCP] Loaded {_staticInvokers.Count} statically compiled tools." );
					return;
				}
			}
			catch ( Exception ex )
			{
				Log.Warning( $"[MCP] Failed to load statically compiled tools: {ex.Message}. Falling back to reflection." );
			}
		}

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
		Log.Info( $"[MCP] Registered {_tools.Count} tools via reflection" );
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

	public bool TryGetStaticInvoker( string name, out Func<JsonElement, object> invoker )
	{
		return _staticInvokers.TryGetValue( name, out invoker );
	}

	public object RunSingle( string method, string paramsJson )
	{
		EnsureInitialized();
		
		if ( _staticInvokers.TryGetValue( method, out var invoker ) )
		{
			try
			{
				using var doc = JsonDocument.Parse( paramsJson ?? "{}" );
				return invoker( doc.RootElement );
			}
			catch ( Exception ex )
			{
				return new { error = $"Static tool execution failed: {ex.Message}" };
			}
		}

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

	public record ToolInfo( string Name, string Description, string Group, MethodDescription Method, object Annotations = null );

	public List<ToolInfo> ListAll()
	{
		return _tools.Select( t =>
		{
			var attr = t.Value.GetCustomAttribute<McpToolAttribute>();
			object ann = null;
			if ( attr != null && (attr.ReadOnlyHint || attr.DestructiveHint || attr.OpenWorldWarning) )
				ann = new { readOnlyHint = attr.ReadOnlyHint, destructiveHint = attr.DestructiveHint, openWorldWarning = attr.OpenWorldWarning };
			return new ToolInfo( t.Key, attr?.Description ?? "", _toolGroups.GetValueOrDefault( t.Key, "" ), t.Value, ann );
		} ).ToList();
	}

	public Dictionary<string, MethodDescription> GetToolsDict() => _tools;
}
