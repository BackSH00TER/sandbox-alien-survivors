using System;
using System.Linq;

public static class StatHistoryTests
{
	public static void Buckets()
	{
		var history = new GameStatHistory();
		history.Add(
			key: GameStatKeys.Kills,
			amount: 1f,
			seconds: 14.999f
		);
		history.Add(
			key: GameStatKeys.Kills,
			amount: 2f,
			seconds: 15f
		);
		history.Add(
			key: GameStatKeys.DamageDealt,
			amount: 50f,
			seconds: 15f
		);
		history.Add(
			key: GameStatKeys.Kills,
			amount: 3f,
			seconds: 0f
		);
		var snapshot = history.Snapshot();
		var firstBucket = snapshot.Single(bucket => bucket.Index == 0);
		var secondBucketKills = snapshot.Single(bucket => bucket.Index == 1 && bucket.Key == GameStatKeys.Kills);
		Check(GameStatHistory.BucketSeconds == 15, "Graph resolution must be 15 seconds.");
		Check(snapshot.Count == 3, "Only one entry per stat and bucket.");
		Check(firstBucket.Amount == 4f, "Events inside the bucket must accumulate, even out of order.");
		Check(secondBucketKills.Amount == 2f, "Exactly 15 seconds belongs to the next bucket.");
	}

	public static void Series()
	{
		var history = new GameStatHistory();
		history.Add(
			key: GameStatKeys.Kills,
			amount: 4f,
			seconds: 2f
		);
		history.Add(
			key: GameStatKeys.Kills,
			amount: 3f,
			seconds: 50f
		);
		var snapshot = history.Snapshot();
		var points = GameStatHistory.BuildSeries(
			buckets: snapshot,
			key: GameStatKeys.Kills,
			duration: 55f
		);
		Check(points.Select(point => point.Seconds).SequenceEqual(new[] { 0f, 15f, 30f, 45f, 55f }), "Series must cover the entire round, including quiet buckets.");
		Check(points.Select(point => point.Value).SequenceEqual(new[] { 0f, 4f, 4f, 4f, 7f }), "Series must carry cumulative totals, not interpolate across missing buckets.");
		var empty = GameStatHistory.BuildSeries(
			buckets: snapshot,
			key: GameStatKeys.DamageTaken,
			duration: 55f
		);
		Check(empty.Count == points.Count && empty.All(point => point.Value == 0f), "Unrecorded stats must render zero throughout the round.");
	}

	public static void Boundaries()
	{
		var history = new GameStatHistory();
		history.Add(
			key: GameStatKeys.Kills,
			amount: 1f,
			seconds: 0f
		);
		var snapshot = history.Snapshot();
		history.Add(
			key: GameStatKeys.Kills,
			amount: 2f,
			seconds: 15f
		);
		Check(snapshot.Count == 1, "Snapshots must not change after further recording.");
		var finalSnapshot = history.Snapshot();
		var exact = GameStatHistory.BuildSeries(
			buckets: finalSnapshot,
			key: GameStatKeys.Kills,
			duration: 15f
		);
		Check(exact.Count == 2 && exact[^1] == new GameStatPoint(15f, 3f), "An event exactly at round end must be included without duplicate timestamps.");
		var zero = GameStatHistory.BuildSeries(
			buckets: snapshot,
			key: GameStatKeys.Kills,
			duration: 0f
		);
		Check(zero.Count == 1 && zero[0] == new GameStatPoint(0f, 1f), "Instant rounds must preserve their final totals.");
		var partial = GameStatHistory.BuildSeries(
			buckets: snapshot,
			key: GameStatKeys.Kills,
			duration: 12f
		);
		Check(partial.Count == 2 && partial[^1] == new GameStatPoint(12f, 1f), "Sub-minute rounds must have a start and final endpoint.");
	}

	public static void Volume()
	{
		var history = new GameStatHistory();
		for (var index = 0; index < 1_000_000; index++)
		{
			history.Add(
				key: GameStatKeys.DamageDealt,
				amount: 1f,
				seconds: index % 600
			);
		}

		Check(history.Count == 40, "A million events across ten minutes must retain exactly forty aggregates.");
		var snapshot = history.Snapshot();
		var points = GameStatHistory.BuildSeries(
			buckets: snapshot,
			key: GameStatKeys.DamageDealt,
			duration: 600f
		);
		Check(points.Count == 41 && points[^1].Value == 1_000_000f, "Rendered point count must depend on duration, not event count; no totals may be lost.");
	}

	public static void Levels()
	{
		var levels = new[]
		{
			new GameStatPoint(0f, 1f),
			new GameStatPoint(3.25f, 2f),
			new GameStatPoint(3.25f, 3f),
			new GameStatPoint(18.5f, 4f)
		};
		var points = GameStatHistory.BuildLevelSeries(levels, 20f);
		var expected = new[]
		{
			new GameStatPoint(0f, 1f),
			new GameStatPoint(3.25f, 1f),
			new GameStatPoint(3.25f, 2f),
			new GameStatPoint(3.25f, 2f),
			new GameStatPoint(3.25f, 3f),
			new GameStatPoint(18.5f, 3f),
			new GameStatPoint(18.5f, 4f),
			new GameStatPoint(20f, 4f)
		};
		Check(points.SequenceEqual(expected), "Levels must step at exact acquisition times, including multiple levels at the same time.");
		var empty = GameStatHistory.BuildLevelSeries(Array.Empty<GameStatPoint>(), 20f);
		Check(empty.SequenceEqual(new[] { new GameStatPoint(0f, 1f), new GameStatPoint(20f, 1f) }), "Players without level-ups start and finish at level one.");
		var instant = GameStatHistory.BuildLevelSeries(Array.Empty<GameStatPoint>(), 0f);
		Check(instant.SequenceEqual(new[] { new GameStatPoint(0f, 1f) }), "Instant rounds must retain starting level.");
		var exact = GameStatHistory.BuildLevelSeries(levels, 18.5f);
		Check(exact[^1] == new GameStatPoint(18.5f, 4f), "A level-up at round end must be included.");
		Reject(() => GameStatHistory.BuildLevelSeries(levels, 18f));
		Reject(() => GameStatHistory.BuildLevelSeries(levels.Reverse(), 20f));
		Reject(() => GameStatHistory.BuildLevelSeries(new[] { new GameStatPoint(1f, 0f) }, 20f));
		Reject(() => GameStatHistory.BuildLevelSeries(new[] { new GameStatPoint(1f, float.NaN) }, 20f));
	}

	public static void Breakdowns()
	{
		var history = new GameStatHistory();
		var enemyKeys = System.Enum.GetValues<GameEnemyKind>().Select(GameStatKeys.KillsByEnemy).ToList();
		for (var index = 0; index < enemyKeys.Count; index++)
		{
			history.Add(
				key: GameStatKeys.Kills,
				amount: index + 1f,
				seconds: index * 15f
			);
			history.Add(
				key: enemyKeys[index],
				amount: index + 1f,
				seconds: index * 15f
			);
		}

		var duration = enemyKeys.Count * 15f + 3f;
		var buckets = history.Snapshot();
		var totals = GameStatHistory.BuildSeries(
			buckets: buckets,
			key: GameStatKeys.Kills,
			duration: duration
		);
		var breakdowns = enemyKeys.Select(key => GameStatHistory.BuildSeries(
			buckets: buckets,
			key: key,
			duration: duration
		)).ToList();
		for (var index = 0; index < totals.Count; index++)
		{
			var total = totals[index];
			Check(breakdowns.All(points => points[index].Seconds == total.Seconds), "Category snapshots must align with the total line.");
			Check(breakdowns.Sum(points => points[index].Value) == total.Value, "Cumulative category stacks must add up to the cumulative total at every bucket endpoint.");
		}
	}

	public static void Invalid()
	{
		var history = new GameStatHistory();
		var snapshot = history.Snapshot();
		foreach (var invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
		{
			Reject(() => history.Add(
				key: GameStatKeys.Kills,
				amount: 1f,
				seconds: invalid
			));
			Reject(() => GameStatHistory.BuildSeries(
				buckets: snapshot,
				key: GameStatKeys.Kills,
				duration: invalid
			));
		}

		foreach (var invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
		{
			Reject(() => history.Add(
				key: GameStatKeys.Kills,
				amount: invalid,
				seconds: 0f
			));
		}

		Reject(() => history.Add(
			key: "",
			amount: 1f,
			seconds: 0f
		));
		Check(history.Count == 0, "Invalid events must not mutate history.");
	}

	private static void Reject(Action action)
	{
		try
		{
			action();
		}
		catch (ArgumentException)
		{
			return;
		}

		throw new InvalidOperationException("Expected invalid history input to throw.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}
}
