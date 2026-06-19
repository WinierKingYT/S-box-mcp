using Sandbox;
using System;

public sealed class BlackFridayHUD : Component
{
	[Property] public float UpdateInterval { get; set; } = 0.2f;

	private BlackFridayGameManager _gm;
	private TimeSince _lastUpdate;

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;
		if ( _lastUpdate < UpdateInterval ) return;
		_lastUpdate = 0;

		DrawHUD();
	}

	private void DrawHUD()
	{
		var gm = Game.ActiveScene?.GetAllComponents<BlackFridayGameManager>().FirstOrDefault();
		var cart = Components.Get<ShoppingCartController>();
		var quota = Game.ActiveScene?.GetAllComponents<QuotaManager>().FirstOrDefault();
		var checkout = Game.ActiveScene?.GetAllComponents<CheckoutZone>().FirstOrDefault();

		string phaseText = gm?.CurrentPhase.ToString() ?? "---";
		int day = gm?.CurrentDay ?? 1;
		float phasePct = gm != null ? (gm.PhaseTimeRemaining / 60f) * 100f : 0;
		float speed = cart?.CurrentSpeed ?? 0;
		int items = cart?.ItemCount ?? 0;
		float quotaVal = quota?.PersonalQuota ?? 0;
		float cash = quota?.MyPersonalCash ?? 0;
		float shared = quota?.SharedPoolCurrent ?? 0;
		float sharedTarget = quota?.SharedPoolTarget ?? 0;
		float patience = checkout?.CashierPatience ?? 0;
		bool locked = checkout?.IsLocked ?? false;

		string text = $"Day {day} | {phaseText} [{phasePct:F0}%]\n" +
			$"Speed: {speed:F0} | Items: {items}\n" +
			$"Cash: ${cash:F0} / ${quotaVal:F0}\n" +
			$"Pool: ${shared:F0} / ${sharedTarget:F0}\n" +
			$"Cashier: {patience:F0}%" + (locked ? " [LOCKED!]" : "");

		DebugOverlay.ScreenText( Vector2.Zero, text, 0, TextFlag.Left, Color.White );
	}
}
