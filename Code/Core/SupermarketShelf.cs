using Sandbox;
using System;

namespace Code.Core;

public sealed class SupermarketShelf : Component
{
	[Property] public float RespawnCooldown { get; set; } = 10f;
	
	private GameObject _spawnedItem;
	private float _respawnTimer;
	private PointLight _statusLight;

	public bool HasItem => _spawnedItem != null && _spawnedItem.IsValid();

	protected override void OnStart()
	{
		// Spawn status light indicator
		var lightObj = new GameObject();
		lightObj.Parent = GameObject;
		lightObj.LocalPosition = Vector3.Up * 32f;
		_statusLight = lightObj.Components.Create<PointLight>();
		if ( _statusLight != null )
		{
			_statusLight.Radius = 60f;
		}

		SpawnShelfItem();
	}

	protected override void OnUpdate()
	{
		if ( _statusLight != null )
		{
			bool hasItem = HasItem;
			if ( hasItem )
			{
				_statusLight.LightColor = new Color( 0f, 1f, 0.2f, 1f );
			}
			else
			{
				// Pulsing warning red
				float pulse = 0.5f + MathF.Sin( Time.Now * 8f ) * 0.4f;
				_statusLight.LightColor = new Color( 1f, 0.1f, 0f, 1f ) * pulse;
			}
		}

		if ( IsProxy ) return;

		if ( _spawnedItem == null || !_spawnedItem.IsValid() )
		{
			_respawnTimer -= Time.Delta;
			if ( _respawnTimer <= 0f )
			{
				SpawnShelfItem();
			}
		}
	}

	private void SpawnShelfItem()
	{
		_spawnedItem = new GameObject( true );
		_spawnedItem.Name = "ShelfItem";
		_spawnedItem.WorldPosition = WorldPosition + Vector3.Up * 20f;
		
		// Add DroppedItem component which automatically loads cereal, soda or fruit shape
		var item = _spawnedItem.Components.Create<DroppedItem>();

		// We disable the Rigidbody gravity and freeze it in place so it stays on the shelf until collected!
		var rb = _spawnedItem.Components.Get<Rigidbody>();
		if ( rb != null )
		{
			rb.Gravity = false;
			rb.MotionEnabled = false; // Freeze on shelf
		}

		_respawnTimer = RespawnCooldown;
	}
}
