using Sandbox;
using System;
using System.Linq;

namespace Code.Core;

public sealed class SpeedPad : Component, Component.ITriggerListener
{
	[Property] public float CooldownTime { get; set; } = 1.5f;

	private float _cooldownTimer;

	protected override void OnUpdate()
	{
		if ( _cooldownTimer > 0f )
		{
			_cooldownTimer -= Time.Delta;
		}
	}

	void Component.ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( IsProxy || _cooldownTimer > 0f ) return;

		var cart = other.GameObject.Components.GetInAncestorsOrSelf<ShoppingCartController>();
		if ( cart != null )
		{
			_cooldownTimer = CooldownTime;

			// 1. Physical push forward
			cart.Push( cart.WorldRotation.Forward * 450f );

			// 2. Trigger Level 3 Drift Boost (instantly sets max speed multiplier)
			cart.TriggerDriftBoost( 3 );

			// 3. Play boost sound
			if ( !string.IsNullOrEmpty( cart.SoundDriftBoost ) )
			{
				Sound.Play( cart.SoundDriftBoost, WorldPosition );
			}

			// 4. Spawn flash of green particles at trigger point
			if ( cart.CollisionSparkPrefab != null )
			{
				var sparks = cart.CollisionSparkPrefab.Clone( WorldPosition + Vector3.Up * 10f );
				var r = sparks.Components.GetInDescendantsOrSelf<ModelRenderer>();
				if ( r != null )
				{
					r.Tint = new Color( 0f, 1f, 0.3f, 1f );
				}
				
				// Scale it up
				sparks.WorldScale = new Vector3( 2.2f, 2.2f, 2.2f );
				
				// Self destroy sparks
				DestroySparks( sparks );
			}
		}
	}

	private async void DestroySparks( GameObject sparks )
	{
		await GameTask.DelaySeconds( 1.2f );
		if ( sparks != null && sparks.IsValid() )
		{
			sparks.Destroy();
		}
	}

	void Component.ITriggerListener.OnTriggerExit( Collider other )
	{
	}
}
