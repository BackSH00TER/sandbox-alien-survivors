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
	[Group("Game Settings"), Description("Ends the game as soon as every player is dead, since nobody is left to fire a revive flare. Turn off while debugging revives.")]
	[Property] public bool ShouldEndWhenAllDead { get; set; } = false;
	[Sync(SyncFlags.FromHost)] public GameState State { get; set; } = GameState.InProgress;
	[Sync(SyncFlags.FromHost)] public GameEnemy Boss { get; private set; }
	[Sync(SyncFlags.FromHost)] public bool BossSpawned { get; private set; }

	[Group("Scene Transition")]
	[Property] public string LobbyScene { get; set; }
	[Group("Scene Transition")]
	[Property] public string GameScene { get; set; } = "scenes/game.scene";

	private float _countdownEndTime;
	private bool _isChangingScene;

	public int RemainingSeconds => System.Math.Max(0, (int)System.MathF.Ceiling(_countdownEndTime - Time.Now));

	private bool _missingSpawnerLogged;

	protected override void OnStart()
	{
		_countdownEndTime = Time.Now + CountdownTime;
	}

	protected override void OnUpdate()
	{
		if (!Networking.IsHost || State == GameState.Ended)
			return;

		if (ShouldEndWhenAllDead && AreAllPlayersDead())
		{
			EndGame();
			return;
		}

		if (RemainingSeconds <= 0 && !BossSpawned)
			SpawnBoss();
	}

	private void SpawnBoss()
	{
		var spawner = Scene.GetAllComponents<GameEnemySpawner>().FirstOrDefault(x => x.Active);
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

	public void BossDefeated(GameEnemy enemy)
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

	private bool AreAllPlayersDead()
	{
		List<GamePlayer> players = Scene.GetAllComponents<GamePlayer>().ToList();
		return players.Count > 0 && players.All(x => x.IsDead);
	}

	private void EndGame()
	{
		State = GameState.Ended;
		GamePlayerStats.EndRound(Scene);
	}

	public void ReturnToLobby()
	{
		ChangeToScene(LobbyScene, "lobby");
	}

	public void StartNewGame()
	{
		ChangeToScene(GameScene, "game");
	}

	private void ChangeToScene(string scenePath, string sceneLabel)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Only the host can change scenes.");
			return;
		}

		if (_isChangingScene)
			return;

		if (string.IsNullOrWhiteSpace(scenePath))
		{
			Log.Error($"Cannot change scene because the {sceneLabel} scene is not set.");
			return;
		}

		var options = new SceneLoadOptions();
		if (!options.SetScene(scenePath))
		{
			Log.Error($"Could not set {sceneLabel} scene: {scenePath}");
			return;
		}

		_isChangingScene = true;
		if (!Game.ChangeScene(options))
		{
			_isChangingScene = false;
			Log.Error($"Could not change to {sceneLabel} scene: {scenePath}");
		}
	}
}
