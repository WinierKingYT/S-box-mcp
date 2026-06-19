using Sandbox;
using System;

[Icon( "money" )]
public sealed class QuotaManager : Component
{
	[Property] public float BasePersonalQuota { get; set; } = 1000f;
	[Property] public float BaseSharedPoolTarget { get; set; } = 5000f;
	[Property] public float QuotaIncreasePerDay { get; set; } = 500f;
	[Property] public float DoomsdayQuota { get; set; } = 1_000_000f;
	[Property] public int DoomsdayDay { get; set; } = 30;

	[Sync] public float PersonalQuota { get; set; }
	[Sync] public float SharedPoolTarget { get; set; }
	[Sync] public float SharedPoolCurrent { get; set; }
	[Sync] public float MyPersonalCash { get; set; }
	[Sync] public bool IsEliminated { get; set; }

	protected override void OnStart()
	{
		ResetQuota();
	}

	public void ResetQuota()
	{
		var gm = Game.ActiveScene?.GetAllComponents<BlackFridayGameManager>().FirstOrDefault();
		int day = gm?.CurrentDay ?? 1;

		if ( day >= DoomsdayDay )
		{
			PersonalQuota = DoomsdayQuota;
			SharedPoolTarget = DoomsdayQuota * 4;
			return;
		}

		PersonalQuota = BasePersonalQuota + (QuotaIncreasePerDay * (day - 1));
		SharedPoolTarget = BaseSharedPoolTarget + (QuotaIncreasePerDay * 2 * (day - 1));
	}

	[Rpc.Broadcast]
	public void ContributeToPool( float amount )
	{
		SharedPoolCurrent += amount;
	}

	[Rpc.Broadcast]
	public void AddPersonalCash( float amount )
	{
		MyPersonalCash += amount;
	}

	public bool HasMetPersonalQuota() => MyPersonalCash >= PersonalQuota;
	public bool HasMetSharedPool() => SharedPoolCurrent >= SharedPoolTarget;

	[Rpc.Broadcast]
	public void EliminateMe() { IsEliminated = true; }
}
