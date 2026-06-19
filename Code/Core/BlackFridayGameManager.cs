using Sandbox;
using System;

public enum GamePhase
{
	Morning,
	Day,
	Evening,
	Night
}

public sealed class BlackFridayGameManager : Component
{
	[Property] public float MorningDuration { get; set; } = 60f;
	[Property] public float DayDuration { get; set; } = 180f;
	[Property] public float EveningDuration { get; set; } = 60f;
	[Property] public float NightDuration { get; set; } = 60f;

	[Sync] public GamePhase CurrentPhase { get; set; } = GamePhase.Morning;
	[Sync] public int CurrentDay { get; set; } = 1;
	[Sync] public float PhaseTimeRemaining { get; set; }

	protected override void OnStart()
	{
		if ( IsProxy ) return;
		StartPhase( GamePhase.Morning );
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;

		PhaseTimeRemaining -= Time.Delta;

		if ( PhaseTimeRemaining <= 0 )
		{
			AdvancePhase();
		}
	}

	private void StartPhase( GamePhase phase )
	{
		CurrentPhase = phase;
		PhaseTimeRemaining = phase switch
		{
			GamePhase.Morning => MorningDuration,
			GamePhase.Day => DayDuration,
			GamePhase.Evening => EveningDuration,
			GamePhase.Night => NightDuration,
			_ => 60f
		};

		Log.Info( $"Day {CurrentDay} — Phase: {phase}" );
	}

	private void AdvancePhase()
	{
		var nextPhase = CurrentPhase switch
		{
			GamePhase.Morning => GamePhase.Day,
			GamePhase.Day => GamePhase.Evening,
			GamePhase.Evening => GamePhase.Night,
			GamePhase.Night => GamePhase.Morning,
			_ => GamePhase.Morning
		};

		if ( nextPhase == GamePhase.Morning )
		{
			CurrentDay++;
		}

		StartPhase( nextPhase );
	}

	public float GetPhaseProgress()
	{
		float total = CurrentPhase switch
		{
			GamePhase.Morning => MorningDuration,
			GamePhase.Day => DayDuration,
			GamePhase.Evening => EveningDuration,
			GamePhase.Night => NightDuration,
			_ => 1f
		};

		return 1f - (PhaseTimeRemaining / total);
	}
}
