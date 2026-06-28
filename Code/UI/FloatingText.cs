using Sandbox;
using System;

namespace Code.UI;

public sealed class FloatingText : Component
{
	[Property] public string Text { get; set; } = "+$25";
	[Property] public Color TextColor { get; set; } = Color.Green;
	[Property] public float Lifetime { get; set; } = 1.2f;
	[Property] public float Speed { get; set; } = 40f;

	private TextRenderer _renderer;
	private float _timer;

	protected override void OnStart()
	{
		_renderer = Components.GetOrCreate<TextRenderer>();
		if ( _renderer != null )
		{
			_renderer.Text = Text;
			_renderer.Color = TextColor;
			_renderer.Scale = 0.22f;
		}
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		_timer += Time.Delta;
		if ( _timer >= Lifetime )
		{
			GameObject.Destroy();
			return;
		}

		// Rise upwards
		WorldPosition += Vector3.Up * Speed * Time.Delta;

		// Face the camera (billboard effect)
		if ( Scene.Camera != null )
		{
			var dir = ( WorldPosition - Scene.Camera.WorldPosition ).Normal;
			WorldRotation = Rotation.LookAt( dir );
		}

		// Fade out alpha
		if ( _renderer != null )
		{
			float alpha = 1.0f - ( _timer / Lifetime );
			_renderer.Color = TextColor.WithAlpha( alpha );
		}
	}
}
