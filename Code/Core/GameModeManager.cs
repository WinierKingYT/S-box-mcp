using Sandbox;
using System;
using System.Linq;

namespace Code.Core;

public sealed class GameModeManager : Component
{
	[Sync] [Property] public float RoundTimeLeft { get; set; } = 180f;
	[Sync] [Property] public int TargetItemsToCheckout { get; set; } = 15;
	[Sync] [Property] public int CheckedOutItemsCount { get; set; } = 0;
	[Sync] public bool IsGameOver { get; set; } = false;

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		if ( !IsGameOver )
		{
			RoundTimeLeft -= Time.Delta;
			if ( RoundTimeLeft <= 0f )
			{
				RoundTimeLeft = 0f;
				EndGame();
			}
		}
	}

	private void EndGame()
	{
		IsGameOver = true;
		
		// Play end round metal gong sound
		Sound.Play( "sounds/physics/metal/metal_solid_impact_hard.sound", WorldPosition );

		McpBridge.McpEventSystem.Emit( "game_over", new { success = CheckedOutItemsCount >= TargetItemsToCheckout, checkedOut = CheckedOutItemsCount, target = TargetItemsToCheckout } );
	}
}
