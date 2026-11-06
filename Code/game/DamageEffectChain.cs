using System;
using System.Collections.Generic;

/// <summary>Runs a bounded instant chain; a target can be hit only once per application.</summary>
public static class DamageEffectChain
{
	public static void Apply<TTarget>(
		TTarget initialTarget,
		DamageEffectSettings settings,
		Func<TTarget, bool> canHit,
		Func<TTarget, IReadOnlySet<TTarget>, TTarget> findNext,
		Action<TTarget> hit,
		Func<float> random
	) where TTarget : class
	{
		ArgumentNullException.ThrowIfNull(initialTarget);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(canHit);
		ArgumentNullException.ThrowIfNull(findNext);
		ArgumentNullException.ThrowIfNull(hit);
		ArgumentNullException.ThrowIfNull(random);
		if (!settings.IsInstant)
		{
			throw new ArgumentException("Chains require an instant damage effect.", nameof(settings));
		}

		var visited = new HashSet<TTarget>();
		var current = initialTarget;
		int remainingChains = settings.MaxChains;
		while (current != null && !visited.Contains(current) && canHit(current))
		{
			visited.Add(current);
			TTarget next = null;
			if (remainingChains > 0 && settings.ChainChance > 0f)
			{
				var continueChain = settings.ChainChance >= 1f;
				if (!continueChain)
				{
					var roll = random();
					if (!float.IsFinite(roll) || roll < 0f || roll > 1f)
					{
						throw new ArgumentOutOfRangeException(nameof(random), "Chain rolls must be between zero and one.");
					}

					continueChain = roll < settings.ChainChance;
				}

				// Select before damage can destroy the origin or create an obstructing ragdoll.
				if (continueChain)
				{
					next = findNext(current, visited);
				}

				remainingChains--;
			}

			hit(current);
			current = next;
		}
	}
}
