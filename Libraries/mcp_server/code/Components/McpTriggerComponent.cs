using Sandbox;
using System;

namespace McpBridge;

public sealed class McpTriggerComponent : Component, Component.ITriggerListener
{
	[Property] public string TriggerType { get; set; } = "touch";
	[Property] public string FilterTag { get; set; } = "player";
	[Property] public float Cooldown { get; set; } = 1.0f;

	private float _cooldownTimer;

	protected override void OnUpdate()
	{
		if ( _cooldownTimer > 0 )
			_cooldownTimer -= Time.Delta;
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( _cooldownTimer > 0 ) return;
		if ( !string.IsNullOrEmpty( FilterTag ) && !other.GameObject.Tags.Has( FilterTag ) ) return;

		_cooldownTimer = Cooldown;
		LogicWeaver.Fire( TriggerType, GameObject.Id.ToString() );
		Log.Info( $"[MCP Trigger] Event '{TriggerType}' fired by '{GameObject.Name}' (ID: {GameObject.Id})" );
	}

	public void OnTriggerExit( Collider other )
	{
	}
}
