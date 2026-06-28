using Sandbox;
using System;
using Code.Player;
using PlayerController = Code.Player.PlayerController;

public sealed class PlayerCamera : Component
{
	[Property] public PlayerController Player { get; set; }
	[Property] public float Distance { get; set; } = 150f;
	[Property] public float DrivingDistance { get; set; } = 250f;
	[Property] public float CameraOffsetHeight { get; set; } = 30f;

	// Speed-based Field of View effect (Juice Factor)
	[Property] public float BaseFOV { get; set; } = 75f;
	[Property] public float MaxFOVEffect { get; set; } = 15f;

	// Boost Warp effects (Juice Factor)
	[Property] public float BoostFOVOffset { get; set; } = 12f;
	[Property] public float BoostDistanceOffset { get; set; } = 40f;

	// Turn-based Camera Roll effect (Juice Factor)
	[Property] public float MaxCameraRoll { get; set; } = 7.0f;

	// Acceleration-based Camera Elasticity (Juice Factor)
	[Property] public float Elasticity { get; set; } = 0.12f;

	private CameraComponent _camera;
	private float _currentDistance;
	private float _currentFOV;
	private float _currentRoll;
	
	// Dynamic collision / landing shake
	private float _impactShake;

	// Snap Zoom Kick (Phase 5 Polish)
	private float _boostDistanceKick;
	private float _boostFOVKick;
	private bool _wasBoostingLastFrame;
	private bool _wasNitroActiveLastFrame;

	protected override void OnStart()
	{
		_camera = Components.Get<CameraComponent>();
		if ( _camera == null )
		{
			_camera = Components.Create<CameraComponent>();
		}
		_currentDistance = Distance;
		_currentFOV = BaseFOV;
		_camera.FieldOfView = BaseFOV;
	}

	protected override void OnUpdate()
	{
		if ( Player == null || _camera == null ) return;

		// Smooth distance transition between driving and foot states
		float targetDistance = Player.State == PlayerState.Driving ? DrivingDistance : Distance;
		
		// Pull camera further back during boost for warp speed feel (Phase 2 Juice)
		if ( Player.State == PlayerState.Driving && Player.ActiveCart != null )
		{
			bool isCurrentlyBoosting = Player.ActiveCart.IsBoosting;
			if ( isCurrentlyBoosting )
			{
				targetDistance += BoostDistanceOffset;
				
				// Snap Zoom Kick triggers on the transition frame to boosting
				if ( !_wasBoostingLastFrame )
				{
					_boostDistanceKick = 65f;
					_boostFOVKick = 18f;
				}
			}
			_wasBoostingLastFrame = isCurrentlyBoosting;

			// Nitro Startup Zoom Kick (Phase 5 Polish)
			bool isCurrentlyNitro = Player.ActiveCart.IsNitroActive;
			if ( isCurrentlyNitro && !_wasNitroActiveLastFrame )
			{
				_boostDistanceKick = 45f;
				_boostFOVKick = 12f;
			}
			_wasNitroActiveLastFrame = isCurrentlyNitro;
		}
		else
		{
			_wasBoostingLastFrame = false;
			_wasNitroActiveLastFrame = false;
		}

		// Decay the snap kick multipliers
		_boostDistanceKick = _boostDistanceKick.LerpTo( 0f, Time.Delta * 4.5f );
		_boostFOVKick = _boostFOVKick.LerpTo( 0f, Time.Delta * 4.5f );

		// Apply the kick to the target distance
		targetDistance += _boostDistanceKick;

		_currentDistance = _currentDistance.LerpTo( targetDistance, Time.Delta * 5f );

		// Speed-based FOV scaling: widen view at high speeds for a better sense of velocity
		float speed = 0f;
		float maxSpeed = 350f;
		float turnInput = 0f;
		bool isBoosting = false;
		bool isNitroActive = false;
		bool isOverheated = false;
		float nitroFuel = 100f;

		if ( Player.State == PlayerState.Driving && Player.ActiveCart != null )
		{
			speed = Player.ActiveCart.CartVelocity.Length;
			maxSpeed = Player.ActiveCart.MaxSpeed;
			turnInput = Player.ActiveCart.CurrentTurnInput;
			isBoosting = Player.ActiveCart.IsBoosting;
			isNitroActive = Player.ActiveCart.IsNitroActive;
			isOverheated = Player.ActiveCart.IsOverheated;
			nitroFuel = Player.ActiveCart.NitroFuel;

			// Read crash/landing impacts from cart for camera shakes
			float impact = Player.ActiveCart.LastImpactSpeed;
			if ( impact > 10f )
			{
				_impactShake += impact * 0.08f;
				_impactShake = _impactShake.Clamp( 0f, 25f ); // Cap maximum shake intensity
				
				// Kick FOV out dynamically for high impacts (e.g. backfires or hard crashes)
				if ( impact > 100f )
				{
					_currentFOV += (impact - 100f) * 0.18f;
				}
				
				Player.ActiveCart.LastImpactSpeed = 0f; // Reset trigger
			}
		}
		
		float speedRatio = (speed / maxSpeed).Clamp( 0f, 1f );
		
		// 1. Dynamic FOV
		float targetFOV = BaseFOV + (speedRatio * MaxFOVEffect);
		if ( isBoosting || isNitroActive )
		{
			targetFOV += BoostFOVOffset;
		}
		targetFOV += _boostFOVKick;

		_currentFOV = _currentFOV.LerpTo( targetFOV, Time.Delta * 4f );
		
		// Add high-speed pulse wobble (speed warp lines sensation)
		if ( isNitroActive || isBoosting )
		{
			_currentFOV += MathF.Sin( Time.Now * 35f ) * 0.8f;
		}
		_camera.FieldOfView = _currentFOV;

		// 2. Dynamic Camera Roll (tilts camera into the turn/drift for action feel)
		float targetRoll = -turnInput * speedRatio * MaxCameraRoll;
		
		// If boosting or using nitro, add a slight roll vibration
		if ( isBoosting || isNitroActive )
		{
			targetRoll += MathF.Sin( Time.Now * 40f ) * 1.5f; // Small high-frequency shake
		}

		_currentRoll = _currentRoll.LerpTo( targetRoll, Time.Delta * 6f );

		// Set camera rotation from player's EyeAngles combined with dynamic roll
		WorldRotation = Player.EyeAngles.ToRotation() * Rotation.FromRoll( _currentRoll );

		// Position camera behind target height offset
		var targetPos = Player.WorldPosition + Vector3.Up * (Player.EyeHeight + CameraOffsetHeight);

		// 3. Camera Elasticity (Juice): lag behind during acceleration/boost, push forward during heavy braking
		if ( Player.State == PlayerState.Driving && Player.ActiveCart != null )
		{
			var localVelocity = Player.ActiveCart.WorldRotation.Inverse * Player.ActiveCart.CartVelocity;
			var lagOffset = Player.ActiveCart.WorldRotation.Forward * (-localVelocity.x * Elasticity);
			targetPos += lagOffset;
		}

		// 4. Dampen and apply collision/landing physical camera shakes (Phase 2 Juice)
		_impactShake = _impactShake.LerpTo( 0f, Time.Delta * 8f );
		if ( _impactShake > 0.02f )
		{
			float shakeX = MathF.Sin( Time.Now * 50f ) * _impactShake * 0.06f;
			float shakeY = MathF.Cos( Time.Now * 45f ) * _impactShake * 0.06f;
			targetPos += WorldRotation.Right * shakeX + WorldRotation.Up * shakeY;
		}

		// 5. Nitro / Overheat Engine Vibration (Phase 3 Juice)
		float nitroShake = 0f;
		if ( isNitroActive )
		{
			nitroShake = 0.5f; // Small constant engine buzz during Nitro
		}
		else if ( isOverheated )
		{
			// Heavy engine rattle shake when overheated (decreases as it cools down)
			nitroShake = 2.0f * (1f - (nitroFuel / 100f));
		}

		if ( nitroShake > 0.05f )
		{
			float rattleX = MathF.Sin( Time.Now * 95f ) * nitroShake * 0.05f;
			float rattleY = MathF.Cos( Time.Now * 90f ) * nitroShake * 0.05f;
			targetPos += WorldRotation.Right * rattleX + WorldRotation.Up * rattleY;
		}

		// 6. Speed-based rickety camera shake (Phase 5 Polish)
		if ( speedRatio > 0.4f )
		{
			float speedShake = ( speedRatio - 0.4f ) * 0.8f;
			float rattleX = MathF.Sin( Time.Now * 80f ) * speedShake;
			float rattleY = MathF.Cos( Time.Now * 75f ) * speedShake;
			targetPos += WorldRotation.Right * rattleX + WorldRotation.Up * rattleY;
		}

		var backward = WorldRotation.Backward;
		
		// Raycast to prevent camera clipping through walls
		var trace = Scene.Trace.Ray( targetPos, targetPos + backward * _currentDistance )
			.WithoutTags( "player", "cart" )
			.Run();

		WorldPosition = trace.EndPosition;
	}
}
