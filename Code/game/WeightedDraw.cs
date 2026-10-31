using System;
using System.Collections.Generic;

public static class WeightedDraw
{
	public static T Pick<T>(IReadOnlyList<T> options, Func<T, float> weight, float roll)
	{
		if (options.Count == 0 || !float.IsFinite(roll) || roll < 0f || roll > 1f)
			throw new ArgumentOutOfRangeException(nameof(roll), "A draw needs options and a roll between zero and one.");

		float total = 0f;
		foreach (var option in options)
		{
			var value = weight(option);
			if (!float.IsFinite(value) || value <= 0f)
				throw new ArgumentOutOfRangeException(nameof(weight), "Draw weights must be finite and positive.");
			total += value;
		}
		if (!float.IsFinite(total))
			throw new ArgumentOutOfRangeException(nameof(weight), "Total draw weight must be finite.");

		var remaining = roll * total;
		foreach (var option in options)
		{
			remaining -= weight(option);
			if (remaining < 0f)
				return option;
		}
		return options[options.Count - 1];
	}
}
