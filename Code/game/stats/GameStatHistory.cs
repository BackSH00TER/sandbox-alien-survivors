using System;
using System.Collections.Generic;
using System.Linq;

public readonly record struct GameStatBucket(
	int Index,
	string Key,
	float Amount
);

public readonly record struct GameStatMoment(
	float Seconds,
	string Kind,
	string Label,
	string Icon = ""
);

public readonly record struct GameStatPoint(float Seconds, float Value);

/// <summary>Host-local, sparse bucket deltas. Combat events are never retained individually.</summary>
public sealed class GameStatHistory
{
	public const int BucketSeconds = 15;
	private readonly Dictionary<(int Index, string Key), float> _buckets = new();

	public int Count => _buckets.Count;

	public void Add(
		string key,
		float amount,
		float seconds
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		if (!float.IsFinite(amount) || amount <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(amount));
		}

		ValidateSeconds(seconds);
		var index = (int)MathF.Floor(seconds / BucketSeconds);
		var bucketKey = (index, key);
		_buckets.TryGetValue(bucketKey, out var previous);
		_buckets[bucketKey] = previous + amount;
	}

	public List<GameStatBucket> Snapshot()
	{
		return _buckets
			.OrderBy(pair => pair.Key.Index)
			.ThenBy(pair => pair.Key.Key)
			.Select(pair => new GameStatBucket(
				Index: pair.Key.Index,
				Key: pair.Key.Key,
				Amount: pair.Value
			))
			.ToList();
	}

	/// <summary>Carry totals through quiet buckets and include the final partial bucket.</summary>
	public static List<GameStatPoint> BuildSeries(
		IEnumerable<GameStatBucket> buckets,
		string key,
		float duration
	)
	{
		ValidateSeconds(duration);
		var deltas = buckets.Where(bucket => bucket.Key == key)
			.GroupBy(bucket => bucket.Index)
			.ToDictionary(group => group.Key, group => group.Sum(bucket => bucket.Amount));
		var points = new List<GameStatPoint> { new(0f, 0f) };
		var lastIndex = (int)MathF.Floor(duration / BucketSeconds);
		var total = 0f;
		for (var index = 0; index <= lastIndex; index++)
		{
			total += deltas.GetValueOrDefault(index);
			var seconds = MathF.Min((index + 1f) * BucketSeconds, duration);
			var point = new GameStatPoint(seconds, total);
			if (points[^1].Seconds == seconds)
			{
				points[^1] = point;
			}
			else
			{
				points.Add(point);
			}
		}

		return points;
	}

	private static void ValidateSeconds(float seconds)
	{
		if (!float.IsFinite(seconds) || seconds < 0f || seconds / BucketSeconds >= int.MaxValue)
		{
			throw new ArgumentOutOfRangeException(nameof(seconds));
		}
	}
}
