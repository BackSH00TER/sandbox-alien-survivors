using Sandbox;

public sealed class GameExperienceOrb : Component
{
	[Property, Sync(SyncFlags.FromHost), Range(1, int.MaxValue)] public int Amount { get; set; } = 1;

	internal bool IsMerging;
	internal bool Consumed => _consumed;
	private bool _consumed;
	private GameObject _visual;
	private ModelRenderer _renderer;
	private PointLight _light;
	private int _visualAmount;
	private float _bobOffset;

	/// <summary>Shared drop entry point. Any source supplies its scene, ground position, and XP value; appearance depends only on that value.</summary>
	public static void Spawn(
		Scene scene,
		Vector3 position,
		int amount
	)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Experience orbs must be spawned on the host.");
			return;
		}

		if (scene == null)
		{
			Log.Error("Experience orbs require a destination scene.");
			return;
		}

		if (amount <= 0)
		{
			Log.Error("Experience orb amounts must be positive.");
			return;
		}

		var orb = new GameObject(
			parent: scene,
			enabled: true,
			name: "Experience Orb"
		);
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

		_renderer = _visual.AddComponent<ModelRenderer>();
		_renderer.Model = Model.Load("models/dev/sphere.vmdl");
		_renderer.RenderType = ModelRenderer.ShadowRenderType.Off;

		_light = _visual.AddComponent<PointLight>();
		_light.Radius = 48f;
		_light.Shadows = false;
		UpdateAppearance();
	}

	private void UpdateAppearance()
	{
		if (_visualAmount == Amount)
		{
			return;
		}

		var color = ExperienceOrbRules.GetColor(Amount);
		_visual.LocalScale = ExperienceOrbRules.GetScale(Amount);
		_renderer.Tint = color;
		_light.LightColor = color;
		_visualAmount = Amount;
	}

	protected override void OnUpdate()
	{
		if (!_visual.IsValid())
		{
			return;
		}

		UpdateAppearance();
		_visual.LocalPosition = Vector3.Up * (System.MathF.Sin((Time.Now + _bobOffset) * 4f) * 3f);
	}

	protected override void OnFixedUpdate()
	{
		if (!Networking.IsHost || IsProxy || IsMerging || Consumed)
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
			_consumed = true;
			target.AddExperience(Amount);
			GameObject.Destroy();
		}
	}

	internal void Consume()
	{
		_consumed = true;
		GameObject.Destroy();
	}
}
