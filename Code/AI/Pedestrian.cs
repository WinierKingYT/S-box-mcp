using Sandbox;
using Sandbox.Citizen;
using System;
using Code.Core;

public sealed class Pedestrian : Component, Component.ICollisionListener
{
	[Property] public Model PedestrianModel { get; set; }
	[Property] public string SoundScream { get; set; } = "sounds/physics/metal/metal_screech.sound";
	
	private CharacterController _characterController;
	private SkinnedModelRenderer _modelRenderer;
	private CitizenAnimationHelper _animHelper;
	
	private Vector3 _targetPos;
	private bool _isKnockedBack;
	private float _knockedBackTimer;
	private Rigidbody _tempRb;
	private BoxCollider _tempCollider;
	private float _dodgeDustTimer;

	protected override void OnStart()
	{
		_characterController = Components.GetOrCreate<CharacterController>();
		_modelRenderer = Components.GetOrCreate<SkinnedModelRenderer>();
		_animHelper = Components.GetOrCreate<CitizenAnimationHelper>();
		
		if ( PedestrianModel == null )
		{
			PedestrianModel = Model.Load( "models/citizen/citizen.vmdl" );
		}
		_modelRenderer.Model = PedestrianModel;
		_animHelper.Target = _modelRenderer;

		ChooseNewTargetPosition();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		if ( _isKnockedBack )
		{
			_knockedBackTimer += Time.Delta;
			
			// Shrink and disappear slowly after 2.5 seconds
			if ( _knockedBackTimer > 2.5f )
			{
				float scale = ( ( 3.0f - _knockedBackTimer ) / 0.5f ).Clamp( 0f, 1f );
				LocalScale = Vector3.One * scale;
			}
			
			if ( _knockedBackTimer >= 3.0f )
			{
				GameObject.Destroy();
			}
			return;
		}

		// Check for nearby fast carts to dodge (Dodge Attempt)
		var incomingCarts = Scene.GetAllComponents<ShoppingCartController>();
		ShoppingCartController threateningCart = null;
		foreach ( var cart in incomingCarts )
		{
			if ( cart.Driver != null && cart.CartVelocity.Length > 150f )
			{
				var toCart = cart.WorldPosition - WorldPosition;
				float dist = toCart.Length;
				if ( dist < 220f )
				{
					float dot = Vector3.Dot( cart.CartVelocity.Normal, -toCart.Normal );
					if ( dot > 0.4f )
					{
						threateningCart = cart;
						break;
					}
				}
			}
		}

		// Move towards target position or dodge in panic
		var toTarget = _targetPos - WorldPosition;
		toTarget.z = 0; // Keep movement on flat ground
		
		Vector3 wishDir = toTarget.Normal;
		float speed = 80f;

		if ( threateningCart != null )
		{
			var dodgeVector = ( WorldPosition - threateningCart.WorldPosition ).WithZ( 0f ).Normal;
			wishDir = ( dodgeVector + WorldRotation.Right * Random.Shared.Float( -0.3f, 0.3f ) ).Normal;
			speed = 220f; // Panicky dash speed
			
			if ( _animHelper != null )
			{
				_animHelper.TriggerJump(); // Play jump to look panicked
			}

			// Spawn panic dodge dust cloud (Phase 5 Polish)
			_dodgeDustTimer -= Time.Delta;
			if ( _dodgeDustTimer <= 0f )
			{
				_dodgeDustTimer = Random.Shared.Float( 0.08f, 0.16f );
				if ( threateningCart.OverheatSmokePrefab != null )
				{
					var dust = threateningCart.OverheatSmokePrefab.Clone( WorldPosition + Vector3.Down * 2f );
					var r = dust.Components.GetInDescendantsOrSelf<ModelRenderer>();
					if ( r != null )
					{
						r.Tint = new Color( 0.85f, 0.8f, 0.75f, 0.35f );
					}
					
					var dustRb = dust.Components.Get<Rigidbody>();
					if ( dustRb != null )
					{
						dustRb.Velocity = Vector3.Up * 15f + Vector3.Random * 12f;
					}
					
					DestroySparksAfterDelay( dust, 0.5f );
				}
			}
		}
		
		if ( threateningCart == null && toTarget.Length < 30f )
		{
			ChooseNewTargetPosition();
		}
		else
		{
			if ( _characterController != null )
			{
				_characterController.Accelerate( wishDir * speed );
				_characterController.Move();
				
				// Align body with move direction
				if ( _characterController.Velocity.Length > 10f )
				{
					var targetRot = Rotation.LookAt( _characterController.Velocity.WithZ( 0 ).Normal, Vector3.Up );
					WorldRotation = Rotation.Slerp( WorldRotation, targetRot, Time.Delta * 8f );
				}
			}
		}

		// Update walking animations
		if ( _animHelper != null && _characterController != null )
		{
			_animHelper.WithVelocity( _characterController.Velocity );
			_animHelper.WithWishVelocity( wishDir * speed );
			_animHelper.IsGrounded = _characterController.IsOnGround;
		}
	}

	private void ChooseNewTargetPosition()
	{
		// Wander around within a 400 unit radius
		_targetPos = WorldPosition + new Vector3( Random.Shared.Float( -400f, 400f ), Random.Shared.Float( -400f, 400f ), 0f );
	}

	public void HitByCart( Vector3 cartVelocity, float cartSpeed )
	{
		if ( _isKnockedBack ) return;

		_isKnockedBack = true;
		_knockedBackTimer = 0f;

		// Play a scream sound
		if ( !string.IsNullOrEmpty( SoundScream ) )
		{
			Sound.Play( SoundScream, WorldPosition );
		}

		// Spawn a dropped item representing carried groceries
		var itemGo = new GameObject( true );
		itemGo.Name = "PedestrianSpilledItem";
		itemGo.WorldPosition = WorldPosition + Vector3.Up * 25f;
		var droppedItem = itemGo.Components.Create<DroppedItem>();
		
		var itemRb = itemGo.Components.Get<Rigidbody>();
		if ( itemRb != null )
		{
			var scatterDir = ( cartVelocity.Normal + Vector3.Up * 1.5f + Vector3.Random * 0.4f ).Normal;
			itemRb.Velocity = scatterDir * cartSpeed * 0.8f;
		}

		// Disable standard movement controller
		if ( _characterController != null )
		{
			_characterController.Enabled = false;
		}

		// Add temporary physical Rigidbody and BoxCollider to simulate ragdoll tumbling
		_tempRb = Components.Create<Rigidbody>();
		_tempCollider = Components.Create<BoxCollider>();
		_tempCollider.Scale = new Vector3( 32f, 32f, 72f ); // human size box collider

		// Throw the pedestrian physically
		if ( _tempRb != null )
		{
			_tempRb.Gravity = true;
			_tempRb.LinearDamping = 0.2f;
			_tempRb.AngularDamping = 0.2f;
			
			// Fling velocity: combined forward cart direction + upwards lift
			var flingDir = cartVelocity.Normal.WithZ( 0f ).Normal;
			var forceVector = ( flingDir * cartSpeed * 1.4f ) + ( Vector3.Up * 220f );
			
			_tempRb.Velocity = forceVector;
			_tempRb.AngularVelocity = Vector3.Random * 20f; // Spin tumble
		}

		// Disable animation updates during flight
		if ( _animHelper != null )
		{
			_animHelper.Enabled = false;
		}
	}

	void Component.ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( !_isKnockedBack ) return;

		float speed = collision.Contact.Speed.Length;
		if ( speed > 80f )
		{
			var otherRb = collision.Other.GameObject.Components.GetInAncestorsOrSelf<Rigidbody>();
			if ( otherRb == null ) // Hit a static world structure
			{
				var activeCart = Scene.GetAllComponents<ShoppingCartController>().FirstOrDefault();
				if ( activeCart != null )
				{
					if ( !string.IsNullOrEmpty( activeCart.SoundCrashHard ) )
					{
						Sound.Play( activeCart.SoundCrashHard, collision.Contact.Point );
					}
					
					if ( activeCart.CollisionSparkPrefab != null )
					{
						var sparks = activeCart.CollisionSparkPrefab.Clone( collision.Contact.Point, Rotation.LookAt( collision.Contact.Normal ) );
						DestroySparksAfterDelay( sparks, 1.2f );
					}
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
