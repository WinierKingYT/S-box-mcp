using Sandbox;
using System;

[Icon( "smart_toy" )]
public sealed class DummyBot : Component
{
	[Property] public float MoveSpeed { get; set; } = 200f;
	[Property] public float CollectRange { get; set; } = 80f;
	[Property] public float ErrorChance { get; set; } = 0.15f;

	private Rigidbody _rb;
	private GameObject _targetItem;
	private GameObject _checkoutZone;
	private bool _isReturning;
	private float _errorTimer;

	protected override void OnStart()
	{
		_rb = Components.Get<Rigidbody>();
		if ( _rb == null )
			_rb = Components.Create<Rigidbody>();

		_errorTimer = Random.Shared.Float( 3f, 8f );
	}

	protected override void OnFixedUpdate()
	{
		if ( _errorTimer > 0 )
		{
			_errorTimer -= Time.Delta;
			return;
		}

		// Simulate error: get caught by security sometimes
		if ( Random.Shared.Float( 1f ) < ErrorChance * Time.Delta )
		{
			_errorTimer = Random.Shared.Float( 5f, 10f );
			_rb.Velocity = Vector3.Zero;
			return;
		}

		if ( _isReturning )
		{
			MoveToCheckout();
		}
		else
		{
			FindAndCollectItem();
		}
	}

	private void FindAndCollectItem()
	{
		if ( _targetItem == null || !_targetItem.IsValid )
		{
			FindNearestItem();
			if ( _targetItem == null )
			{
				_isReturning = true;
				return;
			}
		}

		var dir = (_targetItem.WorldPosition - WorldPosition).Normal;
		_rb.Velocity = dir * MoveSpeed;

		float dist = WorldPosition.Distance( _targetItem.WorldPosition );
		if ( dist < CollectRange )
		{
			_targetItem.Destroy();
			_targetItem = null;

			if ( Random.Shared.Float( 1f ) < 0.3f )
				_isReturning = true;
		}
	}

	private void FindNearestItem()
	{
		var items = Scene.FindInPhysics( new Sphere( WorldPosition, 1000f ) );
		float nearest = float.MaxValue;

		foreach ( var obj in items )
		{
			if ( !obj.Tags.Has( "pickup" ) ) continue;
			float dist = WorldPosition.Distance( obj.WorldPosition );
			if ( dist < nearest )
			{
				nearest = dist;
				_targetItem = obj;
			}
		}
	}

	private void MoveToCheckout()
	{
		if ( _checkoutZone == null || !_checkoutZone.IsValid )
		{
			FindCheckout();
			if ( _checkoutZone == null ) return;
		}

		var dir = (_checkoutZone.WorldPosition - WorldPosition).Normal;
		_rb.Velocity = dir * MoveSpeed;

		float dist = WorldPosition.Distance( _checkoutZone.WorldPosition );
		if ( dist < CollectRange )
		{
			_isReturning = false;
		}
	}

	private void FindCheckout()
	{
		var zones = Scene.FindInPhysics( new Sphere( WorldPosition, 2000f ) );
		foreach ( var obj in zones )
		{
			if ( obj.Tags.Has( "checkout" ) )
			{
				_checkoutZone = obj;
				return;
			}
		}
	}
}
