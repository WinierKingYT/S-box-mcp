using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McpBridge.Tools;

[McpToolGroup("Store")]
public class StoreTools
{
	[McpTool("sbox_game_summary", "Full game state: phase, day, economy, alarm, bots")]
	public object GameSummary()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };

		var gm = scene.GetAllComponents<BlackFridayGameManager>().FirstOrDefault();
		var quota = scene.GetAllComponents<QuotaManager>().FirstOrDefault();
		var alarm = scene.GetAllComponents<AlarmSystem>().FirstOrDefault();
		var bots = scene.GetAllComponents<DummyBot>().ToList();
		var checkouts = scene.GetAllComponents<CheckoutZone>().ToList();

		return new
		{
			phase = gm != null ? new { day = gm.CurrentDay, phase = gm.CurrentPhase.ToString(), timeRemaining = gm.PhaseTimeRemaining, progress = gm.GetPhaseProgress() } : null,
			economy = quota != null ? new { personalCash = quota.MyPersonalCash, personalQuota = quota.PersonalQuota, poolCurrent = quota.SharedPoolCurrent, poolTarget = quota.SharedPoolTarget, isEliminated = quota.IsEliminated } : null,
			alarm = alarm != null ? new { level = alarm.GetAlarmLevelName(), progress = alarm.AlarmProgress } : null,
			bots = bots.Count,
			checkouts = checkouts.Count
		};
	}

	[McpTool("sbox_get_quota", "Current player quota and economy status")]
	public object GetQuota()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var quota = scene.GetAllComponents<QuotaManager>().FirstOrDefault();
		if ( quota == null ) return new { error = "No QuotaManager found" };
		return new
		{
			personalCash = quota.MyPersonalCash,
			personalQuota = quota.PersonalQuota,
			metPersonalQuota = quota.HasMetPersonalQuota(),
			poolCurrent = quota.SharedPoolCurrent,
			poolTarget = quota.SharedPoolTarget,
			metPoolQuota = quota.HasMetSharedPool(),
			isEliminated = quota.IsEliminated
		};
	}

	[McpTool("sbox_get_alarm", "Current security alarm level")]
	public object GetAlarm()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var alarm = scene.GetAllComponents<AlarmSystem>().FirstOrDefault();
		if ( alarm == null ) return new { error = "No AlarmSystem found" };
		return new { level = alarm.GetAlarmLevelName(), levelIndex = alarm.CurrentAlarmLevel, progress = alarm.AlarmProgress, detectionRange = alarm.DetectionRange };
	}

	[McpTool("sbox_trigger_alarm", "Trigger the security alarm")]
	public object TriggerAlarm( float amount = 10f, float x = 0f, float y = 0f, float z = 0f )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var alarm = scene.GetAllComponents<AlarmSystem>().FirstOrDefault();
		if ( alarm == null ) return new { error = "No AlarmSystem found" };
		alarm.TriggerAlarm( amount, new Vector3( x, y, z ) );
		return new { success = true, newLevel = alarm.GetAlarmLevelName() };
	}

	[McpTool("sbox_get_checkout_zones", "List all checkout zones with cashier patience")]
	public object GetCheckoutZones()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var zones = scene.GetAllComponents<CheckoutZone>().ToList();
		return new
		{
			count = zones.Count,
			zones = zones.Select( z => new
			{
				position = z.WorldPosition,
				cashierPatience = z.CashierPatience,
				maxPatience = z.MaxPatience,
				isLocked = z.IsLocked,
				checkRadius = z.CheckRadius
			} ).ToList()
		};
	}

	[McpTool("sbox_get_blackmarket_items", "List available black market items")]
	public object GetBlackMarketItems()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var bm = scene.GetAllComponents<BlackMarket>().FirstOrDefault();
		if ( bm == null ) return new { error = "No BlackMarket found" };

		var quota = scene.GetAllComponents<QuotaManager>().FirstOrDefault();
		var playerCash = quota?.MyPersonalCash ?? 0f;

		return new
		{
			items = bm.Items.Select( ( item, idx ) => new
			{
				index = idx,
				name = item.Name,
				description = item.Description,
				basePrice = item.BasePrice,
				actualPrice = item.BasePrice * bm.BuyMultiplier,
				category = item.Category.ToString(),
				canAfford = playerCash >= item.BasePrice * bm.BuyMultiplier
			} ).ToList()
		};
	}

	[McpTool("sbox_get_bots", "List all DummyBot NPCs in the scene")]
	public object GetBots()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var bots = scene.GetAllComponents<DummyBot>().ToList();
		return new
		{
			count = bots.Count,
			bots = bots.Select( b => new
			{
				name = b.GameObject.Name,
				position = b.WorldPosition,
				moveSpeed = b.MoveSpeed,
				collectRange = b.CollectRange,
				errorChance = b.ErrorChance
			} ).ToList()
		};
	}

	[McpTool("sbox_get_scene_items", "List all CartItem components in the scene")]
	public object GetSceneItems()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var items = scene.GetAllComponents<CartItem>().ToList();
		return new
		{
			count = items.Count,
			items = items.Select( i => new
			{
				name = i.ItemName,
				value = i.Value,
				category = i.Category.ToString(),
				isStolen = i.IsStolen,
				weight = i.Weight,
				gameObject = i.GameObject.Name
			} ).ToList()
		};
	}

	[McpTool("sbox_get_shelves", "List all ProductSpawner shelves with stock info")]
	public object GetShelves()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var spawners = scene.GetAllComponents<ProductSpawner>().ToList();
		return new
		{
			count = spawners.Count,
			shelves = spawners.Select( s => new
			{
				position = s.WorldPosition,
				prefab = s.ItemPrefab?.Name,
				itemsPerShelf = s.ItemsPerShelf,
				spawnRadius = s.SpawnRadius
			} ).ToList()
		};
	}
}

[McpToolGroup("Mechanics")]
public class MechanicsTools
{
	[McpTool("sbox_spawn_customer", "Spawns a DummyBot customer at a position")]
	public object SpawnCustomer( float x = 0f, float y = 0f, float z = 0f, float speed = 200f, float collectRange = 80f )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var go = new GameObject();
		go.Name = $"Customer_{Guid.NewGuid().ToString().Substring( 0, 6 )}";
		go.WorldPosition = new Vector3( x, y, z );
		var bot = go.Components.Create<DummyBot>();
		bot.MoveSpeed = speed;
		bot.CollectRange = collectRange;
		return new { success = true, guid = go.Id, name = go.Name, position = new { x, y, z } };
	}

	[McpTool("sbox_set_item_price", "Override prices for all items matching a value range")]
	public object SetItemPrice( float minValue = 0f, float maxValue = 999f, float newValue = 10f )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var items = scene.GetAllComponents<CartItem>().ToList();
		var changed = 0;
		foreach ( var item in items )
		{
			if ( item.Value >= minValue && item.Value <= maxValue )
			{
				item.Value = newValue;
				changed++;
			}
		}
		return new { success = true, changed, newValue };
	}

	[McpTool("sbox_cart_boost", "Apply a nitro boost to the player's cart")]
	public object CartBoost()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var cart = scene.GetAllComponents<ShoppingCartController>().FirstOrDefault();
		if ( cart == null ) return new { error = "No cart found" };
		cart.HasNitro = cart.NitroMultiplier > 1f;
		return new { success = true };
	}

	[McpTool("sbox_force_phase", "Force the game to advance to the next phase immediately")]
	public object ForcePhase()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var gm = scene.GetAllComponents<BlackFridayGameManager>().FirstOrDefault();
		if ( gm == null ) return new { error = "No game manager found" };
		gm.PhaseTimeRemaining = 0f;
		return new { success = true, newPhase = gm.CurrentPhase.ToString(), day = gm.CurrentDay };
	}

	[McpTool("sbox_respawn_shelves", "Respawn all items on all ProductSpawner shelves")]
	public object RespawnShelves()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return new { error = "No active scene" };
		var spawners = scene.GetAllComponents<ProductSpawner>().ToList();
		foreach ( var s in spawners )
		{
			s.ClearShelf( s.GameObject );
			s.SpawnItemsOnShelf( s.GameObject );
		}
		return new { success = true, shelvesRespawned = spawners.Count };
	}
}
