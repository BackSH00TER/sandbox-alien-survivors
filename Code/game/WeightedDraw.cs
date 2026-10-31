using System;
using System.Collections.Generic;

/// <summary>
/// Chooses an option with probability proportional to its positive weight (weight 3 is three times as likely as 1).
/// A higher drawWeight means a higher chance of selection, relative to the other eligible options.
/// </summary>
public static class WeightedDraw
{
	public static T Pick<T>(IReadOnlyList<T> options, Func<T, float> weight, float roll)
	{
		if (options.Count == 0 || !float.IsFinite(roll) || roll < 0f || roll > 1f)
			throw new ArgumentOutOfRangeException(nameof(roll), "A draw needs options and a roll between zero and one.");

		float totalWeight = 0f;
		foreach (var option in options)
		{
			var optionWeight = weight(option);
			if (!float.IsFinite(optionWeight) || optionWeight <= 0f)
				throw new ArgumentOutOfRangeException(nameof(weight), "Draw weights must be finite and positive.");
			totalWeight += optionWeight;
		}
		if (!float.IsFinite(totalWeight))
			throw new ArgumentOutOfRangeException(nameof(weight), "Total draw weight must be finite.");

		// Map the roll onto the total weight, then locate the option whose weighted interval contains it.
		var remainingWeight = roll * totalWeight;
		foreach (var option in options)
		{
			remainingWeight -= weight(option);
			if (remainingWeight < 0f)
				return option;
		}
		// The RNG may return exactly 1, so include that endpoint in the last interval.
		return options[options.Count - 1];
	}
}
