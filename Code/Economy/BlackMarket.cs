using Sandbox;
using System;
using System.Collections.Generic;

[Icon( "store" )]
public sealed class BlackMarket : Component
{
	[Property] public List<BlackMarketItemDef> Items { get; set; } = new();
	[Property] public float BuyMultiplier { get; set; } = 1.5f;

	public bool TryBuy( int itemIndex, QuotaManager quota )
	{
		if ( itemIndex < 0 || itemIndex >= Items.Count ) return false;
		var item = Items[itemIndex];
		float cost = item.BasePrice * BuyMultiplier;
		if ( quota.MyPersonalCash < cost ) return false;
		quota.AddPersonalCash( -cost );
		Log.Info( $"Bought {item.Name} for ${cost}" );
		return true;
	}
}

public sealed class BlackMarketItemDef
{
	public string Name { get; set; } = "";
	public string Description { get; set; } = "";
	public float BasePrice { get; set; } = 100f;
	public BlackMarketItemCategory Category { get; set; } = BlackMarketItemCategory.CartPart;
}

public enum BlackMarketItemCategory
{
	CartPart,
	Weapon,
	Sabotage
}
