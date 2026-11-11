using Sandbox;
using System.Collections.Generic;

public interface IGameEventAction
{
	bool TryStart();
}

/// <summary>Runtime action registration, separate from scheduling and the round manager.</summary>
public static class GameEventActions
{
	public static IReadOnlyDictionary<GameEventKind, IGameEventAction> Create(GameObject owner)
	{
		return new Dictionary<GameEventKind, IGameEventAction>
		{
			[GameEventKind.Dropship] = owner.GetOrAddComponent<GameDropshipEvent>(),
			[GameEventKind.WeaponAirdrop] = owner.GetOrAddComponent<GameWeaponAirdropEvent>(),
			[GameEventKind.Boss] = owner.GetOrAddComponent<GameBossEvent>()
		};
	}
}
