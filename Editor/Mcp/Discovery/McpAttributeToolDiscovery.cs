using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Editor;

internal sealed class McpAttributeToolDiscovery
{
	private readonly McpToolRegistry _registry;
	private readonly Func<McpConfig> _configProvider;

	public McpAttributeToolDiscovery(
		McpToolRegistry registry,
		Func<McpConfig> configProvider )
	{
		_registry = registry;
		_configProvider = configProvider;
	}

	public void Discover()
	{
		LoadConfiguredAssemblies();
		foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
			DiscoverAssembly( assembly );
	}

	private void LoadConfiguredAssemblies()
	{
		foreach ( var assemblyName in _configProvider().AutoLoadAssemblies
			.Distinct( StringComparer.OrdinalIgnoreCase ) )
		{
			var path = McpPathResolver.ResolveAssemblyPath( assemblyName );
			if ( path == null )
			{
				Log.Warning( $"[MCP] Assembly not found: {assemblyName}" );
				continue;
			}

			try
			{
				Assembly.LoadFrom( path );
			}
			catch ( Exception exception )
			{
				Log.Warning( $"[MCP] Load failed for {assemblyName}: {exception.Message}" );
			}
		}
	}

	private void DiscoverAssembly( Assembly assembly )
	{
		if ( assembly.IsDynamic ) return;

		foreach ( var type in GetLoadableTypes( assembly ) )
		{
			try
			{
				DiscoverType( type );
			}
			catch ( Exception exception )
			{
				Log.Warning( $"[MCP] Failed scanning type {type.FullName}: {exception.Message}" );
			}
		}
	}

	private void DiscoverType( Type type )
	{
		if ( !HasAttributeNamed( type.GetCustomAttributes(), "McpToolGroupAttribute" ) )
			return;

		object instance = null;
		var methods = type.GetMethods(
			BindingFlags.Public
			| BindingFlags.NonPublic
			| BindingFlags.Instance
			| BindingFlags.Static );
		if ( methods.Any( method =>
			!method.IsStatic
			&& HasAttributeNamed(
				method.GetCustomAttributes(),
				"McpToolAttribute" ) ) )
		{
			if ( type.IsAbstract || type.IsGenericType )
				throw new InvalidOperationException(
					$"Tool group {type.FullName} cannot be instantiated." );
			instance = Activator.CreateInstance( type );
		}

		foreach ( var method in methods )
		{
			var attribute = method.GetCustomAttributes()
				.FirstOrDefault( candidate =>
					candidate.GetType().Name == "McpToolAttribute" );
			if ( attribute == null ) continue;

			var name = attribute.GetType().GetProperty( "Name" )?.GetValue( attribute ) as string;
			var description = attribute.GetType().GetProperty( "Description" )
				?.GetValue( attribute ) as string;
			var impactValue = attribute.GetType().GetProperty( "Impact" )
				?.GetValue( attribute );
			var impact = impactValue is McpToolImpact declaredImpact
				? declaredImpact
				: McpToolImpact.HighRisk;
			if ( string.IsNullOrWhiteSpace( name ) ) continue;

			var capturedMethod = method;
			var capturedInstance = instance;
			_registry.Register(
				name,
				description ?? "",
				arguments => InvokeMethod( capturedMethod, capturedInstance, arguments ),
				McpToolSchemaGenerator.Generate( capturedMethod ),
				impact );
			Log.Info(
				$"[MCP] Tool registered: {name} — {description ?? ""}" );
		}
	}

	private static bool HasAttributeNamed(
		IEnumerable<object> attributes,
		string attributeTypeName )
		=> attributes.Any( attribute =>
			attribute.GetType().Name == attributeTypeName );

	private static object InvokeMethod(
		MethodInfo method,
		object instance,
		JsonElement arguments )
	{
		var invokeArguments = new List<object>();
		var isObject = arguments.ValueKind == JsonValueKind.Object;
		foreach ( var parameter in method.GetParameters() )
		{
			if ( isObject && arguments.TryGetProperty( parameter.Name, out var value ) )
			{
				invokeArguments.Add( JsonSerializer.Deserialize(
					value.GetRawText(),
					parameter.ParameterType,
					new JsonSerializerOptions { PropertyNameCaseInsensitive = true } ) );
				continue;
			}

			if ( parameter.HasDefaultValue )
			{
				invokeArguments.Add( parameter.DefaultValue );
				continue;
			}

			throw new ArgumentException(
				$"Missing required argument '{parameter.Name}' for tool '{method.Name}'." );
		}

		return method.Invoke(
			method.IsStatic ? null : instance,
			invokeArguments.ToArray() );
	}

	private static IEnumerable<Type> GetLoadableTypes( Assembly assembly )
	{
		try
		{
			return assembly.GetTypes();
		}
		catch ( ReflectionTypeLoadException exception )
		{
			return exception.Types.Where( type => type != null );
		}
		catch
		{
			return Array.Empty<Type>();
		}
	}
}
