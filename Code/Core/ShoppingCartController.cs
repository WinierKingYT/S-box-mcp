using Sandbox;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Code.Core;
using Code.Economy;
using Code.Player;

public sealed class ShoppingCartController : Component, Component.ICollisionListener
{
	// Movement & Handling Parameters
	[Property] public float MaxSpeed { get; set; } = 350f;
	[Property] public float Acceleration { get; set; } = 600f;
	[Property] public float TurnSpeed { get; set; } = 4.0f;
	[Property] public float Drag { get; set; } = 2.0f;
	
	// Phase 2: Advanced Steering & Reverse Properties
	[Property] public float Braking { get; set; } = 1000f;
	[Property] public float ReverseSpeed { get; set; } = 150f;
	[Property] public float ReverseAcceleration { get; set; } = 300f;

	// Phase 2: Drift Mechanics
	[Property] public float DriftTurnMultiplier { get; set; } = 1.6f;
	[Property] public float NormalGrip { get; set; } = 7.0f;
	[Property] public float DriftGrip { get; set; } = 1.2f;

	// Phase 2: Drift Boost System (Arcade Style Juice)
	[Property] public float BoostDurationLevel1 { get; set; } = 0.8f;
	[Property] public float BoostDurationLevel2 { get; set; } = 1.4f;
	[Property] public float BoostDurationLevel3 { get; set; } = 2.0f;
	[Property] public float BoostMultiplierLevel1 { get; set; } = 1.25f;
	[Property] public float BoostMultiplierLevel2 { get; set; } = 1.45f;
	[Property] public float BoostMultiplierLevel3 { get; set; } = 1.70f;

	// Visual Wheels & Wobble Juice (Phase 2 Visual Polish)
	[Property] public GameObject WheelFL { get; set; }
	[Property] public GameObject WheelFR { get; set; }
	[Property] public GameObject WheelRL { get; set; }
	[Property] public GameObject WheelRR { get; set; }
	[Property] public float WheelRadius { get; set; } = 8.0f;
	[Property] public float MaxVisualSteerAngle { get; set; } = 35.0f;
	[Property] public float WobbleFrequency { get; set; } = 45.0f;
	[Property] public float MaxWobbleAngle { get; set; } = 12.0f;

	// Basket/Body Procedural Roll/Lean Juice (Phase 2 Juice)
	[Property] public GameObject BasketMesh { get; set; }
	[Property] public float MaxBasketRoll { get; set; } = 6.0f;
	[Property] public float BasketPitchFactor { get; set; } = 0.015f;

	// Phase 3: Capacity and Weight Physics
	[Sync] [Property] public int ItemCount { get; set; }
	[Sync] [Property] public int MaxItems { get; set; } = 20;
	[Property] public float MaxWeightPenalty { get; set; } = 0.35f;
	
	// Item Jiggle Visuals (Phase 3 Juice)
	[Property] public GameObject ItemContainer { get; set; }
	[Property] public float ItemJiggleIntensity { get; set; } = 0.6f;

	// Phase 3: Nitro Boost System & Overheat smoke
	[Sync] [Property] public float NitroFuel { get; set; } = 100f;
	[Property] public float NitroBurnRate { get; set; } = 25f;
	[Property] public float NitroRechargeRate { get; set; } = 12f;
	[Property] public float NitroSpeedMultiplier { get; set; } = 1.45f;
	[Property] public float NitroAccelMultiplier { get; set; } = 1.6f;
	[Property] public GameObject OverheatSmokePrefab { get; set; }
	[Sync] public bool IsNitroActive { get; set; }
	[Sync] public bool IsOverheated { get; set; }

	// Phase 3: Suspension Physics Parameters
	[Property] public float SpringConstant { get; set; } = 160f;
	[Property] public float DampingConstant { get; set; } = 16f;
	[Property] public float MaxSuspensionSag { get; set; } = 5.0f; // Basket sinks by this amount when full

	// Phase 3: Broken Wheel Pull parameters
	[Sync] public float PullDirection { get; set; } = 0f; // -1 for left, 1 for right
	[Sync] public float PullIntensity { get; set; } = 0f;
	[Sync] [Property] public float WheelDamage { get; set; } = 0f;

	// Drift & Collision Particles Prefabs (Phase 2 Juice)
	[Property] public GameObject DriftSparkPrefab { get; set; }
	[Property] public GameObject CollisionSparkPrefab { get; set; }
	[Property] public float SparkSpawnRate { get; set; } = 0.08f;

	// Drift, Boost & Nitro Visual Component Hooks (Phase 2/3 Visual Polish)
	[Property] public List<GameObject> DriftTrails { get; set; } = new();
	[Property] public List<GameObject> BoostVisuals { get; set; } = new();
	[Property] public List<GameObject> NitroVisuals { get; set; } = new();

	// Sound event names (Hooks)
	[Property] public string SoundDriftScreech { get; set; } = "sounds/physics/tires/tire_skid.sound";
	[Property] public string SoundDriftBoost { get; set; } = "sounds/physics/rocket/rocket_boost.sound";
	[Property] public string SoundCrashHard { get; set; } = "sounds/physics/metal/metal_solid_impact_hard.sound";
	[Property] public string SoundLanding { get; set; } = "sounds/physics/metal/metal_box_impact_hard.sound";
	[Property] public string SoundRattle { get; set; } = "sounds/physics/metal/metal_rattle.sound";
	[Property] public string SoundWheelWobbleClick { get; set; } = "sounds/physics/metal/wheel_click.sound";
	[Property] public string SoundNitroLoop { get; set; } = "sounds/physics/rocket/rocket_boost.sound";
	[Property] public string SoundWindLoop { get; set; } = "sounds/physics/wind/wind_rush.sound";
	[Property] public string SoundBrokenWheelScrape { get; set; } = "sounds/physics/metal/metal_screech.sound";

	// Physics stabilization & Mid-air parameters
	[Property] public bool StabilizeUpright { get; set; } = true;
	[Property] public float StabilizerStrength { get; set; } = 0.5f;
	[Property] public float GroundCheckDistance { get; set; } = 32f;
	[Property] public float AirControlMultiplier { get; set; } = 0.7f;

	// Per-Wheel Raycast Suspension tuning
	[Property] public float WheelSuspensionRestLength { get; set; } = 28f;   // Ray cast travel distance
	[Property] public float WheelSuspensionStiffness { get; set; } = 28000f; // Spring force N/m
	[Property] public float WheelSuspensionDamping { get; set; } = 2200f;   // Damping force N/(m/s)

	[Sync] [Property] public Code.Player.PlayerController Driver { get; set; }
	
	// Exposing movement data for procedural leaning, camera, and UI
	[Sync] public float CurrentTurnInput { get; set; }
	[Sync] public Vector3 CartVelocity { get; set; }
	[Sync] public bool IsDrifting { get; set; }
	[Sync] public float DriftCharge { get; set; }
	[Sync] public int DriftBoostLevel { get; set; }
	[Sync] public bool IsBoosting => _driftBoostTimer > 0;
	[Sync] public bool IsGrounded { get; set; } = true;
	[Sync] public float LastImpactSpeed { get; set; }

	[Sync] public bool IsBotDriven { get; set; }
	public float AiMoveFwd { get; set; }
	public float AiMoveTurn { get; set; }
	public bool AiDriftInput { get; set; }
	public bool AiWantNitro { get; set; }
	[Sync] public string BotName { get; set; } = "AICart";
	[Sync] public int BotMoney { get; set; } = 0;
	
	// Surface physics state
	[Sync] public string CurrentSurfaceType { get; set; } = "tile";

	private Rigidbody _rb;
	private float _driftBoostTimer;
	private float _currentBoostMultiplier = 1.0f;
	
	// Wheel rotation state
	private float _wheelRollAngle;
	
	private bool _wasGroundedLastFrame = true;
	private float _airTime;
	private bool _isStomping;
	private float _stompTimeout;

	// Dynamic Rattle/Wobble/Spark timings
	private float _rattleTimer;
	private float _wobbleClickTimer;
	private float _sparkTimer;
	private float _nitroSoundTimer;
	private float _scrapeTimer;
	private float _scrapeSparkTimer;
	private float _wallScrapeTimer;
	private float _smokeTimer;
	private SoundHandle _windSoundHandle;
	private PointLight _nitroGlowLight;

	// Suspension springs state
	private float _suspensionHeight;
	private float _suspensionVelocity;
	private float _suspensionPitch;
	private float _suspensionPitchVelocity;
	private float _suspensionRoll;
	private float _suspensionRollVelocity;
	private Vector3 _lastLocalVelocity;

	// Nitro backfire & lockout state
	private float _backfireTimer;

	// Broken wheel lockup state
	private bool _isWheelLockedUp;
	private float _lockupDurationTimer;
	private float _lockupCheckTimer;
	private float _wheelRollAngleFL;

	// Collection Combo Pitch
	private float _lastCollectionTime;
	private int _collectionComboCount;
	private float _comboWindow = 1.2f;

	// Per-wheel raycast suspension state
	// Order: 0=FL, 1=FR, 2=RL, 3=RR
	private readonly float[] _wheelCompression   = new float[4];
	private readonly float[] _wheelPrevCompress   = new float[4];
	private readonly bool[]  _wheelGrounded       = new bool[4];

	protected override void OnStart()
	{
		_rb = Components.Get<Rigidbody>();
		if ( _rb == null )
		{
			_rb = Components.Create<Rigidbody>();
		}
		
		_rb.Gravity = true;
		_rb.LinearDamping = Drag;
		_rb.AngularDamping = 4.0f;
		
		// Lower Center of Mass to prevent cart from flipping over easily (Stage 3 optimization)
		_rb.OverrideMassCenter = true;
		_rb.MassCenterOverride = new Vector3( 0, 0, -15f );
		_lastLocalVelocity = Vector3.Zero;

		// Auto-locate wheels & basket in children if not set
		FindWheelsAndBasketInChildren();
	}

	private void FindWheelsAndBasketInChildren()
	{
		var children = GameObject.Children;
		foreach ( var child in children )
		{
			string name = child.Name.ToLower();
			if ( name.Contains( "wheel" ) )
			{
				if ( WheelFL == null && (name.Contains( "fl" ) || name.Contains( "front_l" ) || name.Contains( "frontleft" )) ) WheelFL = child;
				else if ( WheelFR == null && (name.Contains( "fr" ) || name.Contains( "front_r" ) || name.Contains( "frontright" )) ) WheelFR = child;
				else if ( WheelRL == null && (name.Contains( "rl" ) || name.Contains( "back_l" ) || name.Contains( "rearleft" )) ) WheelRL = child;
				else if ( WheelRR == null && (name.Contains( "rr" ) || name.Contains( "back_r" ) || name.Contains( "rearright" )) ) WheelRR = child;
			}
			else if ( BasketMesh == null && (name.Contains( "basket" ) || name.Contains( "mesh" ) || name.Contains( "body" ) || name.Contains( "frame" )) )
			{
				BasketMesh = child;
			}
			else if ( ItemContainer == null && (name.Contains( "item" ) || name.Contains( "cargo" ) || name.Contains( "load" )) )
			{
				ItemContainer = child;
			}
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;

		// Perform ground check and update surface type
		CheckGroundedStatus();

		// Apply physical per-wheel raycast spring forces
		if ( _rb != null )
		{
			ApplyWheelRaycastSuspension();
		}

		// Perform upright stabilization if enabled (Stage 3 physics optimization)
		if ( IsGrounded && StabilizeUpright && _rb != null )
		{
			ApplyUprightStabilization();
		}

		// Update drift boost timer
		if ( _driftBoostTimer > 0 )
		{
			_driftBoostTimer -= Time.Delta;
			if ( _driftBoostTimer <= 0 )
			{
				_currentBoostMultiplier = 1.0f;
			}
		}

		// Update Nitro System state (Stage 10)
		UpdateNitroSystem();

		// Toggle boost & nitro visuals based on states
		UpdateBoostVisuals( IsBoosting );
		UpdateNitroVisuals( IsNitroActive );
		UpdateEngineGlowLight();

		if ( Driver == null && !IsBotDriven )
		{
			_rb.LinearDamping = 4.0f;
			_rb.AngularDamping = 4.0f;
			CurrentTurnInput = 0f;
			CartVelocity = Vector3.Zero;
			IsDrifting = false;
			DriftCharge = 0f;
			DriftBoostLevel = 0;
			_driftBoostTimer = 0f;
			_currentBoostMultiplier = 1.0f;
			IsNitroActive = false;
			PullIntensity = 0f;
			UpdateDriftTrails( false );
			UpdateBoostVisuals( false );
			UpdateNitroVisuals( false );
			ResetVisualBasketLean();
			ResetWheelTints();
			ResetItemJiggle();
			if ( _windSoundHandle.IsValid() )
			{
				_windSoundHandle.Stop();
			}
			if ( _nitroGlowLight != null )
			{
				_nitroGlowLight.Enabled = false;
			}
			return;
		}

		_rb.LinearDamping = Drag;
		_rb.AngularDamping = 4.0f;

		float moveFwd = IsBotDriven ? AiMoveFwd : (Input.Down( "forward" ) ? 1f : (Input.Down( "backward" ) ? -1f : 0f));
		float moveTurn = IsBotDriven ? AiMoveTurn : (Input.Down( "left" ) ? -1f : (Input.Down( "right" ) ? 1f : 0f));
		bool driftInput = IsBotDriven ? AiDriftInput : Input.Down( "run" );

		// Calculate speed parameters
		float currentSpeed = _rb.Velocity.Length;
		// Guard MaxSpeed against zero to prevent division-by-zero in speedRatio
		float safeMaxSpeed = MaxSpeed > 0.1f ? MaxSpeed : 0.1f;
		float speedRatio = (currentSpeed / safeMaxSpeed).Clamp( 0f, 1f );
		var forward = WorldRotation.Forward.WithZ( 0 );
		// Guard forward vector against zero-length (can happen when perfectly vertical)
		if ( forward.LengthSquared < 0.001f ) forward = Vector3.Forward;
		forward = forward.Normal;

		// Calculate local acceleration — guard against tiny Time.Delta spikes that cause huge values
		var localVelocity = WorldRotation.Inverse * _rb.Velocity;
		var rawAccel = Time.Delta > 0.001f ? (localVelocity - _lastLocalVelocity) / Time.Delta : Vector3.Zero;
		// Clamp acceleration magnitude to prevent wall-collision impulse spikes from poisoning downstream calculations
		var localAcceleration = rawAccel.ClampLength( 12000f );
		_lastLocalVelocity = localVelocity;

		// Capacity and Weight Modifiers calculation (Stage 9)
		float weightRatio = ((float)ItemCount / MaxItems).Clamp( 0f, 1f );
		float weightSpeedPenalty = 1f - (weightRatio * MaxWeightPenalty);
		float weightAccelPenalty = 1f - (weightRatio * MaxWeightPenalty * 1.3f);
		float weightTurnPenalty = 1f - (weightRatio * MaxWeightPenalty * 0.7f);

		// Dynamically shift Center of Mass based on weight (higher mass center increases roll risk)
		if ( _rb != null )
		{
			_rb.OverrideMassCenter = true;
			_rb.MassCenterOverride = new Vector3( 0f, 0f, -15f + (weightRatio * 18f) );
		}

		// Check for broken wheel lockups
		if ( IsGrounded && currentSpeed > 100f && WheelDamage > 0.15f )
		{
			if ( !_isWheelLockedUp )
			{
				_lockupCheckTimer -= Time.Delta;
				if ( _lockupCheckTimer <= 0 )
				{
					_lockupCheckTimer = 1.0f; // Check every second
					if ( Random.Shared.Float() < 0.08f * WheelDamage )
					{
						_isWheelLockedUp = true;
						_lockupDurationTimer = Random.Shared.Float( 0.25f, 0.6f ) * WheelDamage;
						
						if ( !string.IsNullOrEmpty( SoundDriftScreech ) )
						{
							Sound.Play( SoundDriftScreech, WorldPosition );
						}
					}
				}
			}
			else
			{
				_lockupDurationTimer -= Time.Delta;
				if ( _lockupDurationTimer <= 0 )
				{
					_isWheelLockedUp = false;
					_lockupCheckTimer = Random.Shared.Float( 4f, 8f ); // Cooldown between lockups
				}
				else
				{
					if ( _rb != null )
					{
						var forwardDrag = forward * -250f * WheelDamage * Time.Delta;
						_rb.Velocity += forwardDrag;
					}
				}
			}
		}
		else
		{
			_isWheelLockedUp = false;
		}

		// Process broken wheel pull effects
		UpdateBrokenWheelPull( currentSpeed, speedRatio, weightRatio );

		// 1. Wobbly Wheel Physical Micro-Vibrations (Stage 12 Physics Juice)
		// Applies subtle physical vibration shake to the Rigidbody at high speeds, felt in steering/movement
		if ( IsGrounded && currentSpeed > 100f && WheelFL != null )
		{
			float wobbleFreq = WobbleFrequency * (1f - weightRatio * 0.35f);
			float forceIntensity = 35f + (IsNitroActive ? 55f : 0f);
			var wobbleForce = WorldRotation.Right * MathF.Sin( Time.Now * wobbleFreq ) * speedRatio * forceIntensity;
			_rb.Velocity += wobbleForce * Time.Delta;
		}

		// Dynamic metal rattling sound trigger with weight pitch (Stage 9 Audio Juice)
		UpdateDynamicRattle( currentSpeed, speedRatio, weightRatio );

		// Wobbly wheel clicking sound trigger
		UpdateWobblyWheelClickSound( currentSpeed, speedRatio );

		// Update speed-based wind loop sound (Phase 5 Polish)
		UpdateWindLoop( currentSpeed, speedRatio );

		// Spawn exhaust sparks during boosts (Phase 5 Polish)
		if ( IsBoosting || IsNitroActive )
		{
			SpawnExhaustSparksPeriodically();
		}

		// Dynamic suspension spring bounce with Sag, Pitch & Roll (Stage 11)
		UpdateSuspensionSpring( currentSpeed, speedRatio, weightRatio, localAcceleration );

		// Procedural Item Jiggle inside the basket (Stage 9 Visual Juice)
		UpdateItemJiggle( currentSpeed, speedRatio, weightRatio );

		if ( IsGrounded )
		{
			// Ground Movement Fiziği

			float forwardDot = Vector3.Dot( _rb.Velocity.Normal, forward );
			bool isMovingForward = currentSpeed > 20f && forwardDot > 0.2f;
			bool isMovingBackward = currentSpeed > 20f && forwardDot < -0.2f;

			bool wasDrifting = IsDrifting;
			IsDrifting = (driftInput || CurrentSurfaceType == "wet") && isMovingForward && moveTurn != 0;

			UpdateDriftTrails( IsDrifting );

			if ( IsDrifting )
			{
				SpawnDriftEffectsPeriodically();
			}

			// Arcade Drift Boost System
			if ( IsDrifting )
			{
				if ( CurrentSurfaceType != "wet" )
				{
					DriftCharge += Time.Delta;
					
					if ( DriftCharge >= 2.5f ) DriftBoostLevel = 3;
					else if ( DriftCharge >= 1.5f ) DriftBoostLevel = 2;
					else if ( DriftCharge >= 0.7f ) DriftBoostLevel = 1;
					else DriftBoostLevel = 0;
				}
				else
				{
					DriftCharge = 0f;
					DriftBoostLevel = 0;
				}

				UpdateDriftChargeColors();

				if ( !wasDrifting && !string.IsNullOrEmpty( SoundDriftScreech ) )
				{
					Sound.Play( SoundDriftScreech, WorldPosition );
				}
			}
			else
			{
				if ( wasDrifting && DriftBoostLevel > 0 )
				{
					TriggerDriftBoost( DriftBoostLevel );
				}
				DriftCharge = 0f;
				DriftBoostLevel = 0;
				ResetWheelTints();
			}

			CurrentTurnInput = moveTurn;

			// Surface Physics Modifiers: Carpet slows down, wet floor slips
			float surfaceSpeedMult = 1.0f;
			float surfaceAccelMult = 1.0f;
			float surfaceGripMult = 1.0f;

			if ( CurrentSurfaceType == "carpet" )
			{
				surfaceSpeedMult = 0.65f;
				surfaceAccelMult = 0.8f;
				surfaceGripMult = 1.8f;
			}
			else if ( CurrentSurfaceType == "wet" )
			{
				surfaceSpeedMult = 0.95f;
				surfaceAccelMult = 0.4f;
				// Hydroplaning at high speeds drops grip to near zero
				surfaceGripMult = currentSpeed > 220f ? 0.03f : 0.15f;
				
				if ( currentSpeed > 50f && moveFwd != 0 )
				{
					float spinTorque = MathF.Sin( Time.Now * 9f ) * 1.8f * speedRatio;
					_rb.AngularVelocity += Vector3.Up * spinTorque;
				}
			}

			// Acceleration / Braking Forces (Stage 5 & Stage 7 + Phase 3 Weight & Nitro multipliers)
			float activeAcceleration = Acceleration * _currentBoostMultiplier * surfaceAccelMult * weightAccelPenalty;
			
			if ( IsNitroActive )
			{
				activeAcceleration *= NitroAccelMultiplier;
			}

			if ( moveFwd > 0 )
			{
				if ( isMovingBackward )
				{
					_rb.Velocity += forward * moveFwd * Braking * Time.Delta;
				}
				else
				{
					// Dynamic Engine Power Band: high launch torque off the line, maximum pull in mid-range, soft decay at top speed
					float accelFactor = 1.0f;
					if ( speedRatio < 0.25f )
					{
						accelFactor = 1.15f; // Strong initial launch push
					}
					else if ( speedRatio < 0.75f )
					{
						accelFactor = 1.35f - (speedRatio - 0.25f) * 0.45f; // Mid-range power band peak
					}
					else
					{
						accelFactor = 0.95f * MathF.Pow( 1f - speedRatio, 0.7f ); // Taper off smoothly near max speed
					}
					accelFactor = accelFactor.Clamp( 0.05f, 1.4f );
					_rb.Velocity += forward * moveFwd * (activeAcceleration * accelFactor) * Time.Delta;
				}
			}
			else if ( moveFwd < 0 )
			{
				if ( isMovingForward )
				{
					_rb.Velocity += forward * moveFwd * Braking * Time.Delta;
				}
				else
				{
					float revSpeedRatio = (currentSpeed / ReverseSpeed).Clamp( 0f, 1f );
					float revAccelFactor = 1f - revSpeedRatio;
					_rb.Velocity += forward * moveFwd * (ReverseAcceleration * revAccelFactor) * Time.Delta;
				}
			}

			// Advanced Steering & Hıza Duyarlı Dönüş (Stage 6 & Stage 8 + Phase 3 Weight penalty)
			if ( moveTurn != 0 )
			{
				float turnFactor = 1f - (speedRatio * 0.35f);
				float finalTurnSpeed = TurnSpeed * turnFactor * weightTurnPenalty;

				if ( IsDrifting )
				{
					finalTurnSpeed *= DriftTurnMultiplier;
				}

				if ( CurrentSurfaceType == "wet" )
				{
					finalTurnSpeed *= 0.7f;
				}

				float turnAmount = -moveTurn * finalTurnSpeed * Time.Delta * 50f;
				
				// Blend direct yaw rotation at low speed, physical torque at higher speeds
				// Below 15% max speed, direct rotation is fully active for responsive slow maneuvering.
				float directBlend = (1f - speedRatio * 6.6f).Clamp( 0f, 1f );
				float torqueBlend = 1f - directBlend;

				if ( directBlend > 0f )
				{
					WorldRotation = WorldRotation * Rotation.FromAxis( Vector3.Up, turnAmount * directBlend );
				}

				if ( torqueBlend > 0f && _rb != null )
				{
					// Apply physical torque to the Rigidbody angular velocity around local Up axis
					float torqueForce = -moveTurn * finalTurnSpeed * 0.3f * torqueBlend;
					_rb.AngularVelocity = _rb.AngularVelocity.WithZ( _rb.AngularVelocity.z + torqueForce * Time.Delta * 60f );
				}
			}

			// Side-Slip / Grip Interpolation (Stage 8)
			if ( currentSpeed > 10f )
			{
				float currentGrip = IsDrifting ? DriftGrip : NormalGrip;
				currentGrip *= surfaceGripMult;

				if ( !IsDrifting && CurrentSurfaceType != "carpet" )
				{
					currentGrip -= (speedRatio * 2.5f);
				}

				// Floor grip at 0.05 — never let it go zero/negative which would cause NaN in Lerp
				currentGrip = currentGrip.Clamp( 0.05f, 15f );

				float directionSign = (moveFwd >= 0 && !isMovingBackward) ? 1f : -1f;
				var targetVel = WorldRotation.Forward * currentSpeed * directionSign;
				// Guard target velocity NaN: if currentSpeed is 0, Lerp is a no-op anyway
				if ( !float.IsNaN( targetVel.x ) && !float.IsNaN( targetVel.y ) && !float.IsNaN( targetVel.z ) )
				{
					_rb.Velocity = Vector3.Lerp( _rb.Velocity, targetVel, Time.Delta * currentGrip );
				}
			}

			// Speed clamping based on direction & active boosts (Stage 5 & Stage 7 + Weight & Nitro)
			float activeMaxSpeed = ((isMovingBackward ? ReverseSpeed : MaxSpeed) * _currentBoostMultiplier) * surfaceSpeedMult * weightSpeedPenalty;
			
			if ( IsNitroActive )
			{
				activeMaxSpeed *= NitroSpeedMultiplier;
			}

			if ( _rb.Velocity.Length > activeMaxSpeed )
			{
				_rb.Velocity = _rb.Velocity.Normal * activeMaxSpeed;
			}

			// --- Centripetal Rollover Hazard ---
			// Clamp lateralG to prevent wall-collision spikes from launching the cart
			float lateralG = Vector3.Dot( localAcceleration, Vector3.Right ).Clamp( -800f, 800f );
			float tiltThreat = MathF.Abs( lateralG ) * speedRatio * ( 1.0f + weightRatio * 1.8f ) * 0.00035f;
			// Only apply rollover torque when cart is still mostly upright (< 65 deg tilt)
			float currentTiltAngle = Vector3.GetAngle( WorldRotation.Up, Vector3.Up );
			if ( tiltThreat > 0.08f && currentTiltAngle < 65f )
			{
				float tiltDir = MathF.Sign( lateralG );
				_rb.AngularVelocity += WorldRotation.Forward * tiltDir * tiltThreat * 15f * Time.Delta;
			}
		}
		else
		{
			// Havada Sürüş Fiziği (Air Control / Mid-Air Steering & Nitro Gliding)
			IsDrifting = false;
			DriftCharge = 0f;
			DriftBoostLevel = 0;
			UpdateDriftTrails( false );
			ResetWheelTints();

			// Air-Time Nitro Gliding (Stage 10 Physics Juice):
			// Burning Nitro in mid-air applies forward push allowing gliding further
			if ( IsNitroActive && _rb != null )
			{
				_rb.Velocity += WorldRotation.Forward * (Acceleration * 0.5f) * Time.Delta;
			}

			// Air Stomp Trigger (SPACE bar/jump button)
			bool triggerStomp = IsBotDriven ? ( _airTime > 0.8f && Random.Shared.Float() < 0.04f * Time.Delta ) : Input.Pressed( "jump" );
			if ( triggerStomp && !_isStomping && _airTime > 0.25f )
			{
				_isStomping = true;
				_stompTimeout = 3.0f; // Stomp safety timeout
				if ( _rb != null )
				{
					_rb.Velocity = _rb.Velocity.WithZ( -850f );
				}
				if ( !string.IsNullOrEmpty( SoundCrashHard ) )
				{
					Sound.Play( SoundCrashHard, WorldPosition );
				}
			}

			if ( _isStomping && _rb != null )
			{
				_rb.Velocity = _rb.Velocity.WithZ( -850f );
				_stompTimeout -= Time.Delta;
				if ( _stompTimeout <= 0f )
				{
					_isStomping = false;
				}
			}

			// Lock air steering while stomping for a clean vertical drop
			if ( !_isStomping )
			{
				if ( moveFwd != 0 )
				{
					WorldRotation *= Rotation.FromPitch( moveFwd * TurnSpeed * AirControlMultiplier * Time.Delta * 30f );
				}
				if ( moveTurn != 0 )
				{
					WorldRotation *= Rotation.FromYaw( -moveTurn * TurnSpeed * AirControlMultiplier * Time.Delta * 35f );
				}
			}

			CurrentTurnInput = moveTurn;
		}

		// Expose calculated velocity for syncing — guard against destroyed rigidbody
		CartVelocity = _rb != null ? _rb.Velocity : Vector3.Zero;

		// Update visual wheels roll and wobble
		UpdateVisualWheels( currentSpeed, moveTurn, speedRatio, weightRatio );

		// Sync cargo visual model with ItemCount
		UpdateCargoModelVisibility();
	}

	private void UpdateBrokenWheelPull( float speed, float speedRatio, float weightRatio )
	{
		// Decay temporary crash pull
		if ( PullIntensity > 0.01f )
		{
			PullIntensity = PullIntensity.LerpTo( 0f, Time.Delta * 0.35f );
		}

		// Decay wheel damage very slowly while driving
		if ( WheelDamage > 0.01f )
		{
			WheelDamage = WheelDamage.LerpTo( 0f, Time.Delta * 0.015f );
		}

		float activePull = (WheelDamage * 0.35f + PullIntensity).Clamp( 0f, 1.0f );
		if ( activePull <= 0.01f ) return;

		// Pull direction yaw amount (amplified by weight)
		float pullYawAmount = PullDirection * activePull * 4.0f * Time.Delta * (1.0f + weightRatio * 1.2f);
		
		// If wheel is locked up, pull is much more severe!
		if ( _isWheelLockedUp )
		{
			pullYawAmount *= 2.5f;
		}

		WorldRotation *= Rotation.FromYaw( pullYawAmount );

		if ( !IsGrounded || speed < 25f ) return;

		// Screech and sparks
		float scrapeIntensity = activePull;
		if ( _isWheelLockedUp ) scrapeIntensity = 1.0f;

		if ( !string.IsNullOrEmpty( SoundBrokenWheelScrape ) )
		{
			_scrapeTimer -= Time.Delta;
			if ( _scrapeTimer <= 0 )
			{
				float targetVolume = scrapeIntensity * 0.85f;
				float targetPitch = 0.9f + speedRatio * 0.3f;

				var scrapeSound = Sound.Play( SoundBrokenWheelScrape, WorldPosition );
				if ( scrapeSound.IsValid() )
				{
					scrapeSound.Volume = targetVolume;
					scrapeSound.Pitch = targetPitch;
				}
				_scrapeTimer = (0.7f - (scrapeIntensity * 0.4f)).Clamp( 0.15f, 0.8f );
			}
		}

		if ( CollisionSparkPrefab != null )
		{
			_scrapeSparkTimer -= Time.Delta;
			if ( _scrapeSparkTimer <= 0 )
			{
				_scrapeSparkTimer = SparkSpawnRate * (1.5f - scrapeIntensity * 0.5f);

				var targetWheel = PullDirection < 0 ? WheelFL : WheelFR;
				if ( targetWheel == null ) targetWheel = WheelFL;

				if ( targetWheel != null )
				{
					var sparks = CollisionSparkPrefab.Clone( targetWheel.WorldPosition + Vector3.Down * 4f );
					var r = sparks.Components.GetInDescendantsOrSelf<ModelRenderer>();
					if ( r != null ) r.Tint = new Color( 1.0f, 0.4f, 0f );
					DestroyAfterDelay( sparks, 1.0f );
				}
			}
		}
	}

	private void UpdateNitroSystem()
	{
		bool wantNitro = IsBotDriven ? AiWantNitro : Input.Down( "attack2" );
		
		// Decrease backfire timer
		if ( _backfireTimer > 0f )
		{
			_backfireTimer -= Time.Delta;
		}

		if ( wantNitro && (Driver != null || IsBotDriven) )
		{
			if ( IsOverheated )
			{
				// Attempting to use nitro while overheated triggers backfire!
				if ( _backfireTimer <= 0f )
				{
					TriggerNitroBackfire();
				}
				IsNitroActive = false;
			}
			else if ( NitroFuel > 0f )
			{
				IsNitroActive = true;
				NitroFuel -= NitroBurnRate * Time.Delta;
				
				// Sputtering warnings when close to overheating
				float scaleMult = 1.0f;
				if ( NitroFuel < 25f )
				{
					if ( Random.Shared.Float() > 0.85f )
					{
						if ( !string.IsNullOrEmpty( SoundWheelWobbleClick ) )
						{
							Sound.Play( SoundWheelWobbleClick, WorldPosition );
						}
						if ( OverheatSmokePrefab != null )
						{
							var smoke = OverheatSmokePrefab.Clone( WorldPosition - WorldRotation.Forward * 30f + Vector3.Up * 10f );
							DestroyAfterDelay( smoke, 0.6f );
						}
						if ( _rb != null )
						{
							_rb.Velocity += WorldRotation.Backward * 12f;
						}
					}
					scaleMult = 0.5f + MathF.Sin( Time.Now * 45f ) * 0.4f;
				}
				
				foreach ( var flame in NitroVisuals )
				{
					if ( flame != null )
					{
						flame.LocalScale = Vector3.One * scaleMult;
					}
				}

				if ( NitroFuel <= 0f )
				{
					NitroFuel = 0f;
					IsOverheated = true;
					IsNitroActive = false;
					TriggerNitroBackfire(); // Force backfire when hits 0% fuel
				}
				
				_nitroSoundTimer -= Time.Delta;
				if ( _nitroSoundTimer <= 0 )
				{
					_nitroSoundTimer = 0.5f;
					if ( !string.IsNullOrEmpty( SoundNitroLoop ) )
					{
						Sound.Play( SoundNitroLoop, WorldPosition );
					}
				}
			}
			else
			{
				IsNitroActive = false;
			}
		}
		else
		{
			IsNitroActive = false;
			
			if ( NitroFuel < 100f )
			{
				NitroFuel += NitroRechargeRate * Time.Delta;
				if ( NitroFuel > 100f ) NitroFuel = 100f;
				
				if ( IsOverheated && NitroFuel >= 30f )
				{
					IsOverheated = false;
				}
			}

			if ( IsOverheated && OverheatSmokePrefab != null )
			{
				_smokeTimer -= Time.Delta;
				if ( _smokeTimer <= 0 )
				{
					float heatRatio = 1f - (NitroFuel / 100f);
					_smokeTimer = (0.4f - (heatRatio * 0.25f)).Clamp( 0.08f, 0.4f );

					var smokePos = WorldPosition - WorldRotation.Forward * 30f + Vector3.Up * 10f;
					var smoke = OverheatSmokePrefab.Clone( smokePos );
					
					var rb = smoke.Components.Get<Rigidbody>();
					if ( rb != null )
					{
						rb.Velocity = Vector3.Up * 30f + Vector3.Random * 10f;
					}
					
					DestroyAfterDelay( smoke, 1.4f );
				}
			}
		}
	}

	private void TriggerNitroBackfire()
	{
		_backfireTimer = 0.8f;
		LastImpactSpeed = 180f; // Triggers violent screen shake and camera zoom kick (Phase 5 Polish)

		if ( !string.IsNullOrEmpty( SoundCrashHard ) )
		{
			Sound.Play( SoundCrashHard, WorldPosition );
		}

		if ( CollisionSparkPrefab != null )
		{
			var backPos = WorldPosition - WorldRotation.Forward * 35f + Vector3.Up * 8f;
			var sparks = CollisionSparkPrefab.Clone( backPos, Rotation.LookAt( WorldRotation.Backward ) );
			var r = sparks.Components.GetInDescendantsOrSelf<ModelRenderer>();
			if ( r != null ) r.Tint = Color.Red;
			
			// Backfire light flash (Phase 5 Polish)
			var flashGo = new GameObject();
			flashGo.WorldPosition = backPos;
			var flashLight = flashGo.Components.Create<PointLight>();
			if ( flashLight != null )
			{
				flashLight.LightColor = new Color( 1.0f, 0.3f, 0.0f ) * 8.0f;
				flashLight.Radius = 300f;
			}
			DestroyAfterDelay( flashGo, 0.22f );

			DestroyAfterDelay( sparks, 1.0f );
		}

		if ( OverheatSmokePrefab != null )
		{
			var backPos = WorldPosition - WorldRotation.Forward * 35f + Vector3.Up * 8f;
			var smoke = OverheatSmokePrefab.Clone( backPos );
			var rb = smoke.Components.Get<Rigidbody>();
			if ( rb != null )
			{
				rb.Velocity = WorldRotation.Backward * 40f + Vector3.Up * 20f + Vector3.Random * 15f;
			}
			DestroyAfterDelay( smoke, 1.5f );
		}

		if ( _rb != null )
		{
			var kickDir = (WorldRotation.Backward * 35f) + (WorldRotation.Right * Random.Shared.Float( -30f, 30f )) + (Vector3.Up * 15f);
			_rb.Velocity += kickDir;
		}

		LastImpactSpeed = 180f; // Triggers camera shake!
	}

	private void UpdateSuspensionSpring( float speed, float ratio, float weightRatio, Vector3 localAcceleration )
	{
		// Suspension Sag: Target height sags down (sinks) under heavy item loads
		float targetHeight = -weightRatio * MaxSuspensionSag;

		float verticalAccel = localAcceleration.z;
		float displacement = _suspensionHeight - targetHeight;
		float springForce = -SpringConstant * displacement;
		float dampingForce = -DampingConstant * _suspensionVelocity;
		float springAcceleration = springForce + dampingForce - (verticalAccel * 0.1f);

		// Add road noise vibrations at speed
		if ( IsGrounded && speed > 50f )
		{
			float noise = Random.Shared.Float( -15f, 15f ) * ratio;
			if ( CurrentSurfaceType == "carpet" ) noise *= 0.4f;
			
			if ( weightRatio > 0.1f )
			{
				noise *= (1f + weightRatio * 0.5f);
			}

			springAcceleration += noise;
		}

		_suspensionVelocity += springAcceleration * Time.Delta;
		_suspensionHeight += _suspensionVelocity * Time.Delta;

		// Longitudinal acceleration causes pitch lean
		float pitchSens = 0.03f;
		float targetPitchAngle = (-localAcceleration.x * pitchSens).Clamp( -12f, 12f );
		
		float pitchDisplacement = _suspensionPitch - targetPitchAngle;
		float pitchSpringConstant = SpringConstant * 0.8f;
		float pitchDampingConstant = DampingConstant * 0.8f;
		
		float pitchSpringForce = -pitchSpringConstant * pitchDisplacement;
		float pitchDampingForce = -pitchDampingConstant * _suspensionPitchVelocity;
		
		_suspensionPitchVelocity += (pitchSpringForce + pitchDampingForce) * Time.Delta;
		_suspensionPitch += _suspensionPitchVelocity * Time.Delta;

		// Lateral acceleration causes roll lean (tilting outwards in turns)
		float rollSens = 0.04f;
		float targetRollAngle = (localAcceleration.y * rollSens).Clamp( -15f, 15f );
		targetRollAngle += (CurrentTurnInput * ratio * MaxBasketRoll * (1.0f + weightRatio * 1.5f));
		
		float rollDisplacement = _suspensionRoll - targetRollAngle;
		float rollSpringConstant = SpringConstant * 0.7f;
		float rollDampingConstant = DampingConstant * 0.7f;
		
		float rollSpringForce = -rollSpringConstant * rollDisplacement;
		float rollDampingForce = -rollDampingConstant * _suspensionRollVelocity;
		
		_suspensionRollVelocity += (rollSpringForce + rollDampingForce) * Time.Delta;
		_suspensionRoll += _suspensionRollVelocity * Time.Delta;

		if ( BasketMesh != null )
		{
			BasketMesh.LocalPosition = new Vector3( 0, 0, _suspensionHeight );
			BasketMesh.LocalRotation = Rotation.From( _suspensionPitch, 0f, _suspensionRoll );
		}
	}

	private void UpdateItemJiggle( float speed, float speedRatio, float weightRatio )
	{
		if ( ItemContainer == null || ItemCount == 0 ) return;

		float jiggleFreq = 28f;
		float jiggleAmplitudeX = (speedRatio * 1.5f + Math.Abs( _suspensionVelocity ) * 0.05f) * ItemJiggleIntensity;
		float jiggleAmplitudeZ = (speedRatio * 0.8f + Math.Abs( _suspensionHeight ) * 0.3f) * ItemJiggleIntensity;

		jiggleFreq *= (1f - weightRatio * 0.35f);

		float offsetX = MathF.Sin( Time.Now * jiggleFreq ) * jiggleAmplitudeX;
		float offsetZ = MathF.Cos( Time.Now * (jiggleFreq * 0.9f) ) * jiggleAmplitudeZ;

		float targetRotationY = 0f;
		if ( IsDrifting )
		{
			targetRotationY = CurrentTurnInput * 12f;
		}

		ItemContainer.LocalPosition = new Vector3( offsetX * 0.3f, 0f, offsetZ * 0.4f );
		ItemContainer.LocalRotation = Rotation.FromYaw( targetRotationY * 0.8f ) * Rotation.FromRoll( offsetX * 0.5f );

		// Apply reactive rolling/pitching torque or lateral force back to the cart (Stage 9 Physics Juice)
		if ( _rb != null && IsGrounded )
		{
			float cargoForce = MathF.Cos( Time.Now * jiggleFreq ) * jiggleAmplitudeX * weightRatio * 120f;
			_rb.Velocity += WorldRotation.Right * cargoForce * Time.Delta;
			
			float cargoVerticalForce = MathF.Sin( Time.Now * (jiggleFreq * 0.9f) ) * jiggleAmplitudeZ * weightRatio * 80f;
			_rb.Velocity += Vector3.Up * cargoVerticalForce * Time.Delta;
		}
	}

	private void ResetItemJiggle()
	{
		if ( ItemContainer != null )
		{
			ItemContainer.LocalPosition = Vector3.Zero;
			ItemContainer.LocalRotation = Rotation.Identity;
		}
	}

	private void UpdateDynamicRattle( float speed, float ratio, float weightRatio )
	{
		if ( string.IsNullOrEmpty( SoundRattle ) ) return;

		_rattleTimer -= Time.Delta;
		if ( _rattleTimer <= 0 && speed > 15f )
		{
			float suspensionDisplacement = MathF.Abs( _suspensionVelocity ) + MathF.Abs( _suspensionHeight * 2.0f );
			
			float targetPitch = 1.0f - (weightRatio * 0.28f) - (suspensionDisplacement * 0.05f);
			targetPitch = targetPitch.Clamp( 0.6f, 1.3f );

			float targetVolume = (ratio * 0.5f + suspensionDisplacement * 0.08f) * (1.0f - (weightRatio * 0.2f));
			targetVolume = targetVolume.Clamp( 0f, 1.2f );

			var rattleSound = Sound.Play( SoundRattle, WorldPosition );
			if ( rattleSound.IsValid() )
			{
				rattleSound.Pitch = targetPitch;
				rattleSound.Volume = targetVolume;
			}
			
			float nextRattleDelay = 0.55f - (ratio * 0.45f) - (suspensionDisplacement * 0.02f);
			if ( IsDrifting ) nextRattleDelay *= 0.6f;
			if ( CurrentSurfaceType == "carpet" ) nextRattleDelay *= 1.3f;
			
			_rattleTimer = nextRattleDelay.Clamp( 0.06f, 0.7f );
		}
	}

	private void UpdateWobblyWheelClickSound( float speed, float ratio )
	{
		if ( string.IsNullOrEmpty( SoundWheelWobbleClick ) || WheelFL == null ) return;

		_wobbleClickTimer -= Time.Delta;
		if ( _wobbleClickTimer <= 0 && speed > 20f && IsGrounded )
		{
			float targetVolume = ratio * 0.7f;
			float targetPitch = 0.85f + ratio * 0.35f;

			var clickSound = Sound.Play( SoundWheelWobbleClick, WheelFL.WorldPosition );
			if ( clickSound.IsValid() )
			{
				clickSound.Volume = targetVolume;
				clickSound.Pitch = targetPitch;
			}
			
			float nextClickDelay = 0.25f - (ratio * 0.20f);
			if ( CurrentSurfaceType == "carpet" ) nextClickDelay *= 1.5f;
			
			nextClickDelay += Random.Shared.Float( -0.02f, 0.02f );
			_wobbleClickTimer = nextClickDelay.Clamp( 0.04f, 0.4f );
		}
	}

	private void UpdateWindLoop( float speed, float ratio )
	{
		if ( string.IsNullOrEmpty( SoundWindLoop ) )
		{
			if ( _windSoundHandle.IsValid() )
			{
				_windSoundHandle.Stop();
			}
			return;
		}

		if ( ratio > 0.1f )
		{
			if ( !_windSoundHandle.IsValid() )
			{
				_windSoundHandle = Sound.Play( SoundWindLoop, WorldPosition );
			}

			if ( _windSoundHandle.IsValid() )
			{
				_windSoundHandle.Position = WorldPosition;
				_windSoundHandle.Volume = ( (ratio - 0.1f) * 0.8f ).Clamp( 0f, 1f );
				_windSoundHandle.Pitch = 0.8f + (ratio - 0.1f) * 0.4f;
			}
		}
		else
		{
			if ( _windSoundHandle.IsValid() )
			{
				_windSoundHandle.Volume = _windSoundHandle.Volume.LerpTo( 0f, Time.Delta * 5f );
				if ( _windSoundHandle.Volume < 0.01f )
				{
					_windSoundHandle.Stop();
				}
			}
		}
	}

	private void SpawnExhaustSparksPeriodically()
	{
		if ( CollisionSparkPrefab == null ) return;

		if ( Random.Shared.Float() > 0.35f ) return;

		Color tintColor = IsNitroActive ? new Color( 1f, 0.4f, 0.05f ) : (DriftBoostLevel switch
		{
			1 => new Color( 0.1f, 0.6f, 1.0f ),
			2 => new Color( 1.0f, 0.5f, 0f ),
			3 => new Color( 0.8f, 0f, 1.0f ),
			_ => new Color( 0.4f, 0.8f, 1.0f )
		});

		if ( WheelRL != null )
		{
			var pos = WheelRL.WorldPosition - WorldRotation.Forward * 8f + Vector3.Down * 2f;
			var sparks = CollisionSparkPrefab.Clone( pos );
			var r = sparks.Components.GetInDescendantsOrSelf<ModelRenderer>();
			if ( r != null ) r.Tint = tintColor;
			
			// PointLight glow on exhaust sparks (Phase 5 Polish)
			var light = sparks.Components.Create<PointLight>();
			if ( light != null )
			{
				light.LightColor = tintColor * 3.5f;
				light.Radius = 120f;
				var flicker = sparks.Components.Create<FlickerLight>();
			}

			DestroyAfterDelay( sparks, 0.6f );
		}

		if ( WheelRR != null )
		{
			var pos = WheelRR.WorldPosition - WorldRotation.Forward * 8f + Vector3.Down * 2f;
			var sparks = CollisionSparkPrefab.Clone( pos );
			var r = sparks.Components.GetInDescendantsOrSelf<ModelRenderer>();
			if ( r != null ) r.Tint = tintColor;

			// PointLight glow on exhaust sparks (Phase 5 Polish)
			var light = sparks.Components.Create<PointLight>();
			if ( light != null )
			{
				light.LightColor = tintColor * 3.5f;
				light.Radius = 120f;
				var flicker = sparks.Components.Create<FlickerLight>();
			}

			DestroyAfterDelay( sparks, 0.6f );
		}
	}

	private void UpdateEngineGlowLight()
	{
		bool showGlow = IsNitroActive || IsBoosting;
		if ( showGlow && (Driver != null || IsBotDriven) )
		{
			if ( _nitroGlowLight == null )
			{
				var glowObj = new GameObject();
				glowObj.Parent = GameObject;
				glowObj.LocalPosition = new Vector3( -35f, 0f, 12f );
				
				_nitroGlowLight = glowObj.Components.Create<PointLight>();
				if ( _nitroGlowLight != null )
				{
					_nitroGlowLight.Radius = 240f;
					var flicker = glowObj.Components.Create<FlickerLight>();
				}
			}

			if ( _nitroGlowLight != null )
			{
				_nitroGlowLight.Enabled = true;
				if ( IsNitroActive )
				{
					_nitroGlowLight.LightColor = new Color( 1.0f, 0.35f, 0.05f ) * 1.5f;
				}
				else // IsBoosting
				{
					var boostColor = DriftBoostLevel switch
					{
						1 => new Color( 0.1f, 0.6f, 1.0f ),
						2 => new Color( 1.0f, 0.5f, 0f ),
						3 => new Color( 0.8f, 0f, 1.0f ),
						_ => new Color( 0.4f, 0.8f, 1.0f )
					};
					_nitroGlowLight.LightColor = boostColor * 1.5f;
				}
			}
		}
		else
		{
			if ( _nitroGlowLight != null )
			{
				_nitroGlowLight.Enabled = false;
			}
		}
	}

	protected override void OnDisabled()
	{
		if ( _windSoundHandle.IsValid() )
		{
			_windSoundHandle.Stop();
		}
	}

	protected override void OnDestroy()
	{
		if ( _windSoundHandle.IsValid() )
		{
			_windSoundHandle.Stop();
		}
		if ( _nitroGlowLight != null && _nitroGlowLight.GameObject.IsValid() )
		{
			_nitroGlowLight.GameObject.Destroy();
		}
	}

	private void ResetVisualBasketLean()
	{
		_suspensionHeight = _suspensionHeight.LerpTo( 0f, Time.Delta * 8f );
		_suspensionVelocity = _suspensionVelocity.LerpTo( 0f, Time.Delta * 8f );
		_suspensionPitch = _suspensionPitch.LerpTo( 0f, Time.Delta * 8f );
		_suspensionPitchVelocity = _suspensionPitchVelocity.LerpTo( 0f, Time.Delta * 8f );
		_suspensionRoll = _suspensionRoll.LerpTo( 0f, Time.Delta * 8f );
		_suspensionRollVelocity = _suspensionRollVelocity.LerpTo( 0f, Time.Delta * 8f );

		if ( BasketMesh != null )
		{
			BasketMesh.LocalPosition = new Vector3( 0f, 0f, _suspensionHeight );
			BasketMesh.LocalRotation = Rotation.From( _suspensionPitch, 0f, _suspensionRoll );
		}
	}

	private void UpdateDriftChargeColors()
	{
		if ( CurrentSurfaceType == "wet" ) return;

		var sparkColor = DriftBoostLevel switch
		{
			1 => new Color( 0.1f, 0.6f, 1.0f, 1f ),
			2 => new Color( 1.0f, 0.5f, 0.0f, 1f ),
			3 => new Color( 0.8f, 0.0f, 1.0f, 1f ),
			_ => new Color( 0.4f, 0.8f, 1.0f, 0.7f )
		};

		if ( WheelRL != null )
		{
			var r = WheelRL.Components.GetInDescendantsOrSelf<ModelRenderer>();
			if ( r != null ) r.Tint = sparkColor;
		}
		if ( WheelRR != null )
		{
			var r = WheelRR.Components.GetInDescendantsOrSelf<ModelRenderer>();
			if ( r != null ) r.Tint = sparkColor;
		}
	}

	private void ResetWheelTints()
	{
		if ( WheelRL != null )
		{
			var r = WheelRL.Components.GetInDescendantsOrSelf<ModelRenderer>();
			if ( r != null ) r.Tint = Color.White;
		}
		if ( WheelRR != null )
		{
			var r = WheelRR.Components.GetInDescendantsOrSelf<ModelRenderer>();
			if ( r != null ) r.Tint = Color.White;
		}
	}

	private void SpawnDriftEffectsPeriodically()
	{
		_sparkTimer -= Time.Delta;
		if ( _sparkTimer <= 0 )
		{
			_sparkTimer = SparkSpawnRate;

			if ( CurrentSurfaceType == "wet" )
			{
				// Spawn blue/white water splashes instead of sparks on wet floors
				if ( CollisionSparkPrefab != null )
				{
					if ( WheelRL != null )
					{
						var splash = CollisionSparkPrefab.Clone( WheelRL.WorldPosition + Vector3.Down * 4f );
						var r = splash.Components.GetInDescendantsOrSelf<ModelRenderer>();
						if ( r != null ) r.Tint = new Color( 0.6f, 0.85f, 1.0f, 0.7f );
						DestroyAfterDelay( splash, 0.8f );
					}
					if ( WheelRR != null )
					{
						var splash = CollisionSparkPrefab.Clone( WheelRR.WorldPosition + Vector3.Down * 4f );
						var r = splash.Components.GetInDescendantsOrSelf<ModelRenderer>();
						if ( r != null ) r.Tint = new Color( 0.6f, 0.85f, 1.0f, 0.7f );
						DestroyAfterDelay( splash, 0.8f );
					}
				}
			}
			else
			{
				// Spawn drift sparks based on boost level
				if ( DriftSparkPrefab != null )
				{
					if ( WheelRL != null )
					{
						var sparks = DriftSparkPrefab.Clone( WheelRL.WorldPosition );
						var r = sparks.Components.GetInDescendantsOrSelf<ModelRenderer>();
						var tintColor = DriftBoostLevel switch
						{
							1 => new Color( 0.1f, 0.6f, 1.0f ),
							2 => new Color( 1.0f, 0.5f, 0f ),
							3 => new Color( 0.8f, 0f, 1.0f ),
							_ => new Color( 0.4f, 0.8f, 1.0f )
						};
						if ( r != null )
						{
							r.Tint = tintColor;
						}

						// Add dynamic point light to sparks (Phase 5 Polish)
						var light = sparks.Components.Create<PointLight>();
						if ( light != null )
						{
							light.LightColor = tintColor * 5.0f;
							light.Radius = 150f;

							var flicker = sparks.Components.Create<FlickerLight>();
							if ( flicker != null )
							{
								// FlickerLight will automatically cache LightColor and modulate it
							}
						}

						DestroyAfterDelay( sparks, 1.2f );
					}
					if ( WheelRR != null )
					{
						var sparks = DriftSparkPrefab.Clone( WheelRR.WorldPosition );
						var r = sparks.Components.GetInDescendantsOrSelf<ModelRenderer>();
						var tintColor = DriftBoostLevel switch
						{
							1 => new Color( 0.1f, 0.6f, 1.0f ),
							2 => new Color( 1.0f, 0.5f, 0f ),
							3 => new Color( 0.8f, 0f, 1.0f ),
							_ => new Color( 0.4f, 0.8f, 1.0f )
						};
						if ( r != null )
						{
							r.Tint = tintColor;
						}

						// Add dynamic point light to sparks (Phase 5 Polish)
						var light = sparks.Components.Create<PointLight>();
						if ( light != null )
						{
							light.LightColor = tintColor * 5.0f;
							light.Radius = 150f;

							var flicker = sparks.Components.Create<FlickerLight>();
							if ( flicker != null )
							{
								// FlickerLight will automatically cache LightColor and modulate it
							}
						}

						DestroyAfterDelay( sparks, 1.2f );
					}
				}

				// Spawn drift tire smoke puffs (reusing OverheatSmokePrefab)
				if ( OverheatSmokePrefab != null )
				{
					if ( WheelRL != null )
					{
						var smoke = OverheatSmokePrefab.Clone( WheelRL.WorldPosition + Vector3.Down * 4f );
						var r = smoke.Components.GetInDescendantsOrSelf<ModelRenderer>();
						if ( r != null ) r.Tint = new Color( 0.9f, 0.9f, 0.9f, 0.3f );
						DestroyAfterDelay( smoke, 0.6f );
					}
					if ( WheelRR != null )
					{
						var smoke = OverheatSmokePrefab.Clone( WheelRR.WorldPosition + Vector3.Down * 4f );
						var r = smoke.Components.GetInDescendantsOrSelf<ModelRenderer>();
						if ( r != null ) r.Tint = new Color( 0.9f, 0.9f, 0.9f, 0.3f );
						DestroyAfterDelay( smoke, 0.6f );
					}
				}
			}
		}
	}

	private void CheckGroundedStatus()
	{
		var startPos = WorldPosition;
		var tr = Scene.Trace.Ray( startPos, startPos + Vector3.Down * GroundCheckDistance )
			.WithoutTags( "player", "cart" )
			.Run();

		IsGrounded = tr.Hit;

		// Also do a quick 4-corner vote so the cart is still considered grounded
		// even when only the rear or front wheels are touching the ground
		if ( !IsGrounded )
		{
			int groundedWheels = 0;
			for ( int i = 0; i < 4; i++ )
			{
				if ( _wheelGrounded[i] ) groundedWheels++;
			}
			IsGrounded = groundedWheels >= 2; // at least 2 wheels touching
		}

		if ( IsGrounded && tr.GameObject != null )
		{
			var tags = tr.GameObject.Tags;
			if ( tags.Has( "carpet" ) ) CurrentSurfaceType = "carpet";
			else if ( tags.Has( "wet" ) || tags.Has( "oil" ) || tags.Has( "water" ) ) CurrentSurfaceType = "wet";
			else CurrentSurfaceType = "tile";
		}
		else
		{
			CurrentSurfaceType = "air";
		}

		if ( IsGrounded )
		{
			if ( !_wasGroundedLastFrame )
			{
				HandleLanding( _airTime );
			}
			_airTime = 0f;
		}
		else
		{
			_airTime += Time.Delta;
		}

		_wasGroundedLastFrame = IsGrounded;
	}

	private void ApplyWheelRaycastSuspension()
	{
		// Gather the 4 wheel GameObjects in order FL, FR, RL, RR
		var wheels = new GameObject[] { WheelFL, WheelFR, WheelRL, WheelRR };

		for ( int i = 0; i < 4; i++ )
		{
			var wheel = wheels[i];
			if ( wheel == null ) continue;

			// Ray origin is the wheel's world position, pointing straight down
			var origin = wheel.WorldPosition;
			var dir    = Vector3.Down;

			var tr = Scene.Trace
				.Ray( origin, origin + dir * WheelSuspensionRestLength )
				.WithoutTags( "player", "cart" )
				.Run();

			_wheelGrounded[i] = tr.Hit;

			if ( !tr.Hit )
			{
				_wheelCompression[i] = 0f;
				continue;
			}

			// Compression: 0 = fully extended, 1 = fully compressed
			float compression = 1f - (tr.Distance / WheelSuspensionRestLength);
			compression = compression.Clamp( 0f, 1f );

			// Compression velocity (rate of change per second)
			float compressVelocity = Time.Delta > 0.001f
				? (compression - _wheelPrevCompress[i]) / Time.Delta
				: 0f;

			_wheelPrevCompress[i] = _wheelCompression[i];
			_wheelCompression[i]  = compression;

			// Spring force (Hooke's law) + damping force, applied upward at the wheel position
			float springForce  = WheelSuspensionStiffness * compression;
			float dampingForce = WheelSuspensionDamping   * compressVelocity;
			float totalForce   = (springForce + dampingForce).Clamp( 0f, WheelSuspensionStiffness * 2f );

			// Apply as a world-space impulse (Force = mass * accel, but since s&box uses velocity
			// integration we treat this as a per-fixed-step velocity delta)
			var forceDir = tr.Normal; // push along the surface normal (usually ~Vector3.Up)
			_rb.ApplyForceAt( wheel.WorldPosition, forceDir * totalForce );
		}
	}

	private void HandleLanding( float airTime )
	{
		if ( ( airTime > 0.3f || _isStomping ) && _rb != null )
		{
			bool wasStomping = _isStomping;
			_isStomping = false;

			float verticalSpeed = Math.Abs( _rb.Velocity.z );
			
			if ( wasStomping )
			{
				ExecuteStompExplosion();
			}
			
			var horizVel = _rb.Velocity.WithZ( 0 );
			if ( horizVel.Length > 50f )
			{
				float alignment = Vector3.Dot( WorldRotation.Forward.WithZ( 0 ).Normal, horizVel.Normal );
				if ( alignment < 0.85f )
				{
					// Misaligned landings maintain 82% of horizontal speed, smoothly aligning forward
					_rb.Velocity = Vector3.Lerp( _rb.Velocity, WorldRotation.Forward * horizVel.Length * 0.82f, 0.65f ) + Vector3.Up * _rb.Velocity.z;
					
					if ( !string.IsNullOrEmpty( SoundDriftScreech ) )
					{
						Sound.Play( SoundDriftScreech, WorldPosition );
					}
				}
				else
				{
					// Aligned landings keep 95% of horizontal speed for massive speed retention!
					_rb.Velocity = WorldRotation.Forward * horizVel.Length * 0.95f + Vector3.Up * _rb.Velocity.z;
				}
			}

			if ( !string.IsNullOrEmpty( SoundLanding ) )
			{
				Sound.Play( SoundLanding, WorldPosition );
			}

			if ( CollisionSparkPrefab != null )
			{
				var sparks = CollisionSparkPrefab.Clone( WorldPosition + Vector3.Down * 15f );
				DestroyAfterDelay( sparks, 1.5f );
			}

			_suspensionVelocity = -verticalSpeed * 0.18f;

			LastImpactSpeed = verticalSpeed;
		}
	}

	void Component.ICollisionListener.OnCollisionUpdate( Collision collision )
	{
		if ( IsProxy || _rb == null ) return;

		float currentSpeed = _rb.Velocity.Length;
		if ( currentSpeed > 100f )
		{
			// Check if we hit a wall (horizontal normal) rather than floor (vertical normal)
			float verticality = MathF.Abs( collision.Contact.Normal.z );
			if ( verticality < 0.4f )
			{
				_wallScrapeTimer -= Time.Delta;
				if ( _wallScrapeTimer <= 0f )
				{
					_wallScrapeTimer = 0.12f; // Reset — slightly longer to prevent burst on first contact
					
					if ( CollisionSparkPrefab != null )
					{
						var sparks = CollisionSparkPrefab.Clone( collision.Contact.Point, Rotation.LookAt( collision.Contact.Normal ) );
						DestroyAfterDelay( sparks, 0.8f );
					}

					if ( !string.IsNullOrEmpty( SoundBrokenWheelScrape ) )
					{
						var snd = Sound.Play( SoundBrokenWheelScrape, collision.Contact.Point );
						if ( snd.IsValid() )
						{
							snd.Volume = 0.22f * (currentSpeed / MaxSpeed);
							snd.Pitch = 1.25f;
						}
					}
				}

				// Wall friction deceleration penalty
				_rb.Velocity *= 0.995f;
			}
		}
	}

	void Component.ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( IsProxy ) return;

		float relativeSpeed = collision.Contact.Speed.Length;
		if ( relativeSpeed > 100f )
		{
			// Check if we hit a pedestrian
			var ped = collision.Other.GameObject.Components.GetInAncestorsOrSelf<Pedestrian>();
			if ( ped != null )
			{
				ped.HitByCart( _rb.Velocity, _rb.Velocity.Length );
				if ( _rb != null )
				{
					_rb.Velocity *= 0.85f; // Slight penalty when hitting a pedestrian
				}
				return;
			}
		}

		if ( relativeSpeed > 180f )
		{
			LastImpactSpeed = relativeSpeed;

			if ( !string.IsNullOrEmpty( SoundCrashHard ) )
			{
				Sound.Play( SoundCrashHard, collision.Contact.Point );
			}

			if ( CollisionSparkPrefab != null )
			{
				var sparks = CollisionSparkPrefab.Clone( collision.Contact.Point, Rotation.LookAt( collision.Contact.Normal ) );
				DestroyAfterDelay( sparks, 1.5f );
			}

			// Play extra cargo clattering/rattling sounds on crashes (Phase 5 Polish)
			if ( ItemCount > 0 && !string.IsNullOrEmpty( SoundRattle ) )
			{
				float cargoRatio = (float)ItemCount / MaxItems;
				var clatter = Sound.Play( SoundRattle, collision.Contact.Point );
				if ( clatter.IsValid() )
				{
					clatter.Pitch = 1.3f + Random.Shared.Float( -0.15f, 0.15f );
					clatter.Volume = (relativeSpeed / 300f).Clamp( 0.2f, 1.5f ) * cargoRatio * 1.6f;
				}
			}

			// Radial Shockwave on extremely high speed crashes flings nearby items, bots, and carts
			if ( relativeSpeed > 260f )
			{
				var shockPos = collision.Contact.Point;
				
				var carts = Scene.GetAllComponents<ShoppingCartController>();
				foreach ( var other in carts )
				{
					if ( other == this ) continue;
					float dist = (other.WorldPosition - shockPos).Length;
					if ( dist < 180f )
					{
						var pushDir = (other.WorldPosition - shockPos).Normal.WithZ( 0.2f ).Normal;
						other.Push( pushDir * relativeSpeed * 0.8f );
					}
				}
				
				var pedestrians = Scene.GetAllComponents<Pedestrian>();
				foreach ( var ped in pedestrians )
				{
					float dist = (ped.WorldPosition - shockPos).Length;
					if ( dist < 180f )
					{
						ped.HitByCart( (ped.WorldPosition - shockPos).Normal * relativeSpeed, relativeSpeed );
					}
				}

				var items = Scene.GetAllComponents<DroppedItem>();
				foreach ( var item in items )
				{
					float dist = (item.WorldPosition - shockPos).Length;
					if ( dist < 180f )
					{
						var itemRb = item.GameObject.Components.Get<Rigidbody>();
						if ( itemRb != null )
						{
							var pushDir = (item.WorldPosition - shockPos).Normal + Vector3.Up * 0.8f;
							itemRb.Velocity = pushDir.Normal * relativeSpeed * 0.8f;
						}
					}
				}
			}

			// Shock the suspension pitch/roll based on collision normal
			var localNormal = WorldRotation.Inverse * collision.Contact.Normal;
			_suspensionPitchVelocity -= localNormal.x * relativeSpeed * 0.4f;
			_suspensionRollVelocity += localNormal.y * relativeSpeed * 0.4f;

			// Check if we hit another cart (Momentum Transfer)
			var otherCart = collision.Other.GameObject.Components.GetInAncestorsOrSelf<ShoppingCartController>();
			if ( otherCart != null )
			{
				var forceNormal = collision.Contact.Normal;
				otherCart.Push( forceNormal * relativeSpeed * 1.2f );
				this.Push( -forceNormal * relativeSpeed * 1.2f );
				
				// Set other cart's impact speed to trigger screen shake for the other driver
				otherCart.LastImpactSpeed = relativeSpeed;
			}

			// Item Spilling
			if ( ItemCount > 0 )
			{
				int spillCount = (int)((relativeSpeed - 150f) / 35f);
				SpillCargo( spillCount, collision.Contact.Normal );
			}

			// Driver Ejection (on very hard wall collision)
			if ( relativeSpeed > 270f && Driver != null )
			{
				var otherRb = collision.Other.GameObject.Components.GetInAncestorsOrSelf<Rigidbody>();
				if ( otherRb == null ) // Hit a static world wall
				{
					var ejectVelocity = WorldRotation.Forward * relativeSpeed * 1.3f + Vector3.Up * 180f;
					Driver.EjectFromCart( ejectVelocity );
				}
			}

			if ( relativeSpeed > 220f )
			{
				PullDirection = Random.Shared.Float() > 0.5f ? 1f : -1f;
				PullIntensity = (relativeSpeed / MaxSpeed) * 0.8f;
				PullIntensity = PullIntensity.Clamp( 0.2f, 1.0f );

				float damageReduction = Driver != null ? Driver.GetWheelDamageReduction() : 1.0f;
				WheelDamage = (WheelDamage + (relativeSpeed / MaxSpeed) * 0.4f * damageReduction).Clamp( 0f, 1.0f );
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

	private void UpdateVisualWheels( float currentSpeed, float moveTurn, float speedRatio, float weightRatio )
	{
		float rollDelta = WheelRadius > 0.01f
			? (currentSpeed / WheelRadius) * Time.Delta * (180f / MathF.PI)
			: 0f;  // Guard WheelRadius zero — avoids inf/NaN roll accumulation
		
		float forwardDot = Vector3.Dot( _rb.Velocity.Normal, WorldRotation.Forward );
		if ( forwardDot < -0.1f ) rollDelta = -rollDelta;
		
		// FL wheel doesn't roll if locked up
		float flRollDelta = _isWheelLockedUp ? 0f : rollDelta;

		_wheelRollAngle += rollDelta;
		_wheelRollAngleFL += flRollDelta;

		float steerAngle = -moveTurn * MaxVisualSteerAngle;
		if ( IsDrifting && currentSpeed > 50f && _rb != null )
		{
			var localVelDir = WorldRotation.Inverse * _rb.Velocity.Normal;
			float slipAngle = MathF.Atan2( localVelDir.y, localVelDir.x ) * ( 180f / MathF.PI );
			float counterSteer = -slipAngle.Clamp( -MaxVisualSteerAngle * 1.3f, MaxVisualSteerAngle * 1.3f );
			steerAngle = counterSteer;
		}

		float wobbleAngle = 0f;
		if ( IsGrounded && currentSpeed > 15f )
		{
			// Wobble frequency slows down but angle increases under heavy loads
			float finalWobbleFreq = WobbleFrequency * (1f - weightRatio * 0.35f);
			float finalWobbleAngle = MaxWobbleAngle * (1f + weightRatio * 0.3f);

			wobbleAngle = MathF.Sin( Time.Now * finalWobbleFreq ) * speedRatio * finalWobbleAngle;
			
			float activePull = (WheelDamage * 0.35f + PullIntensity).Clamp( 0f, 1f );
			if ( activePull > 0.05f && WheelFL != null )
			{
				wobbleAngle += PullDirection * activePull * finalWobbleAngle * 0.7f;
			}
		}

		if ( WheelFL != null )
		{
			WheelFL.LocalRotation = Rotation.FromYaw( steerAngle + wobbleAngle ) * Rotation.FromPitch( _wheelRollAngleFL );
		}

		if ( WheelFR != null )
		{
			WheelFR.LocalRotation = Rotation.FromYaw( steerAngle ) * Rotation.FromPitch( _wheelRollAngle );
		}

		if ( WheelRL != null )
		{
			WheelRL.LocalRotation = Rotation.FromPitch( _wheelRollAngle );
		}

		if ( WheelRR != null )
		{
			WheelRR.LocalRotation = Rotation.FromPitch( _wheelRollAngle );
		}
	}

	private void UpdateDriftTrails( bool active )
	{
		if ( DriftTrails == null || DriftTrails.Count == 0 ) return;

		foreach ( var trail in DriftTrails )
		{
			if ( trail != null )
			{
				trail.Enabled = active;
			}
		}
	}

	private void UpdateBoostVisuals( bool active )
	{
		if ( BoostVisuals == null || BoostVisuals.Count == 0 ) return;

		foreach ( var visual in BoostVisuals )
		{
			if ( visual != null )
			{
				visual.Enabled = active;
			}
		}
	}

	private void UpdateNitroVisuals( bool active )
	{
		if ( NitroVisuals == null || NitroVisuals.Count == 0 ) return;

		foreach ( var visual in NitroVisuals )
		{
			if ( visual != null )
			{
				visual.Enabled = active;
			}
		}
	}

	public void TriggerDriftBoost( int level )
	{
		switch ( level )
		{
			case 1:
				_currentBoostMultiplier = BoostMultiplierLevel1;
				_driftBoostTimer = BoostDurationLevel1;
				break;
			case 2:
				_currentBoostMultiplier = BoostMultiplierLevel2;
				_driftBoostTimer = BoostDurationLevel2;
				break;
			case 3:
				_currentBoostMultiplier = BoostMultiplierLevel3;
				_driftBoostTimer = BoostDurationLevel3;
				break;
		}

		// Drift-to-Nitro reward conversion
		float nitroAward = level switch
		{
			1 => 15f,
			2 => 30f,
			3 => 50f,
			_ => 0f
		};
		if ( Driver != null )
		{
			nitroAward *= ( 1.0f + Driver.NitroLevel * 0.15f );
		}
		NitroFuel = ( NitroFuel + nitroAward ).Clamp( 0f, 100f + ( Driver != null ? Driver.NitroLevel * 20f : 0f ) );

		if ( _rb != null )
		{
			_rb.Velocity += WorldRotation.Forward * (120f * level);
		}

		if ( !string.IsNullOrEmpty( SoundDriftBoost ) )
		{
			Sound.Play( SoundDriftBoost, WorldPosition );
		}
	}

	private void ApplyUprightStabilization()
	{
		var currentUp = WorldRotation.Up;
		var targetUp = Vector3.Up;
		
		float angle = Vector3.GetAngle( currentUp, targetUp );
		if ( angle > 1.0f )
		{
			// Help flip back if upside down and player presses SPACE
			if ( angle > 70f )
			{
				bool wantFlip = IsBotDriven ? true : Input.Pressed( "jump" );
				if ( wantFlip && _rb != null )
				{
					_rb.Velocity += Vector3.Up * 180f;
					WorldRotation = Rotation.From( 0f, WorldRotation.Yaw(), 0f );
					return;
				}
			}

			// Nudge out of dead-angle zone if almost perfectly upside down
			if ( angle > 155f )
			{
				_rb.AngularVelocity += WorldRotation.Forward * 5f;
			}
			else
			{
				var axis = Vector3.Cross( currentUp, targetUp );
				if ( axis.Length > 0.001f )
				{
					_rb.AngularVelocity = Vector3.Lerp( _rb.AngularVelocity, axis.Normal * angle * StabilizerStrength, Time.Delta * 10f );
				}
			}
		}
	}

	private void UpdateCargoModelVisibility()
	{
		if ( ItemContainer == null ) return;
		
		var children = ItemContainer.Children;
		if ( children.Count == 0 ) return;
		
		for ( int i = 0; i < children.Count; i++ )
		{
			if ( children[i] != null )
			{
				children[i].Enabled = i < ItemCount;
			}
		}
	}

	public void PlayCollectionSound( Vector3 pos )
	{
		if ( string.IsNullOrEmpty( SoundWheelWobbleClick ) ) return;
		
		if ( Time.Now - _lastCollectionTime < _comboWindow )
		{
			_collectionComboCount = (_collectionComboCount + 1).Clamp( 0, 8 );
		}
		else
		{
			_collectionComboCount = 0;
		}
		_lastCollectionTime = Time.Now;

		float targetPitch = 1.0f + ( _collectionComboCount * 0.12f );

		var snd = Sound.Play( SoundWheelWobbleClick, pos );
		if ( snd.IsValid() )
		{
			snd.Pitch = targetPitch;
		}
	}

	public void Push( Vector3 force )
	{
		if ( _rb != null )
		{
			_rb.Velocity += force;
		}
	}

	public void SpillCargo( int count, Vector3 pushNormal = default )
	{
		if ( ItemCount <= 0 ) return;

		int spillCount = count.Clamp( 1, Math.Min( ItemCount, 5 ) );
		var norm = pushNormal == default ? Vector3.Random : pushNormal;
		// Inherit the cart's current velocity so items don't clip back into it
		var cartVelocity = _rb != null ? _rb.Velocity : Vector3.Zero;

		for ( int i = 0; i < spillCount; i++ )
		{
			ItemCount--;

			var itemGo = new GameObject( true );
			itemGo.Name = "SpilledItem";
			itemGo.WorldPosition = WorldPosition + Vector3.Up * 25f + Vector3.Random * 8f;

			var droppedItem = itemGo.Components.Create<DroppedItem>();

			var itemRb = itemGo.Components.Get<Rigidbody>();
			if ( itemRb != null )
			{
				var scatterDir = ( norm + Vector3.Up * 1.3f + Vector3.Random * 0.5f ).Normal;
				// Velocity = cart momentum + scatter impulse so items fly away from the cart
				itemRb.Velocity = cartVelocity * 0.4f + scatterDir * 200f;
			}
		}
	}

	private void ExecuteStompExplosion()
	{
		// 1. Play impact sound
		if ( !string.IsNullOrEmpty( SoundCrashHard ) )
		{
			Sound.Play( SoundCrashHard, WorldPosition );
		}

		// 2. Spawn shockwave using green colored sparks
		if ( CollisionSparkPrefab != null )
		{
			for ( int i = 0; i < 8; i++ )
			{
				float angle = ( i / 8f ) * MathF.PI * 2f;
				var offset = new Vector3( MathF.Cos( angle ) * 45f, MathF.Sin( angle ) * 45f, -12f );
				var sparks = CollisionSparkPrefab.Clone( WorldPosition + offset );
				var r = sparks.Components.GetInDescendantsOrSelf<ModelRenderer>();
				if ( r != null )
				{
					r.Tint = new Color( 0f, 1f, 0.2f, 1f );
				}
				DestroyAfterDelay( sparks, 1.2f );
			}
		}

		// 3. Scan and knock back nearby bot carts/pedestrians
		float stompRadius = 240f;
		var allCarts = Scene.GetAllComponents<ShoppingCartController>().Where( c => c != this ).ToList();
		foreach ( var otherCart in allCarts )
		{
			float dist = ( otherCart.WorldPosition - WorldPosition ).Length;
			if ( dist <= stompRadius )
			{
				var pushDir = ( otherCart.WorldPosition - WorldPosition ).WithZ( 0 ).Normal;
				otherCart.Push( pushDir * 380f + Vector3.Up * 250f );

				if ( otherCart.ItemCount > 0 && Random.Shared.Float() < 0.6f )
				{
					otherCart.SpillCargo( Random.Shared.Next( 1, 3 ), pushDir );
				}
			}
		}

		// Knock back pedestrians
		var pedestrians = Scene.GetAllComponents<Pedestrian>().ToList();
		foreach ( var ped in pedestrians )
		{
			float dist = ( ped.WorldPosition - WorldPosition ).Length;
			if ( dist <= stompRadius )
			{
				var pushDir = ( ped.WorldPosition - WorldPosition ).WithZ( 0 ).Normal;
				ped.HitByCart( pushDir * 400f + Vector3.Up * 180f, 400f );
			}
		}

		// 4. Forward speed boost dash
		if ( _rb != null )
		{
			_rb.Velocity += WorldRotation.Forward * 220f;
		}

		// 5. Trigger intense camera screenshake
		LastImpactSpeed = 280f;
	}
}

public sealed class FlickerLight : Component
{
	private PointLight _light;
	private Color _baseColor;

	protected override void OnStart()
	{
		_light = Components.Get<PointLight>();
		if ( _light != null )
		{
			_baseColor = _light.LightColor;
		}
	}

	protected override void OnUpdate()
	{
		if ( _light == null ) return;
		float flickerMultiplier = 0.6f + 0.4f * MathF.Sin( Time.Now * 95f + Random.Shared.Float( 0f, 100f ) );
		_light.LightColor = _baseColor * flickerMultiplier;
	}
}
