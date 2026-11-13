using Sandbox;

public sealed class GameBossEvent : Component, IGameEventAction
{
	[Sync(SyncFlags.FromHost)] public GameEnemy Boss { get; private set; }
	[Sync(SyncFlags.FromHost)] public bool Spawned { get; private set; }

	public bool TryStart()
	{
		var manager = GetComponent<GameManager>();
		if (!Networking.IsHost || !manager.IsValid() || manager.State != GameState.InProgress)
		{
			Log.Error("Boss events require the host and an active round.");
			return false;
		}

		if (Spawned)
		{
			Log.Error("The match boss has already spawned.");
			return false;
		}

		var spawner = Scene.GetAllComponents<GameEnemySpawner>().FirstOrDefault(item => item.Active);
		if (!spawner.IsValid())
		{
			Log.Error("Boss events require an active EnemySpawner.");
			return false;
		}

		var boss = spawner.SpawnBoss();
		if (!boss.IsValid())
		{
			Log.Error("The boss event could not spawn the match boss.");
			return false;
		}

		Boss = boss;
		Spawned = true;
		return true;
	}
}
