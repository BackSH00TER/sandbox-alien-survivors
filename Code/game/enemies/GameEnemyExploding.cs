using Sandbox;

/// <summary>An enemy variant that leaves a timed, damaging blast on its corpse.</summary>
public sealed class GameEnemyExploding : GameEnemy
{
	public override GameEnemyKind Kind => GameEnemyKind.Exploding;
	[Property, Range(0.1f, 5f), Description("Warning time after death, in seconds.")] public float FuseDuration { get; set; } = DamageEffectCatalog.DefaultExplosionFuseDuration;
	[Property, Range(1f, 1000f), Description("Blast radius in world units.")] public float BlastRadius { get; set; } = DamageEffectCatalog.DefaultExplosionBlastRadius;
	[Property, Range(1f, 1000f)] public float BlastDamage { get; set; } = DamageEffectCatalog.DefaultExplosionBlastDamage;

	private GameObject _beacon;

	protected override void OnStart()
	{
		base.OnStart();
		_beacon = new GameObject(
			parent: GameObject,
			enabled: true,
			name: "Exploder Beacon"
		);
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

	protected override void ConfigureCorpse(GameObject corpse)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Enemy death explosions must be configured on the host.");
			return;
		}

		var definition = DamageEffectCatalog.Get(GameDamageType.Explosive);
		var settings = new DamageEffectSettings(
			type: definition.Type,
			applicationChance: definition.ApplicationChance,
			duration: FuseDuration,
			damagePerSecond: 0f,
			maxStacks: 1,
			spreadChance: 0f,
			spreadRadius: 0f,
			instantDamage: BlastDamage,
			blastRadius: BlastRadius
		);
		GameExplosionEffect.Apply(
			origin: corpse,
			settings: settings,
			destroyOriginAfterExplosion: true
		);
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if (_beacon.IsValid())
		{
			_beacon.Destroy();
		}
	}
}
