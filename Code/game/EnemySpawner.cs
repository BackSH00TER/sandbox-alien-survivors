using Sandbox;

public sealed class EnemySpawner : Component
{
	[Group("Enemy Spawning")]
	[Property] public GameObject EnemyPrefab { get; set; }

	[Group("Enemy Spawning")]
	[Property, Range(0.1f, 60f)] public float SpawnInterval { get; set; } = 3f;

	[Group("Enemy Spawning")]
	[Property, Range(0f, 1000f)] public float SpawnRadius { get; set; } = 400f;

	[Group("Enemy Spawning")]
	[Property, Range(0f, 100f), Description("Percentage of spawns that explode after a short death fuse.")]
	public float ExplodingEnemyChance { get; set; } = 5f;

	[Group("Enemy Spawning")]
	[Property, Range(0f, 100f), Description("Percentage of spawns that shoot projectiles. Exploders take priority if the total exceeds 100%.")]
	public float RangedEnemyChance { get; set; } = 10f;

	private readonly System.Random _random = new();
	private float _spawnTimer;
	private bool _missingPrefabLogged;
	private bool _invalidIntervalLogged;

	protected override void OnUpdate()
	{
		if (!Networking.IsHost)
			return;

		if (SpawnInterval <= 0f)
		{
			if (!_invalidIntervalLogged)
			{
				Log.Error("EnemySpawner SpawnInterval must be greater than zero.");
				_invalidIntervalLogged = true;
			}

			return;
		}

		_invalidIntervalLogged = false;

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (gameManager != null && gameManager.State != GameState.InProgress)
			return;

		_spawnTimer += Time.Delta;
		if (_spawnTimer < SpawnInterval)
			return;

		_spawnTimer = 0f;
		SpawnEnemy();
	}

	private void SpawnEnemy()
	{
		if (EnemyPrefab == null)
		{
			if (!_missingPrefabLogged)
			{
				Log.Error("EnemySpawner has no EnemyPrefab assigned.");
				_missingPrefabLogged = true;
			}

			return;
		}

		var angle = (float)(_random.NextDouble() * System.MathF.PI * 2f);
		var distance = System.MathF.Max(0f, SpawnRadius) * System.MathF.Sqrt((float)_random.NextDouble());
		var spawnPosition = WorldPosition + new Vector3(
			System.MathF.Cos(angle) * distance,
			System.MathF.Sin(angle) * distance,
			0f
		);

		var enemy = EnemyPrefab.Clone(spawnPosition);
		enemy.GetOrAddComponent<Enemy>();
		var variantRoll = _random.NextDouble() * 100f;
		var explodingChance = ExplodingEnemyChance.Clamp(0f, 100f);
		if (variantRoll < explodingChance)
			enemy.GetOrAddComponent<ExplodingEnemy>();
		else if (variantRoll < explodingChance + RangedEnemyChance.Clamp(0f, 100f))
			enemy.GetOrAddComponent<RangedEnemy>();

		if (!enemy.NetworkSpawn())
		{
			enemy.Destroy();
			Log.Error("Failed to network-spawn enemy.");
		}
	}
}
