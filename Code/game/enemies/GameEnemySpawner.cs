using Sandbox;

public sealed class GameEnemySpawner : Component
{
	[Property, Group("Enemy Spawning"), Description("Prefab for ordinary melee enemies.")] public GameObject EnemyPrefab { get; set; }
	[Property, Group("Enemy Spawning")] public GameObject RangedEnemyPrefab { get; set; }
	[Property, Group("Enemy Spawning")] public GameObject ExplodingEnemyPrefab { get; set; }
	[Property, Group("Enemy Spawning")] public GameObject BossPrefab { get; set; }

	[Group("Enemy Spawning")][Property, Range(0.1f, 60f)] public float SpawnInterval { get; set; } = 3f;

	[Group("Enemy Spawning")][Property, Range(0.1f, 60f), Description("Spawn interval reached when the ramp completes.")] public float MinimumSpawnInterval { get; set; } = 0.6f;

	[Group("Enemy Spawning")][Property, Range(1f, 3600f), Description("Seconds of game time over which the spawn interval decreases from SpawnInterval to MinimumSpawnInterval.")] public float SpawnRampDuration { get; set; } = 60f;

	[Group("Enemy Spawning")][Property, Range(0f, 1000f)] public float SpawnRadius { get; set; } = 400f;

	[Group("Enemy Spawning")][Property, Range(0f, 100f), Description("Percentage of spawns that explode after a short death fuse.")] public float ExplodingEnemyChance { get; set; } = 5f;

	[Group("Enemy Spawning")][Property, Range(0f, 100f), Description("Percentage of spawns that shoot projectiles. Exploders take priority if the total exceeds 100%.")] public float RangedEnemyChance { get; set; } = 5f;

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
		SpawnEnemy(isBoss: false);
	}

	public GameEnemy SpawnBoss()
	{
		if (!Networking.IsHost)
		{
			Log.Error("Only the host can spawn the boss.");
			return null;
		}

		return SpawnEnemy(isBoss: true);
	}

	private GameEnemy SpawnEnemy(bool isBoss)
	{
		var angle = (float)(_random.NextDouble() * System.MathF.PI * 2f);
		var distance = System.MathF.Max(0f, SpawnRadius) * System.MathF.Sqrt((float)_random.NextDouble());
		var spawnPosition = WorldPosition + new Vector3(
			System.MathF.Cos(angle) * distance,
			System.MathF.Sin(angle) * distance,
			0f
		);

		if (isBoss)
		{
			var ground = Scene.Trace.FromTo(
				from: spawnPosition + Vector3.Up * 64f,
				to: spawnPosition - Vector3.Up * 2000f
			).Run();
			if (!ground.Hit)
			{
				Log.Error("Cannot spawn boss without ground beneath its spawn position.");
				return null;
			}
			spawnPosition = ground.EndPosition;
		}

		var selectedPrefab = isBoss ? BossPrefab : SelectEnemyPrefab();
		return SpawnAt(
			spawnPosition: spawnPosition,
			prefab: selectedPrefab
		);
	}

	private GameObject SelectEnemyPrefab()
	{
		var variantRoll = _random.NextDouble() * 100f;
		var explodingChance = ExplodingEnemyChance.Clamp(0f, 100f);
		if (variantRoll < explodingChance)
		{
			return ExplodingEnemyPrefab;
		}

		if (variantRoll < explodingChance + RangedEnemyChance.Clamp(0f, 100f))
		{
			return RangedEnemyPrefab;
		}

		return EnemyPrefab;
	}

	public GameEnemy SpawnReinforcement(Vector3 position, GameObject prefab = null)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Only the host can spawn dropship reinforcements.");
			return null;
		}

		var selectedPrefab = prefab ?? SelectEnemyPrefab();
		return SpawnAt(
			spawnPosition: position,
			prefab: selectedPrefab,
			fromDropship: true
		);
	}

	private GameEnemy SpawnAt(
		Vector3 spawnPosition,
		GameObject prefab,
		bool fromDropship = false
	)
	{
		if (!prefab.IsValid())
		{
			if (!_missingPrefabLogged)
			{
				Log.Error("EnemySpawner requires an assigned prefab for the selected enemy type.");
				_missingPrefabLogged = true;
			}

			return null;
		}

		_missingPrefabLogged = false;
		var cloneSettings = new CloneConfig
		{
			Transform = new Transform(spawnPosition),
			StartEnabled = false
		};
		var enemy = prefab.Clone(cloneSettings);
		var components = enemy.Components.GetAll<GameEnemy>(
			FindMode.InSelf | FindMode.Enabled | FindMode.Disabled
		).ToArray();
		if (components.Length != 1)
		{
			enemy.Destroy();
			Log.Error("Enemy prefabs require exactly one GameEnemy or derived enemy component on their root.");
			return null;
		}

		var component = components[0];
		if (fromDropship)
		{
			component.DescendOnSpawn = false;
		}

		enemy.Enabled = true;
		if (!enemy.NetworkSpawn())
		{
			enemy.Destroy();
			Log.Error("Failed to network-spawn enemy.");
			return null;
		}

		return component;
	}
}
