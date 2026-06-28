using Sandbox;
using System;
using System.Linq;
using Code.Core;
using Code.Player;

namespace Code.Economy;

public sealed class CheckoutRegister : Component
{
	[Property] public float CheckoutRange { get; set; } = 150f;
	[Property] public string SoundBeep { get; set; } = "sounds/physics/metal/wheel_click.sound";
	[Property] public int ItemPayout { get; set; } = 25;

	private float _checkoutTimer;

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		// Find the active shopping cart in range that has a driver and items
		var carts = Scene.GetAllComponents<ShoppingCartController>();
		ShoppingCartController targetCart = null;
		foreach ( var cart in carts )
		{
			if ( cart.Driver != null && cart.ItemCount > 0 )
			{
				float dist = (cart.WorldPosition - WorldPosition).Length;
				if ( dist < CheckoutRange )
				{
					targetCart = cart;
					break;
				}
			}
		}

		if ( targetCart != null )
		{
			_checkoutTimer -= Time.Delta;
			if ( _checkoutTimer <= 0f )
			{
				_checkoutTimer = 0.15f; // Process 1 item every 0.15 seconds

				// Deduct item from cart
				targetCart.ItemCount--;

				// Play checkout scanner beep sound
				if ( !string.IsNullOrEmpty( SoundBeep ) )
				{
					Sound.Play( SoundBeep, WorldPosition );
				}

				// Spawn green sparks representing cash splash
				if ( targetCart.CollisionSparkPrefab != null )
				{
					var sparks = targetCart.CollisionSparkPrefab.Clone( WorldPosition + Vector3.Up * 20f );
					var r = sparks.Components.GetInDescendantsOrSelf<ModelRenderer>();
					if ( r != null )
					{
						r.Tint = new Color( 0.1f, 0.9f, 0.2f, 1.0f ); // Vibrant cash green
					}
					DestroyAfterDelay( sparks, 0.5f );
				}

				// Add cash to driver or bot
				if ( targetCart.Driver != null )
				{
					targetCart.Driver.Money += ItemPayout;

					// Register player checkout count
					var manager = Scene.GetAllComponents<GameModeManager>().FirstOrDefault();
					if ( manager != null )
					{
						manager.CheckedOutItemsCount++;
					}

					// Spawn Floating Text above the cart
					var textGo = new GameObject( true );
					textGo.Name = "FloatingCash";
					textGo.WorldPosition = targetCart.WorldPosition + Vector3.Up * 65f + Vector3.Random * 8f;
					var fText = textGo.Components.Create<Code.UI.FloatingText>();
					if ( fText != null )
					{
						fText.Text = $"+${ItemPayout}";
						fText.TextColor = new Color( 0f, 1f, 0.2f, 1f );
					}

					McpBridge.McpEventSystem.Emit( "checkout", new { driver = targetCart.Driver.GameObject.Name, payout = ItemPayout, newBalance = targetCart.Driver.Money } );
				}
				else if ( targetCart.IsBotDriven )
				{
					targetCart.BotMoney += ItemPayout;
					McpBridge.McpEventSystem.Emit( "checkout", new { bot = true, payout = ItemPayout, newBalance = targetCart.BotMoney } );
				}
			}
		}
	}

	private async void DestroyAfterDelay( GameObject obj, float delay )
	{
		await GameTask.DelaySeconds( delay );
		if ( obj.IsValid() )
		{
			obj.Destroy();
		}
	}
}
