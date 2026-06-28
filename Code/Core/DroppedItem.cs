using Sandbox;
using System;

namespace Code.Core;

public sealed class DroppedItem : Component
{
	[Property] public Model ItemModel { get; set; }
	
	[Sync] public bool IsBeingCollected { get; set; }
	[Sync] public ShoppingCartController TargetCart { get; set; }

	private Rigidbody _rb;
	private ModelRenderer _renderer;

	protected override void OnStart()
	{
		_rb = Components.GetOrCreate<Rigidbody>();
		_renderer = Components.GetOrCreate<ModelRenderer>();
		
		int type = Random.Shared.Next( 0, 3 );
		if ( type == 0 )
		{
			// Cereal box shape
			ItemModel = Model.Load( "models/dev/box.vmdl" );
			LocalScale = new Vector3( Random.Shared.Float( 0.22f, 0.3f ), Random.Shared.Float( 0.45f, 0.55f ), Random.Shared.Float( 0.65f, 0.75f ) );
			var col = Components.GetOrCreate<BoxCollider>();
			col.Scale = Vector3.One;
		}
		else if ( type == 1 )
		{
			// Soda can shape (box collider used for physics stability on floor)
			ItemModel = Model.Load( "models/dev/cylinder.vmdl" );
			LocalScale = new Vector3( Random.Shared.Float( 0.25f, 0.35f ), Random.Shared.Float( 0.25f, 0.35f ), Random.Shared.Float( 0.55f, 0.65f ) );
			var col = Components.GetOrCreate<BoxCollider>();
			col.Scale = Vector3.One;
		}
		else
		{
			// Fruit/Sphere shape
			ItemModel = Model.Load( "models/dev/sphere.vmdl" );
			LocalScale = Vector3.One * Random.Shared.Float( 0.35f, 0.48f );
			var col = Components.GetOrCreate<SphereCollider>();
			col.Radius = 18f;
		}

		_renderer.Model = ItemModel;
		_renderer.Tint = Color.Random;
		
		if ( _rb != null )
		{
			_rb.Gravity = true;
			_rb.LinearDamping = 0.5f;
			_rb.AngularDamping = 0.5f;
		}
	}
	
	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		if ( IsBeingCollected )
		{
			if ( !TargetCart.IsValid() || TargetCart.Driver == null || TargetCart.ItemCount >= TargetCart.MaxItems )
			{
				// Target is no longer valid or cart is full
				IsBeingCollected = false;
				TargetCart = null;
				if ( _rb != null )
				{
					_rb.Gravity = true;
					_rb.MotionEnabled = true;
				}
				return;
			}

			// Fly towards the shopping cart's basket center
			var basketCenter = TargetCart.WorldPosition + Vector3.Up * 15f;
			var direction = basketCenter - WorldPosition;
			float distance = direction.Length;
			
			// Visual spin & shrink in magnet collection mode
			LocalRotation *= Rotation.FromYaw( 200f * Time.Delta );
			LocalScale = LocalScale.LerpTo( Vector3.Zero, Time.Delta * 4f );

			if ( distance < 35f )
			{
				// Successfully picked up and returned to cart
				TargetCart.ItemCount++;
				
				// Play a musical pickup sound cue
				TargetCart.PlayCollectionSound( WorldPosition );

				// Spawn green collection sparks
				if ( TargetCart.CollisionSparkPrefab != null )
				{
					var sparks = TargetCart.CollisionSparkPrefab.Clone( WorldPosition );
					var r = sparks.Components.GetInDescendantsOrSelf<ModelRenderer>();
					if ( r != null ) r.Tint = new Color( 0.2f, 1.0f, 0.4f );
					DestroySparksAfterDelay( sparks, 1.0f );
				}
				
				GameObject.Destroy();
				return;
			}
			
			// Magnet pull force: accelerate towards basket
			if ( _rb != null )
			{
				_rb.Gravity = false;
				// Pull speed increases as it gets closer
				float pullSpeed = 450f + ( ( 180f - distance ).Clamp( 0f, 180f ) * 2.5f );
				_rb.Velocity = direction.Normal * pullSpeed;
			}
			return;
		}
		
		// Search for nearby active shopping carts with drivers
		var carts = Scene.GetAllComponents<ShoppingCartController>();
		foreach ( var cart in carts )
		{
			if ( cart.Driver != null && cart.ItemCount < cart.MaxItems )
			{
				float dist = ( cart.WorldPosition - WorldPosition ).Length;
				if ( dist < 160f )
				{
					IsBeingCollected = true;
					TargetCart = cart;
					if ( _rb != null )
					{
						_rb.MotionEnabled = true; // Unfreeze from shelf
					}
					break;
				}
			}
		}
	}

	private async void DestroySparksAfterDelay( GameObject obj, float delay )
	{
		await GameTask.DelaySeconds( delay );
		if ( obj.IsValid() )
		{
			obj.Destroy();
		}
	}
}
