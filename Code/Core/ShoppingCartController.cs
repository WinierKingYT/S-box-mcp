using Sandbox;
using System;

public sealed class ShoppingCartController : Component
{
	[Property] public float MaxSpeed { get; set; } = 400f;
	[Property] public float Acceleration { get; set; } = 800f;
	[Property] public float Braking { get; set; } = 600f;
	[Property] public float DriftFriction { get; set; } = 0.3f;
	[Property] public float NormalFriction { get; set; } = 4.0f;
	[Property] public float TurnSpeed { get; set; } = 3.0f;
	[Property] public float NitroMultiplier { get; set; } = 2.0f;
	[Property] public float NitroDuration { get; set; } = 2.0f;
	[Property] public float NitroRecharge { get; set; } = 5.0f;
	[Property] public float HitForce { get; set; } = 500f;
	[Property] public float MinSpeedForDamage { get; set; } = 150f;
	[Property] public int MaxItems { get; set; } = 20;

	[Sync] public float CurrentSpeed { get; set; }
	[Sync] public bool IsDrifting { get; set; }
	[Sync] public bool HasNitro { get; set; } = true;
	[Sync] public int ItemCount { get; set; }

	private Rigidbody _rb;
	private float _nitroTimer;
	private float _rechargeTimer;
	private Vector3 _velocity;
	private float _dropCooldown;

	protected override void OnStart()
	{
		_rb = Components.Get<Rigidbody>();
		if ( _rb == null )
		{
			_rb = Components.Create<Rigidbody>();
		}
		_rb.Gravity = true;
		_rb.LinearDamping = NormalFriction;
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy )
		{
			if ( _rb != null ) _rb.Velocity = _velocity;
			return;
		}

		var fwd = WorldRotation.Forward.WithZ( 0 ).Normal;

		float inputX = Input.Down( "forward" ) ? 1 : Input.Down( "backward" ) ? -1 : 0;
		float inputZ = Input.Down( "left" ) ? -1 : Input.Down( "right" ) ? 1 : 0;

		float speed = _rb.Velocity.Length;
		float turnFactor = (speed / MaxSpeed).Clamp( 0.2f, 1.0f );

		IsDrifting = Input.Down( "run" ) && inputX != 0;

		if ( Input.Pressed( "attack2" ) && HasNitro )
		{
			HasNitro = false;
			_nitroTimer = NitroDuration;
		}

		if ( _nitroTimer > 0 )
		{
			_nitroTimer -= Time.Delta;
			if ( _nitroTimer <= 0 ) _rechargeTimer = NitroRecharge;
		}

		if ( _rechargeTimer > 0 )
		{
			_rechargeTimer -= Time.Delta;
			if ( _rechargeTimer <= 0 ) HasNitro = true;
		}

		_dropCooldown = Math.Max( 0, _dropCooldown - Time.Delta );
		float speedMult = _nitroTimer > 0 ? NitroMultiplier : 1f;

		if ( inputX != 0 )
		{
			float accel = inputX > 0 ? Acceleration : Braking;
			_rb.Velocity += fwd * inputX * accel * Time.Delta * speedMult;
		}

		if ( inputZ != 0 )
		{
			float rotAmount = inputZ * TurnSpeed * turnFactor * Time.Delta * 100f;

			if ( IsDrifting )
			{
				_rb.Velocity = _rb.Velocity * (1f - DriftFriction * Time.Delta);
				rotAmount *= 2f;
			}

			WorldRotation = WorldRotation * Rotation.FromAxis( Vector3.Up, rotAmount );
		}

		float max = MaxSpeed * speedMult;
		if ( _rb.Velocity.Length > max )
			_rb.Velocity = _rb.Velocity.Normal * max;

		_rb.LinearDamping = IsDrifting ? DriftFriction : NormalFriction;
		CurrentSpeed = _rb.Velocity.Length;
		_velocity = _rb.Velocity;

		// Simple collision check for item drop
		_dropCooldown = Math.Max( 0, _dropCooldown - Time.Delta );
		if ( _dropCooldown <= 0 && _rb.Velocity.Length > MinSpeedForDamage )
		{
			var tr = Scene.Trace.Ray( WorldPosition, WorldPosition + _rb.Velocity.Normal * 50f )
				.WithAnyTags( "cart", "shelf" )
				.WithoutTags( "player" )
				.Run();

			if ( tr.Hit && ItemCount > 0 )
			{
				ItemCount--;
				_dropCooldown = 0.5f;
			}
		}
	}

	public void ApplyHitForce( Vector3 force )
	{
		if ( _rb != null ) _rb.Velocity += force;
	}
}
