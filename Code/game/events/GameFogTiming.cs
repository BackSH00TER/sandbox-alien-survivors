using System;

public static class GameFogTiming
{
	public const float DurationSeconds = 60f;
	public const float FadeSeconds = 20f;

	public static float StrengthAt(float elapsedSeconds)
	{
		if (!float.IsFinite(elapsedSeconds))
		{
			throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Fog age must be finite.");
		}

		if (elapsedSeconds <= 0f || elapsedSeconds >= DurationSeconds)
		{
			return 0f;
		}

		var fadeIn = elapsedSeconds / FadeSeconds;
		var fadeOut = (DurationSeconds - elapsedSeconds) / FadeSeconds;
		var fadingStrength = MathF.Min(fadeIn, fadeOut);
		return MathF.Min(1f, fadingStrength);
	}
}
