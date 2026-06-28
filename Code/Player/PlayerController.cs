using Sandbox;
using Sandbox.Citizen;
using System;
using Code.Economy;

namespace Code.Player;

public enum PlayerState
{
	OnFoot,
	Driving
}

public sealed class PlayerController : Component
{
	[Sync] [Property] public PlayerState State { get; set; } = PlayerState.OnFoot;
	[Property] public float WalkSpeed { get; set; } = 150f;
	[Property] public float RunSpeed { get; set; } = 250f;
	[Property] public float InteractionRange { get; set; } = 120f;
	[Property] public float EyeHeight { get; set; } = 64f;

	[Sync] public int Money { get; set; } = 0;
	[Sync] public int EngineLevel { get; set; } = 1;
	[Sync] public int SuspensionLevel { get; set; } = 1;
	[Sync] public int CapacityLevel { get; set; } = 1;
	[Sync] public int DurabilityLevel { get; set; } = 1;
	[Sync] public int NitroLevel { get; set; } = 1;

	public bool IsShopOpen { get; set; } = false;
	private UpgradeDesk _hoveredUpgradeDesk;

	// Leaning properties for visual juice
	[Property] public float LeanAmountRoll { get; set; } = 0.05f;
	[Property] public float LeanAmountPitch { get; set; } = 0.02f;

	// Grab/Grab-push physical juice
	[Property] public float GrabPushForce { get; set; } = 40f;

	// Sound event names (Hooks)
	[Property] public string SoundEnterCart { get; set; } = "sounds/physics/metal/metal_box_impact_hard.sound";
	[Property] public string SoundExitCart { get; set; } = "sounds/physics/metal/metal_box_impact_soft.sound";

	// References
	[Sync] [Property] public ShoppingCartController ActiveCart { get; set; }
	[Property] public SkinnedModelRenderer ModelRenderer { get; set; }
	
	[Sync] public Angles EyeAngles { get; set; }
	[Sync] public bool IsStunned { get; set; }
	private float _stunTimer;
	private float _stunSparkTimer;
	private float _stunSoundTimer;
	private float _stunStarTimer;
	private Rigidbody _tempRb;
	private BoxCollider _tempCollider;

	private CharacterController _characterController;
	private CitizenAnimationHelper _animHelper;
	private Vector3 _wishVelocity;
	
	// Hover & Highlight state
	private ShoppingCartController _hoveredCart;
	private Color _originalCartTint = Color.White;
	
	// UI Prompt reference
	private Sandbox.UI.CartInteractionPrompt _promptUI;

	protected override void OnStart()
	{
		_characterController = Components.Get<CharacterController>();
		if ( ModelRenderer == null )
		{
			ModelRenderer = Components.GetInDescendants<SkinnedModelRenderer>();
		}
		
		// Setup the animation helper component
		_animHelper = Components.GetOrCreate<CitizenAnimationHelper>();
	}

	protected override void OnUpdate()
	{
		// Find UI Prompt dynamically if not referenced
		if ( _promptUI == null )
		{
			_promptUI = Scene.GetAllComponents<Sandbox.UI.CartInteractionPrompt>().FirstOrDefault();
		}

		if ( IsProxy )
		{
			// Apply animations and procedural leaning for other players
			UpdateAnimations();
			if ( State == PlayerState.Driving && ActiveCart != null )
			{
				ApplyProceduralLean();
			}
			return;
		}

		if ( IsStunned )
		{
			_stunTimer -= Time.Delta;
			if ( ModelRenderer != null )
			{
				float speed = _tempRb != null ? _tempRb.Velocity.Length : 0f;
				float rollSpeed = speed > 10f ? speed * 2.0f : 0f;
				float rollAngle = Time.Now * rollSpeed;

				ModelRenderer.LocalRotation = Rotation.FromPitch( 85f ) * Rotation.FromRoll( rollAngle );
				ModelRenderer.LocalPosition = new Vector3( 0f, 0f, -25f );
			}

			// Spawn dragging sparks and screech sounds while sliding
			if ( _tempRb != null && _tempRb.Velocity.Length > 50f )
			{
				_stunSparkTimer -= Time.Delta;
				if ( _stunSparkTimer <= 0 )
				{
					_stunSparkTimer = 0.08f;
					var activeCart = Scene.GetAllComponents<ShoppingCartController>().FirstOrDefault();
					if ( activeCart != null && activeCart.CollisionSparkPrefab != null )
					{
						var sparks = activeCart.CollisionSparkPrefab.Clone( WorldPosition + Vector3.Down * 12f );
						DestroySparksAfterDelay( sparks, 1.0f );
					}
				}
				
				_stunSoundTimer -= Time.Delta;
				if ( _stunSoundTimer <= 0 && _tempRb.Velocity.Length > 80f )
				{
					_stunSoundTimer = 0.45f;
					var activeCart = Scene.GetAllComponents<ShoppingCartController>().FirstOrDefault();
					if ( activeCart != null && !string.IsNullOrEmpty( activeCart.SoundBrokenWheelScrape ) )
					{
						Sound.Play( activeCart.SoundBrokenWheelScrape, WorldPosition );
					}
				}
			}

			// Spawn rotating dizzy stars above the player's head
			_stunStarTimer -= Time.Delta;
			if ( _stunStarTimer <= 0 )
			{
				_stunStarTimer = 0.2f;
				var activeCart = Scene.GetAllComponents<ShoppingCartController>().FirstOrDefault();
				if ( activeCart != null && activeCart.CollisionSparkPrefab != null )
				{
					float angle = Time.Now * 6f;
					var starPos = WorldPosition + Vector3.Up * 35f + new Vector3( MathF.Cos( angle ) * 12f, MathF.Sin( angle ) * 12f, MathF.Sin( Time.Now * 10f ) * 3f );
					var star = activeCart.CollisionSparkPrefab.Clone( starPos );
					var r = star.Components.GetInDescendantsOrSelf<ModelRenderer>();
					if ( r != null ) r.Tint = new Color( 1.0f, 0.9f, 0.1f );
					DestroySparksAfterDelay( star, 0.6f );
				}
			}

			if ( _stunTimer <= 0 )
			{
				RecoverFromEjection();
			}
			_wishVelocity = Vector3.Zero;
			UpdateAnimations();
			return;
		}

		// Mouse input to look around
		var look = Input.AnalogLook;
		var ee = EyeAngles;
		ee.pitch += look.pitch;
		ee.yaw += look.yaw;
		ee.pitch = ee.pitch.Clamp( -89f, 89f );
		EyeAngles = ee;

		// State handling
		switch ( State )
		{
			case PlayerState.OnFoot:
				HandleOnFootUpdate();
				break;

			case PlayerState.Driving:
				HandleDrivingUpdate();
				break;
		}

		// Update UI Prompt visibility
		if ( _promptUI != null )
		{
			if ( State == PlayerState.OnFoot )
			{
				if ( _hoveredCart != null )
				{
					_promptUI.PromptText = "ARABAYI SÜR";
					_promptUI.IsActive = true;
				}
				else if ( _hoveredUpgradeDesk != null )
				{
					_promptUI.PromptText = "MARKET MAĞAZASI";
					_promptUI.IsActive = true;
				}
				else
				{
					_promptUI.IsActive = false;
				}
			}
			else
			{
				_promptUI.IsActive = false;
			}
		}

		// Animations update
		UpdateAnimations();
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;
		if ( IsStunned ) return;

		if ( State == PlayerState.OnFoot )
		{
			HandleMovementFixedUpdate();
		}
	}

	private void HandleMovementFixedUpdate()
	{
		if ( _characterController != null )
		{
			_characterController.Accelerate( _wishVelocity );
			_characterController.Move();
			
			// Turn body toward move direction if moving
			if ( _wishVelocity.Length > 0.1f )
			{
				var targetRotation = Rotation.LookAt( _wishVelocity.WithZ( 0 ).Normal, Vector3.Up );
				WorldRotation = Rotation.Slerp( WorldRotation, targetRotation, Time.Delta * 8f );
			}
		}
	}

	private void HandleOnFootUpdate()
	{
		// If shop panel is open, block movement and interaction
		if ( IsShopOpen )
		{
			_wishVelocity = Vector3.Zero;
			if ( Input.Pressed( "menu" ) )
			{
				CloseShop();
			}
			return;
		}

		// Calculate movement direction relative to camera/eye angles
		var forward = EyeAngles.ToRotation().Forward.WithZ( 0 ).Normal;
		var right = EyeAngles.ToRotation().Right.WithZ( 0 ).Normal;

		float moveFwd = Input.Down( "forward" ) ? 1f : (Input.Down( "backward" ) ? -1f : 0f);
		float moveLeft = Input.Down( "left" ) ? 1f : (Input.Down( "right" ) ? -1f : 0f);

		_wishVelocity = (forward * moveFwd + right * -moveLeft).Normal;
		float currentSpeed = Input.Down( "run" ) ? RunSpeed : WalkSpeed;
		_wishVelocity *= currentSpeed;

		// precise Raycast hover check
		UpdateInteractionHover();

		// Enter Cart
		if ( Input.Pressed( "use" ) )
		{
			if ( _hoveredCart != null )
			{
				EnterCart( _hoveredCart );
			}
			else if ( _hoveredUpgradeDesk != null )
			{
				OpenShop();
			}
		}
	}

	private void HandleDrivingUpdate()
	{
		_wishVelocity = Vector3.Zero;
		ClearHoverState();

		// Interaction (Exit Cart)
		if ( Input.Pressed( "use" ) || Input.Pressed( "jump" ) )
		{
			ExitCart();
		}
		
		ApplyProceduralLean();
	}

	private void UpdateInteractionHover()
	{
		var startPos = WorldPosition + Vector3.Up * EyeHeight;
		var direction = EyeAngles.ToRotation().Forward;

		var tr = Scene.Trace.Ray( startPos, startPos + direction * InteractionRange )
			.WithoutTags( "player" )
			.Run();

		ShoppingCartController foundCart = null;
		UpgradeDesk foundDesk = null;

		if ( tr.Hit && tr.GameObject != null )
		{
			foundCart = tr.GameObject.Components.GetInAncestorsOrSelf<ShoppingCartController>();
			foundDesk = tr.GameObject.Components.GetInAncestorsOrSelf<UpgradeDesk>();
		}

		if ( foundCart != null && foundCart.Driver != null )
		{
			foundCart = null;
		}

		if ( foundCart != _hoveredCart )
		{
			ClearHoverState();

			if ( foundCart != null )
			{
				_hoveredCart = foundCart;
				
				var renderer = _hoveredCart.GameObject.Components.GetInDescendantsOrSelf<ModelRenderer>();
				if ( renderer != null )
				{
					_originalCartTint = renderer.Tint;
					renderer.Tint = new Color( 0.4f, 0.8f, 1.0f, 1.0f );
				}
			}
		}

		_hoveredUpgradeDesk = foundDesk;
	}

	private void ClearHoverState()
	{
		if ( _hoveredCart != null )
		{
			var renderer = _hoveredCart.GameObject.Components.GetInDescendantsOrSelf<ModelRenderer>();
			if ( renderer != null )
			{
				renderer.Tint = _originalCartTint;
			}
			_hoveredCart = null;
		}
	}

	private void ApplyProceduralLean()
	{
		if ( ActiveCart == null ) return;

		float turnInput = ActiveCart.CurrentTurnInput;
		Vector3 cartVelocity = ActiveCart.CartVelocity;
		float speed = cartVelocity.Length;

		float targetRoll = -turnInput * speed * LeanAmountRoll;
		targetRoll = targetRoll.Clamp( -12f, 12f );

		var localVelocity = ActiveCart.WorldRotation.Inverse * cartVelocity;
		float targetPitch = localVelocity.x * LeanAmountPitch;
		targetPitch = targetPitch.Clamp( -8f, 8f );

		var targetRotation = Rotation.From( targetPitch, 0f, targetRoll );
		GameObject.LocalRotation = Rotation.Lerp( GameObject.LocalRotation, targetRotation, Time.Delta * 10f );

		GameObject.LocalPosition = GameObject.LocalPosition.LerpTo( new Vector3( -55f, 0f, 0f ), Time.Delta * 10f );
	}

	private void UpdateAnimations()
	{
		if ( ModelRenderer == null || _animHelper == null ) return;

		// Link skinned model renderer
		_animHelper.Target = ModelRenderer;

		if ( IsStunned )
		{
			_animHelper.WithVelocity( Vector3.Zero );
			_animHelper.WithWishVelocity( Vector3.Zero );
			_animHelper.IsGrounded = false;
			_animHelper.HoldType = CitizenAnimationHelper.HoldTypes.None;
			return;
		}

		if ( State == PlayerState.Driving && ActiveCart != null )
		{
			// Player runs behind the cart based on cart speed
			_animHelper.WithVelocity( ActiveCart.CartVelocity );
			_animHelper.WithWishVelocity( ActiveCart.CartVelocity.Normal * ActiveCart.CartVelocity.Length );
			_animHelper.IsGrounded = true;
			_animHelper.HoldType = CitizenAnimationHelper.HoldTypes.HoldItem;
		}
		else
		{
			// Standard walking/running animations
			var velocity = _characterController != null ? _characterController.Velocity : Vector3.Zero;
			_animHelper.WithVelocity( velocity );
			_animHelper.WithWishVelocity( _wishVelocity );
			_animHelper.IsGrounded = _characterController != null ? _characterController.IsOnGround : true;
			_animHelper.HoldType = CitizenAnimationHelper.HoldTypes.None;
		}
	}

	public void EnterCart( ShoppingCartController cart )
	{
		ClearHoverState();
		ActiveCart = cart;
		State = PlayerState.Driving;
		cart.Driver = this;
		ApplyUpgradesToCart( cart );

		if ( _characterController != null )
		{
			_characterController.Enabled = false;
		}

		GameObject.Parent = cart.GameObject;
		GameObject.LocalPosition = new Vector3( -55f, 0f, 0f );
		GameObject.LocalRotation = Rotation.Identity;

		// Add forward push force for grab juice
		cart.Push( cart.WorldRotation.Forward * GrabPushForce );

		if ( !string.IsNullOrEmpty( SoundEnterCart ) )
		{
			Sound.Play( SoundEnterCart, WorldPosition );
		}
	}

	public void ExitCart()
	{
		if ( ActiveCart == null ) return;

		var cart = ActiveCart;
		ActiveCart = null;
		State = PlayerState.OnFoot;
		cart.Driver = null;

		GameObject.Parent = null;

		if ( _characterController != null )
		{
			_characterController.Enabled = true;
		}

		WorldPosition = cart.WorldPosition - cart.WorldRotation.Forward * 80f + Vector3.Up * 5f;
		WorldRotation = cart.WorldRotation;

		if ( !string.IsNullOrEmpty( SoundExitCart ) )
		{
			Sound.Play( SoundExitCart, WorldPosition );
		}
	}

	public void EjectFromCart( Vector3 force )
	{
		if ( IsStunned ) return;
		
		// If driving, exit the cart first
		if ( State == PlayerState.Driving )
		{
			ExitCartEjection();
		}
		
		IsStunned = true;
		
		// Stun duration scales with velocity length (longer stun on faster speeds)
		float ejectSpeed = force.Length;
		_stunTimer = ( ejectSpeed / 120f ).Clamp( 2.0f, 4.5f );
		_stunSparkTimer = 0f;
		_stunSoundTimer = 0f;
		_stunStarTimer = 0f;
		
		if ( _characterController != null )
		{
			_characterController.Enabled = false;
		}
		
		// Add temporary physical bodies for tumbling
		_tempRb = Components.GetOrCreate<Rigidbody>();
		_tempCollider = Components.GetOrCreate<BoxCollider>();
		_tempCollider.Scale = new Vector3( 32f, 32f, 32f ); // smaller box for tumbling
		
		if ( _tempRb != null )
		{
			_tempRb.Gravity = true;
			_tempRb.LinearDamping = 0.8f;
			_tempRb.AngularDamping = 0.5f;
			_tempRb.Velocity = force;
			_tempRb.AngularVelocity = Vector3.Random * 15f;
		}
	}
	
	private void ExitCartEjection()
	{
		if ( ActiveCart == null ) return;

		var cart = ActiveCart;
		ActiveCart = null;
		State = PlayerState.OnFoot;
		cart.Driver = null;

		GameObject.Parent = null;
	}

	private void RecoverFromEjection()
	{
		IsStunned = false;
		
		// Remove physical components
		if ( _tempRb != null ) _tempRb.Destroy();
		if ( _tempCollider != null ) _tempCollider.Destroy();
		
		_tempRb = null;
		_tempCollider = null;
		
		// Reset model positions
		if ( ModelRenderer != null )
		{
			ModelRenderer.LocalRotation = Rotation.Identity;
			ModelRenderer.LocalPosition = Vector3.Zero;
		}
		
		if ( _characterController != null )
		{
			_characterController.Enabled = true;
		}
		
		// Snap player to stand upright
		WorldRotation = Rotation.FromYaw( WorldRotation.Angles().yaw );
	}

	private async void DestroySparksAfterDelay( GameObject obj, float delay )
	{
		await GameTask.DelaySeconds( delay );
		if ( obj.IsValid() )
		{
			obj.Destroy();
		}
	}

	private void OpenShop()
	{
		IsShopOpen = true;
		Mouse.Visibility = MouseVisibility.Visible;

		var shopUI = Scene.GetAllComponents<Sandbox.UI.UpgradeShopPanel>().FirstOrDefault();
		if ( shopUI != null )
		{
			shopUI.IsActive = true;
		}
	}

	private void CloseShop()
	{
		IsShopOpen = false;
		Mouse.Visibility = MouseVisibility.Hidden;

		var shopUI = Scene.GetAllComponents<Sandbox.UI.UpgradeShopPanel>().FirstOrDefault();
		if ( shopUI != null )
		{
			shopUI.IsActive = false;
		}
	}

	public void ApplyUpgradesToCart( ShoppingCartController cart )
	{
		if ( cart == null ) return;

		// 1. Engine
		cart.Acceleration = EngineLevel switch
		{
			1 => 600f,
			2 => 750f,
			3 => 900f,
			4 => 1100f,
			5 => 1350f,
			_ => 600f
		};
		cart.MaxSpeed = EngineLevel switch
		{
			1 => 350f,
			2 => 380f,
			3 => 410f,
			4 => 440f,
			5 => 480f,
			_ => 350f
		};

		// 2. Suspension (Visual + Physical Raycast spring upgrades)
		cart.SpringConstant = SuspensionLevel switch
		{
			1 => 160f,
			2 => 220f,
			3 => 300f,
			4 => 400f,
			5 => 550f,
			_ => 160f
		};
		cart.WheelSuspensionStiffness = SuspensionLevel switch
		{
			1 => 28000f,
			2 => 34000f,
			3 => 41000f,
			4 => 49000f,
			5 => 58000f,
			_ => 28000f
		};
		cart.WheelSuspensionDamping = SuspensionLevel switch
		{
			1 => 2200f,
			2 => 2600f,
			3 => 3100f,
			4 => 3700f,
			5 => 4400f,
			_ => 2200f
		};

		// 3. Capacity
		cart.MaxItems = CapacityLevel switch
		{
			1 => 20,
			2 => 25,
			3 => 30,
			4 => 38,
			5 => 50,
			_ => 20
		};

		// 4. Durability
		cart.NormalGrip = DurabilityLevel switch
		{
			1 => 7.0f,
			2 => 7.5f,
			3 => 8.0f,
			4 => 8.5f,
			5 => 9.0f,
			_ => 7.0f
		};

		// 5. Nitro
		cart.NitroRechargeRate = NitroLevel switch
		{
			1 => 12f,
			2 => 15f,
			3 => 18f,
			4 => 22f,
			5 => 26f,
			_ => 12f
		};
		cart.NitroBurnRate = NitroLevel switch
		{
			1 => 25f,
			2 => 22f,
			3 => 19f,
			4 => 16f,
			5 => 13f,
			_ => 25f
		};
	}

	public float GetWheelDamageReduction()
	{
		return DurabilityLevel switch
		{
			1 => 1.0f,
			2 => 0.75f,
			3 => 0.50f,
			4 => 0.30f,
			5 => 0.10f,
			_ => 1.0f
		};
	}

	protected override void OnDestroy()
	{
		ClearHoverState();
	}

	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = Color.Yellow.WithAlpha( 0.15f );
		Gizmo.Draw.LineSphere( Vector3.Zero, InteractionRange );
	}
}
