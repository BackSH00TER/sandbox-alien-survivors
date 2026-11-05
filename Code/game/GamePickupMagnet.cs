using Sandbox;
using System;

internal static class GamePickupMagnet
{
	internal const float DefaultRange = 150f;
	internal const float DefaultSpeed = 200f;

	internal static GamePlayer Attract(
		Component pickup,
		float pickupRange,
		float magnetRange = DefaultRange,
		float magnetSpeed = DefaultSpeed,
		float targetHeight = 12f,
		Func<GamePlayer, bool> canAttract = null
	)
	{
		GamePlayer target = null;
		var nearestDistanceSquared = float.PositiveInfinity;
		foreach (var player in pickup.Scene.GetAllComponents<GamePlayer>())
		{
			if (!player.Active || player.Health <= 0f)
			{
				continue;
			}

			var distanceSquared = (player.WorldPosition + Vector3.Up * targetHeight - pickup.WorldPosition).LengthSquared;
			var collectionRange = magnetRange * player.ExperienceCollectionRangeMultiplier;
			if (distanceSquared <= collectionRange * collectionRange && distanceSquared <= nearestDistanceSquared
				&& (canAttract == null || canAttract(player)))
			{
				target = player;
				nearestDistanceSquared = distanceSquared;
			}
		}

		if (target == null)
		{
			return null;
		}

		if (nearestDistanceSquared <= pickupRange * pickupRange)
		{
			return target;
		}

		var targetPosition = target.WorldPosition + Vector3.Up * targetHeight;
		var direction = (targetPosition - pickup.WorldPosition).Normal;
		var distance = MathF.Sqrt(nearestDistanceSquared);
		var targetCollectionRange = magnetRange * target.ExperienceCollectionRangeMultiplier;
		var attractionDistance = targetCollectionRange - pickupRange;
		var proximity = 1f - (distance - pickupRange) / attractionDistance;
		var speed = magnetSpeed * (0.05f + 0.95f * proximity * proximity * proximity);
		var step = MathF.Min(speed * Time.Delta, distance);
		pickup.WorldPosition += direction * step;
		return null;
	}
}
