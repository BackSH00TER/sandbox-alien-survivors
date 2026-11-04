using System;

public readonly record struct GameTimelineEvent(
	float Seconds,
	string Icon,
	string Label
)
{
	public const float ViewDurationSeconds = 120f;
	public const float NowPositionPercent = 15f;
	public const float PastSeconds = ViewDurationSeconds * NowPositionPercent / 100f;
	public const float FutureSeconds = ViewDurationSeconds - PastSeconds;

	public float PositionPercent(float elapsedSeconds)
	{
		return NowPositionPercent + (Seconds - elapsedSeconds) / ViewDurationSeconds * 100f;
	}

	public bool IsInView(float elapsedSeconds)
	{
		var relativeSeconds = Seconds - elapsedSeconds;
		return relativeSeconds >= -PastSeconds && relativeSeconds <= FutureSeconds;
	}

	public bool IsPast(float elapsedSeconds)
	{
		return Seconds < elapsedSeconds;
	}

	public int MinutesUntilView(float elapsedSeconds)
	{
		var secondsUntilView = Seconds - elapsedSeconds - FutureSeconds;
		return Math.Max(0, (int)MathF.Ceiling(secondsUntilView / 60f));
	}
}
