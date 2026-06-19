using Sandbox;
using System.Collections.Generic;

namespace McpBridge.Extensions;

public static class GameObjectExtensions
{
	public static T GetOrCreate<T>( this GameObject go ) where T : Component, new()
	{
		var comp = go.Components.Get<T>();
		if ( comp.IsValid() ) return comp;
		return go.Components.Create<T>();
	}

	public static bool HasComponent<T>( this GameObject go ) where T : Component
		=> go.Components.Get<T>().IsValid();

	public static Dictionary<string, object> ToBrief( this GameObject go )
		=> new()
		{
			["id"] = go.Id,
			["name"] = go.Name,
			["position"] = new { x = go.WorldPosition.x, y = go.WorldPosition.y, z = go.WorldPosition.z }
		};
}
