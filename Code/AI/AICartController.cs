using Sandbox;
using System;
using System.Linq;
using Code.Core;
using Code.Economy;
using Code.Player;
using PlayerController = Code.Player.PlayerController;

namespace Code.AI;

public sealed class AICartController : Component
{
	private ShoppingCartController _cart;
	private float _stuckTimer;
	private float _reverseDuration;
	private float _reverseDirection = 1f;

	protected override void OnStart()
	{
		_cart = Components.Get<ShoppingCartController>();
		if ( _cart != null )
		{
			_cart.IsBotDriven = true;
			_cart.BotName = GetRandomBotName();
		}
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || _cart == null ) return;

		// 1. stuck detection & recovery
		if ( _reverseDuration > 0f )
		{
			_reverseDuration -= Time.Delta;
			_cart.AiMoveFwd = -1f;
			_cart.AiMoveTurn = _reverseDirection;
			return;
		}

		if ( _cart.CartVelocity.Length < 15f )
		{
			_stuckTimer += Time.Delta;
			if ( _stuckTimer > 1.2f )
			{
				_reverseDuration = 1.6f;
				_reverseDirection = Random.Shared.Float() > 0.5f ? 1f : -1f;
				_stuckTimer = 0f;
				return;
			}
		}
		else
		{
			_stuckTimer = 0f;
		}

		// 2. target selection (Shelf with items vs Checkout Register vs Player Ramming)
		Vector3 targetPos = Vector3.Zero;
		bool hasTarget = false;
		bool huntPlayer = false;

		// Player Hunt: if player is driving nearby and bot sepet is not full, bot hunts player!
		var player = Scene.GetAllComponents<PlayerController>().FirstOrDefault( p => !p.IsProxy );
		ShoppingCartController playerCart = player?.ActiveCart;
		if ( playerCart != null && _cart.ItemCount < _cart.MaxItems )
		{
			float distToPlayer = ( playerCart.WorldPosition - WorldPosition ).Length;
			if ( distToPlayer < 250f )
			{
				targetPos = playerCart.WorldPosition;
				hasTarget = true;
				huntPlayer = true;
			}
		}

		if ( !huntPlayer )
		{
			var shelves = Scene.GetAllComponents<SupermarketShelf>().Where( s => s.HasItem ).ToList();
			bool shouldCheckout = _cart.ItemCount >= 5 || ( _cart.ItemCount > 0 && shelves.Count == 0 );

			if ( shouldCheckout )
			{
				// Target the closest Checkout Register
				var registers = Scene.GetAllComponents<CheckoutRegister>().ToList();
				if ( registers.Count > 0 )
				{
					var closestRegister = registers.OrderBy( r => ( r.WorldPosition - WorldPosition ).Length ).First();
					targetPos = closestRegister.WorldPosition;
					hasTarget = true;
				}
			}
			else if ( shelves.Count > 0 )
			{
				// Target the closest Shelf with items
				var closestShelf = shelves.OrderBy( s => ( s.WorldPosition - WorldPosition ).Length ).First();
				targetPos = closestShelf.WorldPosition;
				hasTarget = true;
			}
		}

		if ( !hasTarget )
		{
			// Idle or wander
			_cart.AiMoveFwd = 0f;
			_cart.AiMoveTurn = 0f;
			_cart.AiDriftInput = false;
			_cart.AiWantNitro = false;
			return;
		}

		// 3. steering & movement logic
		var direction = ( targetPos - WorldPosition ).WithZ( 0f ).Normal;
		var localDir = WorldRotation.Inverse * direction;
		float angle = MathF.Atan2( localDir.y, localDir.x ) * ( 180f / MathF.PI );
		float distance = ( targetPos - WorldPosition ).Length;

		// Steering angle
		if ( angle > 10f )
		{
			_cart.AiMoveTurn = 1f; // Turn Right
		}
		else if ( angle < -10f )
		{
			_cart.AiMoveTurn = -1f; // Turn Left
		}
		else
		{
			_cart.AiMoveTurn = 0f;
		}

		// Acceleration
		if ( localDir.x > -0.2f )
		{
			_cart.AiMoveFwd = 1f;
		}
		else
		{
			_cart.AiMoveFwd = -1f; // Reverse if target is way behind
		}

		// Nitro on straightaways or when hunting player
		_cart.AiWantNitro = ( ( distance > 320f && MathF.Abs( angle ) < 15f ) || huntPlayer ) && !_cart.IsOverheated;

		// Drift on sharp corners
		_cart.AiDriftInput = MathF.Abs( angle ) > 35f && _cart.CartVelocity.Length > 160f && distance > 100f;
	}

	private string GetRandomBotName()
	{
		string[] names = { "WinierKing", "FarukBot", "TurboAlıcı", "KasaFaresi", "SepetUstası", "KaosSürücüsü" };
		return names[Random.Shared.Next( 0, names.Length )];
	}
}
