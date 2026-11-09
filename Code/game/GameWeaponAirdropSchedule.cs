using System;

public static class GameWeaponAirdropSchedule
{
	public static float SecondsFor(int index)
	{
		if (index < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(index));
		}

		return index == 0 ? 60f : index * 300f;
	}
}
