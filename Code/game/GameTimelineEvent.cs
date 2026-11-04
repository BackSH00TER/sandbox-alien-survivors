using System;

public readonly record struct GameTimelineEvent(
	float Seconds,
	string Icon,
	string Label
)
{
	public const float ViewRadiusSeconds = 60f;

	public float PositionPercent(float elapsedSeconds)
	{
		return 50f + (Seconds - elapsedSeconds) / ViewRadiusSeconds * 50f;
	}

	public bool IsInView(float elapsedSeconds)
	{
		return MathF.Abs(Seconds - elapsedSeconds) <= ViewRadiusSeconds;
	}

	public bool IsPast(float elapsedSeconds)
	{
		return Seconds < elapsedSeconds;
	}

	public int MinutesUntilView(float elapsedSeconds)
	{
		var secondsUntilView = Seconds - elapsedSeconds - ViewRadiusSeconds;
		return Math.Max(0, (int)MathF.Ceiling(secondsUntilView / 60f));
	}
}
