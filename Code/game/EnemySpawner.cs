using Sandbox;

public sealed class EnemySpawner : Component
{
	[Group( "Enemy Spawning" )]
	[Property] public GameObject EnemyPrefab { get; set; }

	[Group( "Enemy Spawning" )]
	[Property, Range( 0.1f, 60f )] public float SpawnInterval { get; set; } = 3f;

	[Group( "Enemy Spawning" )]
	[Property, Range( 0f, 1000f )] public float SpawnRadius { get; set; } = 400f;

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
		var distance = System.MathF.Max( 0f, SpawnRadius ) * System.MathF.Sqrt((float)_random.NextDouble());
		var spawnPosition = WorldPosition + new Vector3(
			System.MathF.Cos(angle) * distance,
			System.MathF.Sin(angle) * distance,
			0f
		);

		var enemy = EnemyPrefab.Clone(spawnPosition);
		var playerController = enemy.GetComponent<PlayerController>();
		if (playerController == null)
		{
			enemy.Destroy();
			Log.Error("Enemy prefab must include a PlayerController.");
			return;
		}

		playerController.UseInputControls = false;
		playerController.UseLookControls = false;
		playerController.UseCameraControls = false;

		var dresser = enemy.GetComponent<Dresser>();
		if (dresser == null)
		{
			enemy.Destroy();
			Log.Error("Enemy prefab must include a Dresser.");
			return;
		}

		dresser.Source = Dresser.ClothingSource.Manual;
		dresser.Randomize();

		if (!enemy.NetworkSpawn())
		{
			enemy.Destroy();
			Log.Error("Failed to network-spawn enemy.");
		}
	}
}
