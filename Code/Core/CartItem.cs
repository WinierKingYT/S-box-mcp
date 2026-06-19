using Sandbox;

public sealed class CartItem : Component
{
	[Property] public string ItemName { get; set; } = "Unknown";
	[Property] public float Value { get; set; } = 10f;
	[Property] public ItemCategory Category { get; set; } = ItemCategory.Food;
	[Property] public bool IsStolen { get; set; } = false;
	[Property] public float Weight { get; set; } = 1f;
}
