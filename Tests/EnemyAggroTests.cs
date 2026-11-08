using System;

internal static class EnemyAggroTests
{
	private sealed class Player
	{
		public bool Active { get; set; } = true;
	}

	public static void Cooldown()
	{
		var first = new Player();
		var second = new Player();
		var third = new Player();
		var aggro = new EnemyAggro<Player>(5f, player => player.Active);

		aggro.RecordHit(first, 10f);
		Check(aggro.Target == first, "The first shooter should immediately gain aggro.");
		aggro.RecordHit(second, 11f);
		aggro.RecordHit(third, 12f);
		aggro.RecordHit(first, 13f);
		aggro.Advance(14.99f);
		Check(aggro.Target == first, "Hits during the cooldown must not switch targets.");
		aggro.Advance(15f);
		Check(aggro.Target == third, "The latest competing shooter should gain aggro at the deadline.");
		aggro.RecordHit(second, 15.1f);
		aggro.Advance(19.99f);
		Check(aggro.Target == third, "A new target must get its own full cooldown.");
		aggro.Advance(20f);
		Check(aggro.Target == second, "Queued aggro should switch without requiring another shot.");
		aggro.Advance(30f);
		Check(aggro.Target == second, "An expired cooldown alone must not change aggro.");
		aggro.RecordHit(first, 30f);
		Check(aggro.Target == first, "A hit after the cooldown should switch immediately.");
	}

	public static void Unavailable()
	{
		var first = new Player();
		var second = new Player();
		var third = new Player();
		var aggro = new EnemyAggro<Player>(5f, player => player.Active);

		aggro.RecordHit(first, 0f);
		aggro.RecordHit(second, 1f);
		second.Active = false;
		aggro.Advance(5f);
		Check(aggro.Target == first, "An unavailable queued shooter must not gain aggro.");
		aggro.RecordHit(third, 6f);
		Check(aggro.Target == third, "An eligible shooter should gain aggro after the cooldown.");
		aggro.RecordHit(first, 7f);
		third.Active = false;
		aggro.Advance(7.1f);
		Check(aggro.Target == first, "An eligible queued shooter should replace an unavailable target immediately.");
		first.Active = false;
		aggro.Advance(7.2f);
		Check(aggro.Target == null, "Aggro should clear when the target is unavailable and nobody is queued.");
		second.Active = true;
		aggro.RecordHit(second, 7.3f);
		Check(aggro.Target == second, "A valid hit should immediately establish aggro after the target is cleared.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}
}
