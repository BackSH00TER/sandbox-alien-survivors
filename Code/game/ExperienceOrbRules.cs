using Sandbox;
using System;

/// <summary>Shared by merged orbs and direct high-XP drops, including future boss rewards.</summary>
internal static class ExperienceOrbRules
{
	/// <summary>Fixed world-unit reach for every XP tier; larger orbs never attract from farther away.</summary>
	internal const float MergeDistance = 64f;
	/// <summary>Seconds a reserved pair spends moving toward its midpoint before combining XP.</summary>
	internal const float MergeSeconds = 0.2f;
	/// <summary>Seconds between neighbor searches; active merge animations still advance every fixed tick.</summary>
	internal const float ScanSeconds = 0.25f;

	internal static bool CanMerge(
		int amount,
		int otherAmount,
		float distanceSquared
	)
	{
		return amount > 0
			&& otherAmount > 0
			&& amount <= int.MaxValue - otherAmount
			&& distanceSquared >= 0f
			&& distanceSquared <= MergeDistance * MergeDistance;
	}

	/// <summary>Green 1-3, blue 4-7, cyan 8-10, purple 11-15, pink 16-20, orange 21-30, gold 31-50, red 51+.</summary>
	internal static Color GetColor(int amount)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
		return amount switch
		{
			<= 3 => new Color(
				r: 0.3f,
				g: 1f,
				b: 0.55f
			),
			<= 7 => new Color(
				r: 0.25f,
				g: 0.5f,
				b: 1f
			),
			<= 10 => new Color(
				r: 0.2f,
				g: 1f,
				b: 1f
			),
			<= 15 => new Color(
				r: 0.75f,
				g: 0.3f,
				b: 1f
			),
			<= 20 => new Color(
				r: 1f,
				g: 0.3f,
				b: 0.7f
			),
			<= 30 => new Color(
				r: 1f,
				g: 0.45f,
				b: 0.15f
			),
			<= 50 => new Color(
				r: 1f,
				g: 0.85f,
				b: 0.2f
			),
			_ => new Color(
				r: 1f,
				g: 0.15f,
				b: 0.15f
			)
		};
	}

	internal static float GetScale(int amount)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
		var growth = MathF.Log2(amount) * 0.012f;
		return MathF.Min(0.12f + growth, 0.21f);
	}
}
