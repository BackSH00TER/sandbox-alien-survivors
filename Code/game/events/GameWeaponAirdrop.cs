using Sandbox;

/// <summary>Host-owned delivery; peers animate the aircraft and falling cargo from the synchronized clock.</summary>
public sealed class GameWeaponAirdrop : Component
{
	private const float ReleaseSeconds = 3f;
	private const float LandingSeconds = 5f;
	private const float DepartureSeconds = 6f;
	private const float FlightHeight = 360f;

	[Sync(SyncFlags.FromHost)] public Vector3 DropPosition { get; private set; }
	[Sync(SyncFlags.FromHost)] private Vector3 FlightDirection { get; set; }
	[Sync(SyncFlags.FromHost)] private TimeSince FlightClock { get; set; }
	[Sync(SyncFlags.FromHost)] private bool CrateLanded { get; set; }
	[Sync(SyncFlags.FromHost)] private GameWeaponDefinition Rifle { get; set; }
	[Sync(SyncFlags.FromHost)] private GameWeaponDefinition Shotgun { get; set; }

	private GameObject _aircraft;
	private GameObject _cargo;
	private float _landingRetryAt;

	public static GameWeaponAirdrop Spawn(
		Vector3 groundPosition,
		GameWeaponDefinition rifle,
		GameWeaponDefinition shotgun
	)
	{
		if (!Networking.IsHost || rifle == null || shotgun == null || !rifle.Validate() || !shotgun.Validate())
		{
			Log.Error("Weapon airdrops require the host and valid rifle/shotgun resources.");
			return null;
		}

		var obj = new GameObject(enabled: true, name: "Weapon Airdrop");
		obj.WorldPosition = groundPosition;
		var drop = obj.AddComponent<GameWeaponAirdrop>();
		drop.DropPosition = groundPosition;
		var yaw = Game.Random.Float(0f, 360f);
		drop.FlightDirection = Rotation.FromYaw(yaw) * Vector3.Forward;
		drop.FlightClock = 0f;
		drop.Rifle = rifle;
		drop.Shotgun = shotgun;
		if (!obj.NetworkSpawn())
		{
			obj.Destroy();
			Log.Error("Failed to network-spawn weapon airdrop.");
			return null;
		}

		return drop;
	}

	protected override void OnStart()
	{
		_aircraft = CreateVisual("Delivery Aircraft");
		_cargo = CreateVisual("Falling Weapon Crate");
		var aircraftColor = new Color(
			r: 0.38f,
			g: 0.47f,
			b: 0.55f
		);
		var cargoColor = new Color(
			r: 0.16f,
			g: 0.36f,
			b: 0.51f
		);
		var bodySize = new Vector3(
			x: 180f,
			y: 36f,
			z: 30f
		);
		AddBox(
			parent: _aircraft,
			position: Vector3.Zero,
			size: bodySize,
			color: aircraftColor
		);
		var wingSize = new Vector3(
			x: 40f,
			y: 230f,
			z: 10f
		);
		AddBox(
			parent: _aircraft,
			position: Vector3.Zero,
			size: wingSize,
			color: aircraftColor
		);
		var tailSize = new Vector3(
			x: 35f,
			y: 12f,
			z: 50f
		);
		AddBox(
			parent: _aircraft,
			position: Vector3.Backward * 65f + Vector3.Up * 22f,
			size: tailSize,
			color: aircraftColor
		);
		var tailWingSize = new Vector3(
			x: 30f,
			y: 90f,
			z: 8f
		);
		AddBox(
			parent: _aircraft,
			position: Vector3.Backward * 65f,
			size: tailWingSize,
			color: aircraftColor
		);
		var cargoSize = new Vector3(
			x: 40f,
			y: 40f,
			z: 44f
		);
		AddBox(
			parent: _cargo,
			position: Vector3.Zero,
			size: cargoSize,
			color: cargoColor
		);
		var bandSize = new Vector3(
			x: 42f,
			y: 8f,
			z: 46f
		);
		var bandColor = new Color(
			r: 1f,
			g: 0.78f,
			b: 0.34f
		);
		AddBox(
			parent: _cargo,
			position: Vector3.Zero,
			size: bandSize,
			color: bandColor
		);
		_cargo.Enabled = false;
	}

	private GameObject CreateVisual(string name)
	{
		var visual = new GameObject(
			parent: GameObject,
			enabled: true,
			name: name
		);
		visual.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		return visual;
	}

	private static void AddBox(
		GameObject parent,
		Vector3 position,
		Vector3 size,
		Color color
	)
	{
		var part = new GameObject(
			parent: parent,
			enabled: true,
			name: "Body Part"
		);
		part.LocalPosition = position;
		part.LocalScale = size / 50f;
		var renderer = part.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load("models/dev/box.vmdl");
		renderer.Tint = color;
	}

	protected override void OnUpdate()
	{
		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (manager.IsValid() && manager.State != GameState.InProgress)
		{
			_aircraft.Enabled = false;
			_cargo.Enabled = false;
			if (Networking.IsHost)
			{
				GameObject.Destroy();
			}
			return;
		}

		float age = FlightClock;
		_aircraft.Enabled = age < DepartureSeconds;
		_aircraft.WorldPosition = DropPosition + Vector3.Up * FlightHeight + FlightDirection * ((age - ReleaseSeconds) * 500f);
		_aircraft.WorldRotation = Rotation.LookAt(FlightDirection);
		_cargo.Enabled = age >= ReleaseSeconds && !CrateLanded;
		var fallProgress = ((age - ReleaseSeconds) / (LandingSeconds - ReleaseSeconds)).Clamp(0f, 1f);
		_cargo.WorldPosition = DropPosition + Vector3.Up * (22f + FlightHeight * (1f - fallProgress * fallProgress));

		if (!Networking.IsHost)
		{
			return;
		}

		if (!CrateLanded && age >= LandingSeconds && Time.Now >= _landingRetryAt)
		{
			_landingRetryAt = Time.Now + 1f;
			var crate = GameSupplyCrate.Spawn(
				groundPosition: DropPosition,
				rifle: Rifle,
				shotgun: Shotgun,
				kind: GameCrateKind.Weapon
			);
			if (crate.IsValid())
			{
				CrateLanded = true;
				if (manager.IsValid())
				{
					manager.RecordRoundMoment("Weapon crate landed");
				}
			}
		}

		if (CrateLanded && age >= DepartureSeconds)
		{
			GameObject.Destroy();
		}
	}
}
