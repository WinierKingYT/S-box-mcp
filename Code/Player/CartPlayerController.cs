using Sandbox;
using System;

public sealed class CartPlayerController : Component
{
	[Property] public GameObject CartPrefab { get; set; }
	[Property] public float RespawnTime { get; set; } = 3f;

	public ShoppingCartController Cart { get; set; }
	public bool IsAlive { get; set; } = true;

	private GameObject _cartInstance;
	private Vector3 _spawnPosition;

	protected override void OnStart()
	{
		_spawnPosition = Transform.Position;

		if ( IsProxy ) return;

		SpawnCart();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( !IsAlive ) return;

		// Look input is handled by CartController
	}

	private void SpawnCart()
	{
		if ( _cartInstance != null )
		{
			_cartInstance.Destroy();
		}

		_cartInstance = CartPrefab.Clone( Transform.Position + Transform.Rotation.Forward * 80f );
		_cartInstance.BreakFromPrefab();
		_cartInstance.NetworkSpawn( Network.OwnerConnection );

		Cart = _cartInstance.Components.Get<ShoppingCartController>();
	}

	public void Respawn()
	{
		if ( !IsAlive )
		{
			IsAlive = true;
			SpawnCart();
		}
	}
}
