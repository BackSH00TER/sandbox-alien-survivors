using Sandbox;

public enum GameState
{
	InProgress,
	Ended
}

public sealed class GameManager : Component, IGameEventWorld
{
	[Group("Game Settings")] [Property, Description("Seconds until the boss spawns. Defeating the boss ends the match.")] public int CountdownTime { get; set; } = 60;
	[Group("Game Settings"), Description("Ends the game as soon as every player is dead, since nobody is left to fire a revive flare. Turn off while debugging revives.")] [Property] public bool ShouldEndWhenAllDead { get; set; } = true;
	[Sync(SyncFlags.FromHost)] public GameState State { get; set; } = GameState.InProgress;
	[Sync(SyncFlags.FromHost)] public GameEnemy Boss { get; private set; }
	[Sync(SyncFlags.FromHost)] public bool BossSpawned { get; private set; }

	[Group("Scene Transition")] [Property] public string LobbyScene { get; set; }
	[Group("Scene Transition")] [Property] public string GameScene { get; set; } = "scenes/game.scene";

	[Sync(SyncFlags.FromHost)] public TimeSince RoundClock { get; private set; }
	[Sync(SyncFlags.FromHost)] public bool TimelineReady { get; private set; }
	[Sync(SyncFlags.FromHost)] public float BossEventSeconds { get; private set; }
	[Sync(SyncFlags.FromHost)] public NetList<GameStatMoment> RoundMoments { get; private set; } = new();
	[Sync(SyncFlags.FromHost)] public NetList<GameScheduledEvent> ScheduledEvents { get; private set; } = new();
	[Property, Group("Dropship")] public float DropshipHealth { get; set; } = 500f;
	[Property, Group("Dropship")] public float DropshipSpawnInterval { get; set; } = 3f;
	[Property, Group("Dropship")] public float DropshipSpawnRadius { get; set; } = 900f;
	[Property, Group("Dropship"), Description("Optional enemy prefab for reinforcements, including future heavy enemies. Uses the regular enemy prefab when unset.")] public GameObject DropshipEnemyPrefab { get; set; }
	[Property, Group("Dropship Loot")] public int DropshipExperience { get; set; } = 100;
	[Property, Group("Dropship Loot")] public int DropshipHealthKits { get; set; } = 1;
	[Property, Group("Dropship Loot")] public int DropshipArmorKits { get; set; } = 1;
	[Sync(SyncFlags.FromHost)] private float RoundEndedAt { get; set; }
	private bool _isChangingScene;
	private GameEventSchedule _eventSchedule;
	private float _nextEventAttempt;

	public float ElapsedSeconds => State == GameState.Ended ? RoundEndedAt : System.Math.Max(0f, RoundClock.Relative);
	public int RemainingSeconds => System.Math.Max(0, (int)System.MathF.Ceiling(BossEventSeconds - ElapsedSeconds));

	public IEnumerable<GameTimelineEvent> TimelineEvents
	{
		get
		{
			if (TimelineReady)
			{
				yield return new GameTimelineEvent(
					Seconds: BossEventSeconds,
					Icon: "star",
					Label: "Boss spawn"
				);
				foreach (var scheduled in ScheduledEvents.OrderBy(item => item.Seconds))
				{
					var definition = GameEventCatalog.Get(scheduled.Kind);
					yield return new GameTimelineEvent(
						Seconds: scheduled.Seconds,
						Icon: definition.Icon,
						Label: definition.Label
					);
				}
			}
		}
	}

	private bool _missingSpawnerLogged;

	protected override void OnStart()
	{
		if (Networking.IsHost)
		{
			_eventSchedule = new GameEventSchedule();
			RoundClock = 0f;
			BossEventSeconds = System.Math.Max(0, CountdownTime);
			TimelineReady = true;
			RecordRoundMoment("Round started");
			ScheduleEvents();
		}
	}

	protected override void OnUpdate()
	{
		if (!Networking.IsHost || State == GameState.Ended)
		{
			return;
		}

		if (ShouldEndWhenAllDead && AreAllPlayersDead())
		{
			EndGame();
			return;
		}

		if (RemainingSeconds <= 0 && !BossSpawned)
		{
			SpawnBoss();
		}

		UpdateEvents();
	}

	private void ScheduleEvents()
	{
		var newlyScheduled = _eventSchedule.Advance(ElapsedSeconds, () => Game.Random.Float());
		foreach (var scheduled in newlyScheduled)
		{
			ScheduledEvents.Add(scheduled);
		}
	}

	private void UpdateEvents()
	{
		ScheduleEvents();
		if (ElapsedSeconds < _nextEventAttempt)
		{
			return;
		}

		for (var i = 0; i < ScheduledEvents.Count; i++)
		{
			var scheduled = ScheduledEvents[i];
			if (scheduled.Occurred || ElapsedSeconds < scheduled.Seconds)
			{
				continue;
			}

			var definition = GameEventCatalog.Get(scheduled.Kind);
			if (!definition.TryStart(this))
			{
				_nextEventAttempt = ElapsedSeconds + 1f;
				continue;
			}

			ScheduledEvents[i] = scheduled with
			{
				Seconds = ElapsedSeconds,
				Occurred = true
			};
			RecordRoundMoment($"{definition.Label} arrived");
		}
	}

	public bool TryLandDropship()
	{
		if (!Networking.IsHost || State != GameState.InProgress)
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
			health: DropshipHealth,
			spawnInterval: DropshipSpawnInterval,
			spawnRadius: DropshipSpawnRadius,
			experience: DropshipExperience,
			healthKits: DropshipHealthKits,
			armorKits: DropshipArmorKits,
			enemyPrefab: DropshipEnemyPrefab
		);
		return ship.IsValid();
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
		{
			return;
		}

		Boss = boss;
		BossSpawned = true;
		BossEventSeconds = ElapsedSeconds;
		RecordRoundMoment("Boss spawned");
	}

	public void BossDefeated(GameEnemy enemy)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Only the host can end the match after a boss defeat.");
			return;
		}

		if (State != GameState.InProgress)
		{
			return;
		}

		if (!BossSpawned || !enemy.IsValid() || enemy != Boss || enemy.Health > 0f)
		{
			Log.Warning("Rejected a boss defeat for an enemy that is not the defeated match boss.");
			return;
		}

		RecordRoundMoment("Boss defeated");
		EndGame();
	}

	private bool AreAllPlayersDead()
	{
		List<GamePlayer> players = Scene.GetAllComponents<GamePlayer>().ToList();
		return players.Count > 0 && players.All(x => x.IsDead);
	}

	private void EndGame()
	{
		RoundEndedAt = ElapsedSeconds;
		State = GameState.Ended;
		RecordRoundMoment("Round ended");
		GamePlayerStats.EndRound(Scene);
	}

	/// <summary>Record actual event occurrences, not scheduled future events.</summary>
	public void RecordRoundMoment(string label)
	{
		if (!Networking.IsHost || !TimelineReady || string.IsNullOrWhiteSpace(label))
		{
			Log.Error("Round moments require the host, an initialized clock, and a label.");
			return;
		}

		RoundMoments.Add(new GameStatMoment(
			Seconds: ElapsedSeconds,
			Kind: "world",
			Label: label
		));
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
		{
			return;
		}

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
