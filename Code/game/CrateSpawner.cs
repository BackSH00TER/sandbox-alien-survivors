using Sandbox;

public sealed class CrateSpawner : Component
{
	[Property, Range(1, 50), Description("Maximum live crates. Each broken crate frees one slot after the respawn delay.")]
	public int MaxCrates { get; set; } = 5;
	[Property, Range(0.1f, 120f), Description("Delay after a crate breaks, independent of when its loot is collected.")]
	public float RespawnDelay { get; set; } = 10f;
	[Property, Range(100f, 5000f)] public float SpawnRadius { get; set; } = 900f;
	[Property] public WeaponDefinition Rifle { get; set; }
	[Property] public WeaponDefinition Shotgun { get; set; }

	private sealed class Slot
	{
		public SupplyCrate Crate;
		public float ReadyAt;
	}

	private readonly List<Slot> _slots = new();
	private bool _spawnFailureLogged;

	protected override void OnStart()
	{
		if (!Networking.IsHost)
			return;

		Rifle ??= ResourceLibrary.Get<WeaponDefinition>("weapons/rifle.weapon");
		Shotgun ??= ResourceLibrary.Get<WeaponDefinition>("weapons/shotgun.weapon");
		if (MaxCrates < 1 || MaxCrates > 50 || !float.IsFinite(RespawnDelay) || RespawnDelay <= 0f
			|| !float.IsFinite(SpawnRadius) || SpawnRadius < 100f
			|| Rifle == null || Shotgun == null || !Rifle.Validate() || !Shotgun.Validate())
		{
			Log.Error("CrateSpawner requires valid spawning settings and rifle/shotgun resources.");
			Enabled = false;
			return;
		}

		for (var i = 0; i < MaxCrates; i++)
			_slots.Add(new Slot());
	}

	protected override void OnUpdate()
	{
		if (!Networking.IsHost)
			return;

		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (manager != null && manager.State != GameState.InProgress)
			return;

		foreach (var slot in _slots)
		{
			if (slot.Crate != null)
			{
				if (slot.Crate.IsValid())
					continue;

				slot.Crate = null;
				slot.ReadyAt = Time.Now + RespawnDelay;
			}

			if (Time.Now < slot.ReadyAt)
				continue;

			if (TryFindPosition(out var position))
			{
				slot.Crate = SupplyCrate.Spawn(position, Rifle, Shotgun);
				if (slot.Crate.IsValid())
				{
					_spawnFailureLogged = false;
					continue;
				}
			}
			else if (!_spawnFailureLogged)
			{
				Log.Warning("CrateSpawner could not find clear ground within its radius; retrying in one second.");
				_spawnFailureLogged = true;
			}

			slot.ReadyAt = Time.Now + 1f;
		}
	}

	private bool TryFindPosition(out Vector3 position)
	{
		for (var attempt = 0; attempt < 24; attempt++)
		{
			var angle = Game.Random.Float(0f, System.MathF.PI * 2f);
			var distance = SpawnRadius * System.MathF.Sqrt(Game.Random.Float(0f, 1f));
			var candidate = WorldPosition + new Vector3(System.MathF.Cos(angle), System.MathF.Sin(angle), 0f) * distance;
			var ground = Scene.Trace.FromTo(candidate + Vector3.Up * 512f, candidate - Vector3.Up * 2000f)
				.IgnoreDynamic().Run();
			if (!ground.Hit || ground.StartedSolid || ground.Normal.z < 0.9f
				|| (ground.GameObject.IsValid() && ground.GameObject.GetComponentInParent<Entity>().IsValid()))
				continue;

			candidate = ground.EndPosition;
			if (Scene.GetAllComponents<SupplyCrate>().Any(x => (x.WorldPosition - candidate).Length < 100f)
				|| Scene.GetAllComponents<SurvivorController>().Any(x => (x.WorldPosition - candidate).Length < 100f))
				continue;

			var bounds = new BBox(new Vector3(-21f, -21f, 1f), new Vector3(21f, 21f, 46f));
			var clearance = Scene.Trace.Box(bounds, candidate, candidate).Run();
			if (clearance.Hit || clearance.StartedSolid)
				continue;

			position = candidate;
			return true;
		}

		position = default;
		return false;
	}
}
