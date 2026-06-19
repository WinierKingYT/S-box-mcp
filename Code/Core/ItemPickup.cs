using Sandbox;
using System;

public enum ItemCategory
{
	Food, Drink, Cleaning, Toy, Cosmetic, Electronics, Fashion, Luxury
}

public sealed class ItemPickup : Component
{
	[Property] public float PickupRange { get; set; } = 100f;
	[Property] public float ThrowForce { get; set; } = 400f;
	[Property] public int MaxCartSlots { get; set; } = 20;

	private ShoppingCartController _cart;
	private GameObject _highlight;

	protected override void OnStart()
	{
		_cart = Components.Get<ShoppingCartController>();
		if ( _cart == null )
			_cart = Components.GetInAncestors<ShoppingCartController>();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		var cam = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( cam == null ) return;

		var trace = Scene.Trace.Ray( cam.Transform.Position, cam.Transform.Position + cam.Transform.Rotation.Forward * PickupRange )
			.WithAnyTags( "pickup" ).Run();

		if ( trace.Hit && trace.GameObject.Tags.Has( "pickup" ) )
		{
			Highlight( trace.GameObject );
			if ( Input.Pressed( "use" ) ) Pickup( trace.GameObject );
		}
		else ClearHighlight();

		if ( Input.Pressed( "attack1" ) ) ThrowItem();
	}

	private void Pickup( GameObject obj )
	{
		if ( _cart == null || _cart.ItemCount >= MaxCartSlots ) return;
		obj.Destroy();
		_cart.ItemCount++;
	}

	private void ThrowItem()
	{
		if ( _cart == null || _cart.ItemCount <= 0 ) return;
		_cart.ItemCount--;

		var go = new GameObject( true, "Thrown" );
		go.Transform.Position = Transform.Position + Transform.Rotation.Forward * 50f;
		go.Tags.Add( "pickup" );
		var rb = go.Components.Create<Rigidbody>();
		rb.Velocity = Transform.Rotation.Forward * ThrowForce + Vector3.Up * 50f;

		if ( Game.IsEditor )
		{
			var mr = go.Components.Create<ModelRenderer>();
			mr.Model = Model.Load( "models/editor/error.vmdl" );
		}
	}

	private void Highlight( GameObject obj )
	{
		if ( _highlight == obj ) return;
		ClearHighlight();
		_highlight = obj;
	}

	private void ClearHighlight()
	{
		_highlight = null;
	}
}
