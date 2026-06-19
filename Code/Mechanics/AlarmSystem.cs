using Sandbox;
using System;

[Icon( "security" )]
public sealed class AlarmSystem : Component
{
	[Property] public float AlarmDecayRate { get; set; } = 5f;
	[Property] public float DetectionRange { get; set; } = 300f;
	[Property] public float PocketNoiseRange { get; set; } = 200f;
	[Property] public float WallPenetrationMultiplier { get; set; } = 0.5f;

	[Sync] public int CurrentAlarmLevel { get; set; }
	[Sync] public float AlarmProgress { get; set; }

	public string GetAlarmLevelName()
	{
		return CurrentAlarmLevel switch
		{
			0 => "None",
			1 => "Yellow",
			2 => "Orange",
			3 => "Red",
			4 => "Black",
			_ => "None"
		};
	}

	public void TriggerAlarm( float amount, Vector3 origin )
	{
		AlarmProgress = Math.Min( AlarmProgress + amount, 100f );

		int newLevel = AlarmProgress switch
		{
			>= 100f => 4,
			>= 75f => 3,
			>= 50f => 2,
			>= 25f => 1,
			_ => 0
		};

		if ( newLevel != CurrentAlarmLevel )
		{
			CurrentAlarmLevel = newLevel;
			Log.Info( $"Alarm level increased to {GetAlarmLevelName()}" );
		}
	}

	public void OnPocketNoise( Vector3 playerPos, float wallThickness = 0f )
	{
		float noise = 10f;
		if ( wallThickness > 0 )
			noise *= WallPenetrationMultiplier;

		TriggerAlarm( noise, playerPos );
	}

	public void ResetAlarm()
	{
		AlarmProgress = 0;
		CurrentAlarmLevel = 0;
	}
}
