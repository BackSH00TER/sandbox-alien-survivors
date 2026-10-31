using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Validated, immutable effect values captured when a projectile fires and reused when its effect spreads.</summary>
public sealed class DamageEffectSettings
{
	public DamageType Type { get; }
	public float ApplicationChance { get; }
	public float Duration { get; }
	public float DamagePerSecond { get; }
	public int MaxStacks { get; }
	public float SpreadChance { get; }
	public float SpreadRadius { get; }

	public DamageEffectSettings(DamageType type, float applicationChance, float duration,
		float damagePerSecond, int maxStacks, float spreadChance, float spreadRadius)
	{
		if (!Enum.IsDefined(type) || type == DamageType.Physical
			|| !float.IsFinite(applicationChance) || applicationChance < 0f || applicationChance > 1f
			|| !float.IsFinite(duration) || duration <= 0f
			|| !float.IsFinite(damagePerSecond) || damagePerSecond <= 0f || maxStacks < 1
			|| !float.IsFinite(spreadChance) || spreadChance < 0f || spreadChance > 1f
			|| !float.IsFinite(spreadRadius) || spreadRadius <= 0f)
			throw new ArgumentOutOfRangeException(nameof(type), "Invalid timed damage effect settings.");

		Type = type;
		ApplicationChance = applicationChance;
		Duration = duration;
		DamagePerSecond = damagePerSecond;
		MaxStacks = maxStacks;
		SpreadChance = spreadChance;
		SpreadRadius = spreadRadius;
	}
}

/// <summary>One application with its own expiry and damage source, independent of other applications of the same type.</summary>
public sealed class TimedDamageStack<TSource>
{
	public DamageEffectSettings Settings { get; }
	public TSource Source { get; }
	public float ExpiresAt { get; }
	// Advances only for time already paid out as damage, leaving partial tick time for the next update.
	internal float LastDamageTime { get; set; }

	internal TimedDamageStack(DamageEffectSettings settings, TSource source, float now)
	{
		Settings = settings;
		Source = source;
		LastDamageTime = now;
		ExpiresAt = now + settings.Duration;
	}
}

/// <summary>
/// Tracks one target's timed applications, stack caps and damage ticks without depending on the scene or networking.
/// The caller supplies the clock and damage callback, and clears this state when the target dies or the round ends.
/// </summary>
public sealed class TimedDamageEffects<TSource>
{
	private readonly List<TimedDamageStack<TSource>> _stacks = new();
	public IReadOnlyList<TimedDamageStack<TSource>> Stacks => _stacks;

	public int Count(DamageType type, float now) =>
		_stacks.Count(effectStack => effectStack.Settings.Type == type && effectStack.ExpiresAt > now);

	/// <summary>Spread may fill an available stack slot, but unlike a direct hit it cannot replace a full stack.</summary>
	public bool CanSpreadTo(DamageEffectSettings settings, float now) =>
		Count(settings.Type, now) < settings.MaxStacks;

	/// <summary>Adds an independent application, renewing the oldest of the same type when the incoming cap is full.</summary>
	/// <remarks>Advance with flush enabled first so retiring applications do not lose their pending partial damage.</remarks>
	public void Apply(DamageEffectSettings settings, TSource source, float now)
	{
		if (settings == null || !float.IsFinite(now))
			throw new ArgumentException("Applying an effect requires settings and a finite time.");

		_stacks.RemoveAll(effectStack => effectStack.ExpiresAt <= now);
		var sameTypeStacks = _stacks.Where(effectStack => effectStack.Settings.Type == settings.Type).ToList();
		// A lower-cap source cannot remove another player's stacks, but can renew the oldest.
		if (sameTypeStacks.Count >= settings.MaxStacks)
			_stacks.Remove(sameTypeStacks[0]);
		_stacks.Add(new TimedDamageStack<TSource>(settings, source, now));
	}

	/// <summary>Pays whole-second ticks, plus final partial ticks on expiry or when flush is enabled.</summary>
	/// <remarks>Flushing is used before reapplication and during immunity; the caller can discard damage while immune.</remarks>
	public void Advance(float now, Action<TimedDamageStack<TSource>, float> dealDamage, bool flush = false)
	{
		if (!float.IsFinite(now))
			throw new ArgumentOutOfRangeException(nameof(now));

		// The damage callback may kill the target and clear the collection while it is being processed.
		foreach (var effectStack in _stacks.ToArray())
		{
			var damageEndTime = MathF.Min(now, effectStack.ExpiresAt);
			var elapsedDamageTime = MathF.Max(0f, damageEndTime - effectStack.LastDamageTime);
			// Whole-second ticks avoid continuous hit reactions; expiry/reapplication pays the remaining fraction.
			if (!flush && damageEndTime < effectStack.ExpiresAt)
				elapsedDamageTime = MathF.Floor(elapsedDamageTime);
			effectStack.LastDamageTime += elapsedDamageTime;
			if (elapsedDamageTime > 0f)
				dealDamage(effectStack, elapsedDamageTime * effectStack.Settings.DamagePerSecond);
		}
		_stacks.RemoveAll(effectStack => effectStack.ExpiresAt <= now);
	}

	public void Clear() => _stacks.Clear();
}
