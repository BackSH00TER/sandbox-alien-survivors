using Sandbox;
using System.Collections.Generic;

/// <summary>Dropship-specific configuration and execution. The manager only dispatches event actions.</summary>
public sealed class GameDropshipEvent : Component, IGameEventAction
{
	[Property] public float Health { get; set; } = 500f;
	[Property] public float SpawnInterval { get; set; } = 3f;
	[Property] public float SpawnRadius { get; set; } = 900f;
	[Property, Description("Optional reinforcement prefab for authored variants, including future heavy enemies.")] public GameObject EnemyPrefab { get; set; }
	[Property, Range(0, 1000)] public int Experience { get; set; } = 100;
	public List<GameLootDrop> LootDrops { get; set; } = new()
	{
		new GameLootDrop(
			kind: LootKind.Health,
			minimumCount: 1,
			maximumCount: 1
		),
		new GameLootDrop(
			kind: LootKind.Armor,
			minimumCount: 1,
			maximumCount: 1
		)
	};

	public bool TryStart()
	{
		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (!Networking.IsHost || !manager.IsValid() || manager.State != GameState.InProgress)
		{
			Log.Error("Dropship events require the host and an active round.");
			return false;
		}

		var spawner = Scene.GetAllComponents<GameEnemySpawner>().FirstOrDefault(item => item.Active);
		if (!spawner.IsValid())
		{
			Log.Error("Dropship events require an active EnemySpawner.");
			return false;
		}

		var ship = GameDropship.Spawn(
			spawner: spawner,
			health: Health,
			spawnInterval: SpawnInterval,
			spawnRadius: SpawnRadius,
			experience: Experience,
			lootDrops: LootDrops,
			enemyPrefab: EnemyPrefab
		);
		return ship.IsValid();
	}
}
