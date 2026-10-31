using System;
using System.Collections.Generic;
using System.Linq;

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

public sealed class TimedDamageStack<TSource>
{
	public DamageEffectSettings Settings { get; }
	public TSource Source { get; }
	public float ExpiresAt { get; }
	internal float LastDamageTime { get; set; }

	internal TimedDamageStack(DamageEffectSettings settings, TSource source, float now)
	{
		Settings = settings;
		Source = source;
		LastDamageTime = now;
		ExpiresAt = now + settings.Duration;
	}
}

public sealed class TimedDamageEffects<TSource>
{
	private readonly List<TimedDamageStack<TSource>> _stacks = new();
	public IReadOnlyList<TimedDamageStack<TSource>> Stacks => _stacks;

	public int Count(DamageType type, float now) =>
		_stacks.Count(x => x.Settings.Type == type && x.ExpiresAt > now);

	public bool CanSpreadTo(DamageEffectSettings settings, float now) =>
		Count(settings.Type, now) < settings.MaxStacks;

	public void Apply(DamageEffectSettings settings, TSource source, float now)
	{
		if (settings == null || !float.IsFinite(now))
			throw new ArgumentException("Applying an effect requires settings and a finite time.");

		_stacks.RemoveAll(x => x.ExpiresAt <= now);
		var sameType = _stacks.Where(x => x.Settings.Type == settings.Type).ToList();
		// A lower-cap source cannot remove another player's stacks, but can renew the oldest.
		if (sameType.Count >= settings.MaxStacks)
			_stacks.Remove(sameType[0]);
		_stacks.Add(new TimedDamageStack<TSource>(settings, source, now));
	}

	public void Advance(float now, Action<TimedDamageStack<TSource>, float> dealDamage, bool flush = false)
	{
		if (!float.IsFinite(now))
			throw new ArgumentOutOfRangeException(nameof(now));

		foreach (var stack in _stacks.ToArray())
		{
			var end = MathF.Min(now, stack.ExpiresAt);
			var elapsed = MathF.Max(0f, end - stack.LastDamageTime);
			// Whole-second ticks avoid continuous hit reactions; expiry/reapplication pays the remaining fraction.
			if (!flush && end < stack.ExpiresAt)
				elapsed = MathF.Floor(elapsed);
			stack.LastDamageTime += elapsed;
			if (elapsed > 0f)
				dealDamage(stack, elapsed * stack.Settings.DamagePerSecond);
		}
		_stacks.RemoveAll(x => x.ExpiresAt <= now);
	}

	public void Clear() => _stacks.Clear();
}
