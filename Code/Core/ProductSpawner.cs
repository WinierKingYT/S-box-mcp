using Sandbox;
using System;

public sealed class ProductSpawner : Component
{
	[Property] public GameObject ItemPrefab { get; set; }
	[Property] public int ItemsPerShelf { get; set; } = 6;
	[Property] public float SpawnRadius { get; set; } = 50f;

	public void SpawnItemsOnShelf( GameObject shelf )
	{
		var shelfBounds = shelf.GetBounds();
		var shelfPos = shelfBounds.Center;
		var shelfSize = shelfBounds.Size;

		int cols = 3;
		int rows = 2;

		for ( int i = 0; i < cols && i * rows < ItemsPerShelf; i++ )
		{
			for ( int j = 0; j < rows && (i * rows + j) < ItemsPerShelf; j++ )
			{
				float xOffset = (i / (float)(cols - 1)) * shelfSize.x - shelfSize.x * 0.5f;
				float zOffset = (j / (float)(rows - 1)) * shelfSize.z * 0.5f;

				if ( cols <= 1 ) xOffset = 0;
				if ( rows <= 1 ) zOffset = 0;

				var spawnPos = shelfPos + new Vector3( xOffset, 0, zOffset );

				SpawnItem( spawnPos, shelf );
			}
		}
	}

	private void SpawnItem( Vector3 position, GameObject shelf )
	{
		if ( ItemPrefab == null ) return;

		var item = ItemPrefab.Clone( position + Vector3.Random * SpawnRadius * 0.3f );
		item.BreakFromPrefab();
		item.NetworkSpawn();

		var cartItem = item.Components.Get<CartItem>();
		if ( cartItem != null )
		{
			cartItem.Value = GetRandomValueForFloor();
		}

		item.Tags.Add( "pickup", "item" );

		var rb = item.Components.Get<Rigidbody>();
		if ( rb != null )
		{
			rb.Gravity = true;
		}
	}

	private float GetRandomValueForFloor()
	{
		var gm = Components.Get<BlackFridayGameManager>();
		int day = gm != null ? gm.CurrentDay : 1;

		return day switch
		{
			<= 3 => Random.Shared.Float( 5f, 30f ),
			<= 6 => Random.Shared.Float( 20f, 80f ),
			_ => Random.Shared.Float( 50f, 200f )
		};
	}

	public void ClearShelf( GameObject shelf )
	{
		var bounds = shelf.GetBounds();
		var items = Scene.FindInPhysics( new Sphere( bounds.Center, bounds.Size.Length * 0.8f ) );
		foreach ( var obj in items )
		{
			if ( obj.Tags.Has( "pickup" ) )
			{
				obj.Destroy();
			}
		}
	}

	public void RespawnAll( GameObject[] shelves )
	{
		foreach ( var shelf in shelves )
		{
			ClearShelf( shelf );
			SpawnItemsOnShelf( shelf );
		}
	}
}
