using System;

public static class GameFogDrift
{
	public const float HalfWidth = 1600f;
	public const float EdgePadding = 600f;
	public const float Speed = 28f;
	public const int LayerCount = 4;

	public static float LayerHeightAt(int layer, float elapsedSeconds)
	{
		if (layer < 0 || layer >= LayerCount)
		{
			throw new ArgumentOutOfRangeException(nameof(layer));
		}

		if (!float.IsFinite(elapsedSeconds))
		{
			throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Fog age must be finite.");
		}

		var roll = (float)Math.Sin((double)elapsedSeconds * 0.14d + layer * 1.6d) * 8f;
		return 45f + layer * 55f + roll;
	}

	public static Vector2 LayerWindAt(int layer)
	{
		if (layer < 0 || layer >= LayerCount)
		{
			throw new ArgumentOutOfRangeException(nameof(layer));
		}

		return new Vector2(18f + layer * 7f, 5f - layer * 3f);
	}

	public static float PositionAt(
		float initialPosition,
		float elapsedSeconds,
		float halfWidth = HalfWidth
	)
	{
		if (!float.IsFinite(initialPosition))
		{
			throw new ArgumentOutOfRangeException(nameof(initialPosition), "Fog position must be finite.");
		}

		if (!float.IsFinite(elapsedSeconds))
		{
			throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Fog age must be finite.");
		}

		if (!float.IsFinite(halfWidth) || halfWidth <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(halfWidth), "Fog region half-width must be finite and positive.");
		}

		var width = (double)halfWidth * 2d;
		var offset = (double)initialPosition + halfWidth + (double)elapsedSeconds * Speed;
		var position = offset % width;
		if (position < 0f)
		{
			position += width;
		}

		var wrappedPosition = (float)(position - halfWidth);
		if (wrappedPosition >= halfWidth)
		{
			return -halfWidth;
		}

		return wrappedPosition;
	}
}
