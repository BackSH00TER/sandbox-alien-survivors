using Sandbox;

/// <summary>An enemy variant that leaves a timed, damaging blast on its corpse.</summary>
public sealed class ExplodingEnemy : Component
{
	[Property, Range(0.1f, 5f), Description("Warning time after death, in seconds.")]
	public float FuseDuration { get; set; } = 3f;
	[Property, Range(1f, 1000f), Description("Blast radius in world units.")]
	public float BlastRadius { get; set; } = 100f;
	[Property, Range(1f, 1000f)] public float BlastDamage { get; set; } = 40f;

	private GameObject _beacon;

	protected override void OnStart()
	{
		_beacon = new GameObject(GameObject, true, "Exploder Beacon");
		_beacon.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		_beacon.LocalPosition = Vector3.Up * 82f;
		var renderer = _beacon.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load("models/dev/sphere.vmdl");
		_beacon.LocalScale = 14f / renderer.Model.Bounds.Size.x;
		renderer.Tint = new Color(1f, 0.3f, 0.02f);
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;

		var light = _beacon.AddComponent<PointLight>();
		light.LightColor = new Color(1f, 0.3f, 0.02f);
		light.Radius = 70f;
		light.Shadows = false;
	}

	public void ConfigureDeathExplosion(GameObject corpse)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Enemy death explosions must be configured on the host.");
			return;
		}

		var explosion = corpse.AddComponent<EnemyExplosion>();
		explosion.FuseDuration = FuseDuration;
		explosion.BlastRadius = BlastRadius;
		explosion.BlastDamage = BlastDamage;
	}

	protected override void OnDisabled()
	{
		if (_beacon.IsValid())
			_beacon.Destroy();
	}
}
