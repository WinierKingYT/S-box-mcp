using System;
using System.Collections.Generic;

namespace McpBridge;

public static class McpEvents
{
	public static event Action<string, string> OnEventFired;

	private static readonly List<string> _eventTypes = new()
	{
		"cart_grabbed",
		"cart_released",
		"item_added",
		"item_removed",
		"checkout_complete",
		"player_joined",
		"player_left",
		"sale_started",
		"sale_ended",
		"scenario_started",
		"scenario_completed",
		"macro_executed"
	};

	public static List<string> List() => new( _eventTypes );

	public static void Fire( string type, string data )
	{
		var handler = OnEventFired;
		if ( handler == null ) return;
		foreach ( var subscriber in handler.GetInvocationList() )
		{
			try
			{
				( (Action<string, string>)subscriber )( type, data );
			}
			catch ( Exception e )
			{
				Log.Warning( $"[MCP-Events] Subscriber error in '{type}': {e.Message}" );
			}
		}
	}
}
