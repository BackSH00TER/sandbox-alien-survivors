using Sandbox;

public enum GameState
{
	InProgress,
	Ended
}

public sealed class GameManager : Component
{
	[Group("Game Settings")]
	[Property, Description("Seconds until the boss spawns. Defeating the boss ends the match.")]
	public int CountdownTime { get; set; } = 60;
	[Group("Game Settings"), Description("Ends the game as soon as every survivor is dead, since nobody is left to fire a revive flare. Turn off while debugging revives.")]
	[Property] public bool ShouldEndWhenAllDead { get; set; } = false;
	[Sync(SyncFlags.FromHost)] public GameState State { get; set; } = GameState.InProgress;
	[Sync(SyncFlags.FromHost)] public Enemy Boss { get; private set; }
	[Sync(SyncFlags.FromHost)] public bool BossSpawned { get; private set; }

	[Group("Scene Transition")]
	[Property] public string LobbyScene { get; set; }

	private float _countdownEndTime;

	public int RemainingSeconds => System.Math.Max(0, (int)System.MathF.Ceiling(_countdownEndTime - Time.Now));

	private bool _missingSpawnerLogged;

	protected override void OnStart()
	{
		_countdownEndTime = Time.Now + CountdownTime;
	}

	protected override void OnUpdate()
	{
		if (!Networking.IsHost || State != GameState.InProgress)
			return;

		if (ShouldEndWhenAllDead && AreAllSurvivorsDead())
		{
			EndGame();
			return;
		}

		if (RemainingSeconds <= 0 && !BossSpawned)
			SpawnBoss();
	}

	private void SpawnBoss()
	{
		var spawner = Scene.GetAllComponents<EnemySpawner>().FirstOrDefault(x => x.Active);
		if (!spawner.IsValid())
		{
			if (!_missingSpawnerLogged)
			{
				Log.Error("GameManager requires an active EnemySpawner to spawn the boss.");
				_missingSpawnerLogged = true;
			}
			return;
		}

		_missingSpawnerLogged = false;
		var boss = spawner.SpawnBoss();
		if (!boss.IsValid())
			return;

		Boss = boss;
		BossSpawned = true;
	}

	public void BossDefeated(Enemy enemy)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Only the host can end the match after a boss defeat.");
			return;
		}

		if (State != GameState.InProgress)
			return;

		if (!BossSpawned || !enemy.IsValid() || enemy != Boss || enemy.Health > 0f)
		{
			Log.Warning("Rejected a boss defeat for an enemy that is not the defeated match boss.");
			return;
		}

		EndGame();
	}

	private bool AreAllSurvivorsDead()
	{
		List<SurvivorController> survivors = Scene.GetAllComponents<SurvivorController>().ToList();
		return survivors.Count > 0 && survivors.All(x => x.IsDead);
	}

	private void EndGame()
	{
		State = GameState.Ended;

		if (string.IsNullOrWhiteSpace(LobbyScene))
		{
			Log.Error("Cannot end game because LobbyScene is not set.");
			return;
		}

		var options = new SceneLoadOptions();
		if (!options.SetScene(LobbyScene))
		{
			Log.Error($"Could not set lobby scene: {LobbyScene}");
			return;
		}

		if (!Game.ChangeScene(options))
			Log.Error($"Could not change to lobby scene: {LobbyScene}");
	}
}
