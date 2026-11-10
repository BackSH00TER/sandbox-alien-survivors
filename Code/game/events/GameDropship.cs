using Sandbox;
using System.Collections.Generic;

/// <summary>A persistent, shootable reinforcement source. Only its destruction awards loot.</summary>
public sealed class GameDropship : GameEntity
{
	[Property, Range(0.1f, 60f)] public float SpawnInterval { get; set; } = 3f;
	[Property, Range(0, 1000)] public int ExperienceReward { get; set; } = 100;
	public IReadOnlyList<GameLootDrop> LootDrops { get; private set; }
	[Sync(SyncFlags.FromHost)] public bool Landed { get; private set; }
	[Sync(SyncFlags.FromHost)] public Vector3 GroundPosition { get; private set; }
	[Sync(SyncFlags.FromHost)] private TimeSince ArrivalClock { get; set; }
	[Sync(SyncFlags.FromHost)] private GameEnemySpawner Spawner { get; set; }
	[Property] public GameObject EnemyPrefab { get; set; }
	public override bool CanTakeDamage => Landed && Health > 0f && !_destroyed && IsRoundInProgress;
	public override float BloodAmount => 0f;

	private const float LandingHeight = 600f;
	private const float LandingDuration = 3f;
	private float _spawnTimer;
	private bool _destroyed;
	private GameManager _manager;
	private bool IsRoundInProgress => !_manager.IsValid() || _manager.State == GameState.InProgress;

	public static GameDropship Spawn(
		GameEnemySpawner spawner,
		float health,
		float spawnInterval,
		float spawnRadius,
		int experience,
		IReadOnlyList<GameLootDrop> lootDrops,
		GameObject enemyPrefab = null
	)
	{
		if (!Networking.IsHost || !spawner.IsValid()
			|| !float.IsFinite(health) || health <= 0f
			|| !float.IsFinite(spawnInterval) || spawnInterval < 0.1f
			|| !float.IsFinite(spawnRadius) || spawnRadius < 200f
			|| experience < 0 || experience > 1000
			|| lootDrops == null || lootDrops.Any(drop => drop == null)
			|| !(enemyPrefab ?? spawner.EnemyPrefab).IsValid())
		{
			Log.Error("Dropships require the host, a spawner with an enemy prefab, positive health/interval, radius >= 200, XP in [0,1000], and a non-null loot table without null entries.");
			return null;
		}

		if (!TryFindLandingPosition(spawner, spawnRadius, out var groundPosition))
		{
			Log.Warning("Dropship could not find clear, flat landing ground; the event will retry.");
			return null;
		}

		var obj = new GameObject(enabled: true, name: "Alien Dropship");
		obj.WorldPosition = groundPosition + Vector3.Up * LandingHeight;
		var ship = obj.AddComponent<GameDropship>();
		ship.MaxHealth = health;
		ship.SpawnInterval = spawnInterval;
		ship.ExperienceReward = experience;
		ship.LootDrops = lootDrops.ToArray();
		ship.GroundPosition = groundPosition;
		ship.ArrivalClock = 0f;
		ship.Spawner = spawner;
		ship.EnemyPrefab = enemyPrefab;
		var collider = obj.AddComponent<BoxCollider>();
		collider.Center = Vector3.Up * 66f;
		collider.Scale = new Vector3(
			240f,
			230f,
			120f
		);
		if (!obj.NetworkSpawn())
		{
			obj.Destroy();
			Log.Error("Failed to network-spawn alien dropship.");
			return null;
		}

		return ship;
	}

	private static bool TryFindLandingPosition(
		GameEnemySpawner spawner,
		float radius,
		out Vector3 position
	)
	{
		var scene = spawner.Scene;
		var footprint = new BBox(
			new Vector3(
				-150f,
				-150f,
				2f
			),
			new Vector3(
				150f,
				150f,
				150f
			)
		);
		for (var attempt = 0; attempt < 24; attempt++)
		{
			var angle = Game.Random.Float(0f, System.MathF.PI * 2f);
			var distance = radius * System.MathF.Sqrt(Game.Random.Float());
			var direction = Vector3.Forward * System.MathF.Cos(angle) + Vector3.Left * System.MathF.Sin(angle);
			var candidate = spawner.WorldPosition + direction * distance;
			var ground = scene.Trace.FromTo(candidate + Vector3.Up * 512f, candidate - Vector3.Up * 2000f)
				.IgnoreDynamic().Run();
			if (!ground.Hit || ground.StartedSolid || ground.Normal.z < 0.98f
				|| (ground.GameObject.IsValid() && ground.GameObject.GetComponentInParent<GameEntity>().IsValid()))
			{
				continue;
			}

			candidate = ground.EndPosition;
			var occupied = scene.GetAllComponents<GameEntity>()
				.Any(entity => (entity.WorldPosition - candidate).WithZ(0f).Length < 220f);
			var clearance = scene.Trace.Box(
				bbox: footprint,
				from: candidate,
				to: candidate + Vector3.Up * LandingHeight
			).Run();
			if (occupied || clearance.Hit || clearance.StartedSolid)
			{
				continue;
			}

			position = candidate;
			return true;
		}

		position = default;
		return false;
	}

	protected override void OnStart()
	{
		base.OnStart();
		_manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		var hullColor = new Color(
			0.2f,
			0.25f,
			0.3f
		);
		var glowColor = new Color(
			0.3f,
			1f,
			0.65f
		);
		AddPart(
			name: "Armored Hull",
			position: Vector3.Up * 66f,
			size: new Vector3(
				240f,
				150f,
				60f
			),
			color: hullColor,
			modelPath: "models/dev/sphere.vmdl"
		);
		AddPart(
			name: "Alien Cockpit",
			position: Vector3.Up * 100f,
			size: new Vector3(
				110f,
				100f,
				42f
			),
			color: glowColor,
			modelPath: "models/dev/sphere.vmdl"
		);
		foreach (var side in new[] { -1f, 1f })
		{
			AddPart(
				name: "Engine Pod",
				position: Vector3.Left * (95f * side) + Vector3.Up * 60f,
				size: new Vector3(
					180f,
					40f,
					50f
				),
				color: hullColor
			);
			AddPart(
				name: "Engine Glow",
				position: Vector3.Left * (95f * side) + Vector3.Up * 40f,
				size: new Vector3(
					140f,
					30f,
					12f
				),
				color: glowColor
			);
			foreach (var end in new[] { -1f, 1f })
			{
				AddPart(
					name: "Landing Strut",
					position: Vector3.Forward * (75f * end) + Vector3.Left * (65f * side) + Vector3.Up * 22f,
					size: new Vector3(
						16f,
						16f,
						44f
					),
					color: hullColor
				);
			}
		}
	}

	private void AddPart(
		string name,
		Vector3 position,
		Vector3 size,
		Color color,
		string modelPath = "models/dev/box.vmdl"
	)
	{
		var part = new GameObject(
			parent: GameObject,
			enabled: true,
			name: name
		);
		part.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		part.LocalPosition = position;
		var renderer = part.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load(modelPath);
		renderer.Tint = color;
		var modelWidth = renderer.Model.Bounds.Size.x;
		part.LocalScale = size / modelWidth;
	}

	protected override void OnFixedUpdate()
	{
		if (!Networking.IsHost || IsProxy || !IsRoundInProgress || _destroyed)
		{
			return;
		}

		if (!Landed)
		{
			var progress = (ArrivalClock.Relative / LandingDuration).Clamp(0f, 1f);
			var eased = progress * progress * (3f - 2f * progress);
			WorldPosition = GroundPosition + Vector3.Up * (LandingHeight * (1f - eased));
			if (progress >= 1f)
			{
				Landed = true;
				_manager?.RecordRoundMoment("Alien dropship landed");
			}
			return;
		}

		_spawnTimer += Time.Delta;
		if (_spawnTimer < SpawnInterval)
		{
			return;
		}

		_spawnTimer = 0f;
		if (!Spawner.IsValid() || !Spawner.Active)
		{
			Log.Error("Dropship lost its active enemy spawner; reinforcements cannot spawn.");
			return;
		}

		var angle = Game.Random.Float(0f, System.MathF.PI * 2f);
		var direction = Vector3.Forward * System.MathF.Cos(angle) + Vector3.Left * System.MathF.Sin(angle);
		var candidate = GroundPosition + direction * 200f;
		var ground = Scene.Trace.FromTo(candidate + Vector3.Up * 128f, candidate - Vector3.Up * 512f)
			.IgnoreGameObjectHierarchy(GameObject).IgnoreDynamic().Run();
		if (!ground.Hit || ground.StartedSolid || ground.Normal.z < 0.9f
			|| (ground.GameObject.IsValid() && ground.GameObject.GetComponentInParent<GameEntity>().IsValid()))
		{
			Log.Warning("Dropship reinforcement exit has no valid ground; retrying next spawn interval.");
			return;
		}

		Spawner.SpawnReinforcement(position: ground.EndPosition, prefab: EnemyPrefab);
	}

	protected override void OnHealthDepleted()
	{
		if (_destroyed)
		{
			return;
		}

		_destroyed = true;
		_manager?.RecordRoundMoment("Alien dropship destroyed");
		var orbAmounts = ExperienceOrbRules.SplitReward(ExperienceReward);
		for (var i = 0; i < orbAmounts.Length; i++)
		{
			var position = RewardPosition(index: i, count: orbAmounts.Length);
			GameExperienceOrb.Spawn(
				scene: Scene,
				position: position,
				amount: orbAmounts[i]
			);
		}

		var loot = new List<LootDefinition>();
		foreach (var drop in LootDrops)
		{
			var count = drop.RollCount((minimum, maximum) => Game.Random.Next(minimum, maximum));
			var definition = GameLootCatalog.Get(drop.Kind);
			for (var i = 0; i < count; i++)
			{
				loot.Add(definition);
			}
		}
		for (var i = 0; i < loot.Count; i++)
		{
			var position = RewardPosition(index: i, count: loot.Count);
			GameLootPickup.Spawn(groundPosition: position, definition: loot[i]);
		}

		GameObject.Destroy();
	}

	private Vector3 RewardPosition(int index, int count)
	{
		var angle = index * System.MathF.PI * 2f / count;
		var direction = Vector3.Forward * System.MathF.Cos(angle) + Vector3.Left * System.MathF.Sin(angle);
		return GroundPosition + direction * 100f;
	}
}
