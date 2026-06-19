using Sandbox;
using System;
using System.Collections.Generic;

namespace McpBridge;

public class WorldSnapshot
{
	public string Name { get; set; }
	public string SceneName { get; set; }
	public EconomyState Economy { get; set; } = new();
	public List<CartState> Carts { get; set; } = new();
	public List<NpcState> Npcs { get; set; } = new();
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class EconomyState
{
	public decimal BaseMultiplier { get; set; } = 1.0m;
	public decimal SaleMultiplier { get; set; } = 1.0m;
	public int TotalSales { get; set; }
	public decimal TotalRevenue { get; set; }
}

public class CartState
{
	public int NetworkIdent { get; set; }
	public Vector3 Position { get; set; }
	public Angles Rotation { get; set; }
}

public class NpcState
{
	public int NetworkIdent { get; set; }
	public Vector3 Position { get; set; }
	public string Behavior { get; set; } = "wander";
}
