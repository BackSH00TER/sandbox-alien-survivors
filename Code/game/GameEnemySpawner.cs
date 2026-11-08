using Sandbox;

public sealed class GameEnemySpawner : Component
{
	[Group("Enemy Spawning")] [Property] public GameObject EnemyPrefab { get; set; }

	[Group("Enemy Spawning")] [Property, Range(0.1f, 60f)] public float SpawnInterval { get; set; } = 3f;

	[Group("Enemy Spawning")] [Property, Range(0.1f, 60f), Description("Spawn interval reached when the ramp completes.")] public float MinimumSpawnInterval { get; set; } = 0.6f;

	[Group("Enemy Spawning")] [Property, Range(1f, 3600f), Description("Seconds of game time over which the spawn interval decreases from SpawnInterval to MinimumSpawnInterval.")] public float SpawnRampDuration { get; set; } = 60f;

	[Group("Enemy Spawning")] [Property, Range(0f, 1000f)] public float SpawnRadius { get; set; } = 400f;

	[Group("Enemy Spawning")] [Property, Range(0f, 100f), Description("Percentage of spawns that explode after a short death fuse.")] public float ExplodingEnemyChance { get; set; } = 5f;

	[Group("Enemy Spawning")] [Property, Range(0f, 100f), Description("Percentage of spawns that shoot projectiles. Exploders take priority if the total exceeds 100%.")] public float RangedEnemyChance { get; set; } = 10f;

	private readonly System.Random _random = new();
	private float _spawnTimer;
	private bool _missingPrefabLogged;
	private bool _invalidSpawnSettingsLogged;
	private float _elapsedSpawnTime;

	protected override void OnUpdate()
	{
		if (!Networking.IsHost)
		{
			return;
		}

		if (SpawnInterval <= 0f || MinimumSpawnInterval <= 0f
			|| MinimumSpawnInterval > SpawnInterval || SpawnRampDuration <= 0f)
		{
			if (!_invalidSpawnSettingsLogged)
			{
				Log.Error("EnemySpawner requires positive spawn intervals, MinimumSpawnInterval no greater than SpawnInterval, and a positive SpawnRampDuration.");
				_invalidSpawnSettingsLogged = true;
			}

			return;
		}

		_invalidSpawnSettingsLogged = false;

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (gameManager != null && gameManager.State != GameState.InProgress)
		{
			return;
		}

		var elapsedSeconds = gameManager != null
			? gameManager.ElapsedSeconds
			: _elapsedSpawnTime += Time.Delta;
		var rampProgress = (elapsedSeconds / SpawnRampDuration).Clamp(0f, 1f);
		var currentSpawnInterval = SpawnInterval
			+ (MinimumSpawnInterval - SpawnInterval) * rampProgress;

		_spawnTimer += Time.Delta;
		if (_spawnTimer < currentSpawnInterval)
		{
			return;
		}

		_spawnTimer = 0f;
		SpawnEnemy();
	}

	private void SpawnEnemy()
	{
		SpawnEnemy(false);
	}

	public GameEnemy SpawnBoss()
	{
		if (!Networking.IsHost)
		{
			Log.Error("Only the host can spawn the boss.");
			return null;
		}

		return SpawnEnemy(true);
	}

	private GameEnemy SpawnEnemy(bool isBoss)
	{
		if (EnemyPrefab == null)
		{
			if (!_missingPrefabLogged)
			{
				Log.Error("EnemySpawner has no EnemyPrefab assigned.");
				_missingPrefabLogged = true;
			}

			return null;
		}

		var angle = (float)(_random.NextDouble() * System.MathF.PI * 2f);
		var distance = System.MathF.Max(0f, SpawnRadius) * System.MathF.Sqrt((float)_random.NextDouble());
		var spawnPosition = WorldPosition + new Vector3(
			System.MathF.Cos(angle) * distance,
			System.MathF.Sin(angle) * distance,
			0f
		);

		if (isBoss)
		{
			var ground = Scene.Trace.FromTo(spawnPosition + Vector3.Up * 64f,
				spawnPosition - Vector3.Up * 2000f).Run();
			if (!ground.Hit)
			{
				Log.Error("Cannot spawn boss without ground beneath its spawn position.");
				return null;
			}
			spawnPosition = ground.EndPosition;
		}

		var enemy = EnemyPrefab.Clone(spawnPosition);
		var component = enemy.GetOrAddComponent<GameEnemy>();
		if (isBoss)
		{
			enemy.Name = "Boss";
			enemy.GetOrAddComponent<GameEnemyBoss>().Configure(component);
		}
		else
		{
			var variantRoll = _random.NextDouble() * 100f;
			var explodingChance = ExplodingEnemyChance.Clamp(0f, 100f);
			if (variantRoll < explodingChance)
			{
				enemy.GetOrAddComponent<GameEnemyExploding>();
			}
			else if (variantRoll < explodingChance + RangedEnemyChance.Clamp(0f, 100f))
			{
				enemy.GetOrAddComponent<GameEnemyRanged>();
			}
		}

		if (!enemy.NetworkSpawn())
		{
			enemy.Destroy();
			Log.Error("Failed to network-spawn enemy.");
			return null;
		}

		return component;
	}
}
