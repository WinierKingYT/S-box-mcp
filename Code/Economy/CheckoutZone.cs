using Sandbox;
using System;

[Icon( "payments" )]
public sealed class CheckoutZone : Component
{
	[Property] public float CheckRadius { get; set; } = 100f;
	[Property] public float PatienceDecreaseRate { get; set; } = 2f;
	[Property] public float PatienceIncreaseOnJump { get; set; } = 15f;
	[Property] public float MaxPatience { get; set; } = 100f;
	[Property] public float BribeCost { get; set; } = 100f;
	[Property] public float BribeRestore { get; set; } = 30f;
	[Property] public float QteCooldown { get; set; } = 10f;

	[Sync] public float CashierPatience { get; set; } = 100f;
	[Sync] public bool IsLocked { get; set; }

	private QuotaManager _quota;
	private float _lockTimer;
	private float _qteTimer;

	protected override void OnStart()
	{
		_quota = Components.Get<QuotaManager>();
		if ( _quota == null )
			_quota = Components.GetInAncestors<QuotaManager>();
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;

		_qteTimer = Math.Max( 0, _qteTimer - Time.Delta );

		if ( IsLocked )
		{
			_lockTimer -= Time.Delta;
			if ( _lockTimer <= 0 ) { IsLocked = false; CashierPatience = MaxPatience; }
			return;
		}

		CashierPatience = Math.Min( CashierPatience + PatienceDecreaseRate * Time.Delta, MaxPatience );
	}

	public bool TryCheckout( ShoppingCartController cart, out float payout )
	{
		payout = 0;
		if ( IsLocked || cart == null || cart.ItemCount <= 0 ) return false;

		float val = cart.ItemCount * 20f;
		_quota?.ContributeToPool( val );
		_quota?.AddPersonalCash( val );
		cart.ItemCount = 0;
		payout = val;
		return true;
	}

	public bool TryQueueJump()
	{
		if ( _qteTimer > 0 ) return false;
		CashierPatience = Math.Min( CashierPatience + PatienceIncreaseOnJump, MaxPatience );
		_qteTimer = QteCooldown;
		if ( CashierPatience >= MaxPatience ) { LockCashier(); return false; }
		return true;
	}

	public void OnBribe()
	{
		CashierPatience = Math.Max( 0, CashierPatience - BribeRestore );
	}

	private void LockCashier() { IsLocked = true; _lockTimer = 10f; }

	protected override void DrawGizmos()
	{
		Gizmo.Draw.LineSphere( Vector3.Zero, CheckRadius );
	}
}
