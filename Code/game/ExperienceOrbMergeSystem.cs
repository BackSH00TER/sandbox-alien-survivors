using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Host-only, scene-local pairing. A fixed spatial grid keeps merging local even as orb sizes grow.</summary>
public sealed class ExperienceOrbMergeSystem : GameObjectSystem
{
	private sealed class Merge
	{
		internal GameExperienceOrb First;
		internal GameExperienceOrb Second;
		internal Vector3 FirstStart;
		internal Vector3 SecondStart;
		internal Vector3 Midpoint;
		internal float Elapsed;
	}

	private readonly List<Merge> _merges = new();
	private readonly Dictionary<(int X, int Y, int Z), List<GameExperienceOrb>> _cells = new();
	private float _untilScan;

	public ExperienceOrbMergeSystem(Scene scene) : base(scene)
	{
		Listen(
			stage: Stage.StartFixedUpdate,
			order: -1,
			function: Tick,
			debugName: nameof(ExperienceOrbMergeSystem)
		);
	}

	private void Tick()
	{
		if (!Networking.IsHost)
		{
			return;
		}

		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (manager != null && manager.State != GameState.InProgress)
		{
			return;
		}

		AdvanceMerges();
		_untilScan -= Time.Delta;
		if (_untilScan > 0f)
		{
			return;
		}

		_untilScan = ExperienceOrbRules.ScanSeconds;
		FindPairs();
	}

	private void AdvanceMerges()
	{
		for (var i = _merges.Count - 1; i >= 0; i--)
		{
			var merge = _merges[i];
			if (!Available(merge.First) || !Available(merge.Second))
			{
				if (merge.First.IsValid())
				{
					merge.First.IsMerging = false;
				}
				if (merge.Second.IsValid())
				{
					merge.Second.IsMerging = false;
				}
				_merges.RemoveAt(i);
				continue;
			}

			merge.Elapsed += Time.Delta;
			var fraction = MathF.Min(merge.Elapsed / ExperienceOrbRules.MergeSeconds, 1f);
			var eased = fraction * fraction * (3f - 2f * fraction);
			merge.First.WorldPosition = merge.FirstStart + (merge.Midpoint - merge.FirstStart) * eased;
			merge.Second.WorldPosition = merge.SecondStart + (merge.Midpoint - merge.SecondStart) * eased;
			if (fraction < 1f)
			{
				continue;
			}

			merge.First.Amount = checked(merge.First.Amount + merge.Second.Amount);
			merge.Second.Consume();
			merge.First.IsMerging = false;
			_merges.RemoveAt(i);
		}
	}

	private static bool Available(GameExperienceOrb orb)
	{
		return orb.IsValid() && orb.Active && !orb.IsProxy && !orb.Consumed;
	}

	private static (int X, int Y, int Z) Cell(Vector3 position)
	{
		return (
			(int)MathF.Floor(position.x / ExperienceOrbRules.MergeDistance),
			(int)MathF.Floor(position.y / ExperienceOrbRules.MergeDistance),
			(int)MathF.Floor(position.z / ExperienceOrbRules.MergeDistance)
		);
	}

	private void FindPairs()
	{
		_cells.Clear();
		foreach (var orb in Scene.GetAllComponents<GameExperienceOrb>())
		{
			if (!Available(orb) || orb.IsMerging)
			{
				continue;
			}

			var cell = Cell(orb.WorldPosition);
			GameExperienceOrb nearest = null;
			List<GameExperienceOrb> nearestCell = null;
			var nearestDistance = float.PositiveInfinity;
			for (var x = -1; x <= 1; x++)
			{
				for (var y = -1; y <= 1; y++)
				{
					for (var z = -1; z <= 1; z++)
					{
						if (!_cells.TryGetValue((cell.X + x, cell.Y + y, cell.Z + z), out var candidates))
						{
							continue;
						}

						foreach (var candidate in candidates)
						{
							var distance = (candidate.WorldPosition - orb.WorldPosition).LengthSquared;
							if (distance < nearestDistance && ExperienceOrbRules.CanMerge(
								amount: orb.Amount,
								otherAmount: candidate.Amount,
								distanceSquared: distance
							))
							{
								nearest = candidate;
								nearestCell = candidates;
								nearestDistance = distance;
							}
						}
					}
				}
			}

			if (nearest != null)
			{
				nearestCell.Remove(nearest);
				nearest.IsMerging = true;
				orb.IsMerging = true;
				var merge = new Merge
				{
					First = nearest,
					Second = orb,
					FirstStart = nearest.WorldPosition,
					SecondStart = orb.WorldPosition,
					Midpoint = (nearest.WorldPosition + orb.WorldPosition) * 0.5f
				};
				_merges.Add(merge);
				continue;
			}

			if (!_cells.TryGetValue(cell, out var occupants))
			{
				occupants = new List<GameExperienceOrb>();
				_cells.Add(cell, occupants);
			}
			occupants.Add(orb);
		}
		_cells.Clear();
	}
}
