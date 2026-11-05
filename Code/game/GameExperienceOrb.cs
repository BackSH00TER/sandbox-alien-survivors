using Sandbox;

public sealed class GameExperienceOrb : Component
{
	[Property, Range(1, 1000)] public int Amount { get; set; } = 1;

	private static readonly Color OrbColor = new Color(0.3f, 1f, 0.55f);
	private GameObject _visual;
	private float _bobOffset;

	public static void Spawn(Scene scene, Vector3 position, int amount)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Experience orbs must be spawned on the host.");
			return;
		}

		if (amount <= 0)
		{
			return;
		}

		var orb = new GameObject(true, "Experience Orb");
		orb.WorldPosition = position + Vector3.Up * 12f;
		orb.AddComponent<GameExperienceOrb>().Amount = amount;

		if (!orb.NetworkSpawn())
		{
			orb.Destroy();
			Log.Error("Failed to network-spawn experience orb.");
		}
	}

	protected override void OnStart()
	{
		_bobOffset = Game.Random.Float(0f, 10f);
		_visual = new GameObject(GameObject, true, "Visual");
		_visual.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		_visual.LocalScale = 0.12f;

		var renderer = _visual.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load("models/dev/sphere.vmdl");
		renderer.Tint = OrbColor;
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;

		var light = _visual.AddComponent<PointLight>();
		light.LightColor = OrbColor;
		light.Radius = 48f;
		light.Shadows = false;
	}

	protected override void OnUpdate()
	{
		if (!_visual.IsValid())
		{
			return;
		}

		_visual.LocalPosition = Vector3.Up * (System.MathF.Sin((Time.Now + _bobOffset) * 4f) * 3f);
	}

	protected override void OnFixedUpdate()
	{
		if (!Networking.IsHost || IsProxy)
		{
			return;
		}

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (gameManager != null && gameManager.State != GameState.InProgress)
		{
			return;
		}

		var target = GamePickupMagnet.Attract(pickup: this);
		if (target != null)
		{
			target.AddExperience(Amount);
			GameObject.Destroy();
		}
	}
}
