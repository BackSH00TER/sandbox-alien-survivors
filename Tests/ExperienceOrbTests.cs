using System;

internal static class ExperienceOrbTests
{
	public static void Reach()
	{
		var cases = new (int Amount, int OtherAmount, float DistanceSquared, bool Expected)[]
		{
			(1, 1, 0f, true),
			(1, 99, 24f * 24f, true),
			(1, 1, 24.01f * 24.01f, true),
			(1, 1, 40f * 40f, true),
			(1, 99, 48f * 48f, true),
			(1, 1, 48.01f * 48.01f, true),
			(1, 1, 60f * 60f, true),
			(1, 99, 64f * 64f, true),
			(1, 1, 64.01f * 64.01f, false),
			(1000000, 1000000, 65f * 65f, false),
			(int.MaxValue - 1, 1, 1f, true),
			(int.MaxValue, 1, 1f, false),
			(0, 1, 1f, false),
			(1, -1, 1f, false),
			(1, 1, float.NaN, false),
			(1, 1, -1f, false)
		};
		foreach (var test in cases)
		{
			var actual = ExperienceOrbRules.CanMerge(
				amount: test.Amount,
				otherAmount: test.OtherAmount,
				distanceSquared: test.DistanceSquared
			);
			Check(actual == test.Expected, $"Unexpected merge eligibility for {test}.");
		}
	}

	public static void Appearance()
	{
		var minimums = new[] { 1, 4, 8, 11, 16, 21, 31, 51 };
		var maximums = new[] { 3, 7, 10, 15, 20, 30, 50, int.MaxValue };
		var rangedDropColor = ExperienceOrbRules.GetColor(4);
		var blue = new Color(
			r: 0.25f,
			g: 0.5f,
			b: 1f
		);
		Check(rangedDropColor == blue, "A four-XP ranged enemy drop must be blue without merging.");
		for (var i = 0; i < minimums.Length; i++)
		{
			var color = ExperienceOrbRules.GetColor(minimums[i]);
			var upperColor = ExperienceOrbRules.GetColor(maximums[i]);
			Check(color == upperColor, "Each XP tier must retain its color.");
			for (var j = 0; j < i; j++)
			{
				var otherColor = ExperienceOrbRules.GetColor(minimums[j]);
				Check(color != otherColor, "All eight XP tiers must have distinct colors.");
			}
		}

		var singleScale = ExperienceOrbRules.GetScale(1);
		var mergedScale = ExperienceOrbRules.GetScale(2);
		Check(singleScale == 0.12f, "Single XP must preserve the original size.");
		Check(mergedScale > singleScale, "Even a 1+1 merge must grow.");
		var previous = 0f;
		for (var amount = 1; amount <= 10000; amount++)
		{
			var scale = ExperienceOrbRules.GetScale(amount);
			Check(scale >= previous && scale <= 0.21f, "Size growth must be monotonic and capped.");
			previous = scale;
		}
		var maximumScale = ExperienceOrbRules.GetScale(int.MaxValue);
		Check(maximumScale == 0.21f, "Boss-sized drops must retain the size cap.");
	}

	public static void Invalid()
	{
		foreach (var amount in new[] { 0, -1, int.MinValue })
		{
			ExpectInvalid(() => ExperienceOrbRules.GetColor(amount));
			ExpectInvalid(() => ExperienceOrbRules.GetScale(amount));
		}
	}

	private static void ExpectInvalid(Action action)
	{
		try
		{
			action();
		}
		catch (ArgumentOutOfRangeException)
		{
			return;
		}
		throw new InvalidOperationException("Invalid XP must be rejected explicitly.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}
}
