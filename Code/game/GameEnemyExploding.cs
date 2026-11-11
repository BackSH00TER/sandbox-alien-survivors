using Sandbox;

/// <summary>An enemy variant that leaves a timed, damaging blast on its corpse.</summary>
public sealed class GameEnemyExploding : Component
{
	[Property, Range(0.1f, 5f), Description("Warning time after death, in seconds.")] public float FuseDuration { get; set; } = DamageEffectCatalog.DefaultExplosionFuseDuration;
	[Property, Range(1f, 1000f), Description("Blast radius in world units.")] public float BlastRadius { get; set; } = DamageEffectCatalog.DefaultExplosionBlastRadius;
	[Property, Range(1f, 1000f)] public float BlastDamage { get; set; } = DamageEffectCatalog.DefaultExplosionBlastDamage;

	public void ConfigureDeathExplosion(GameObject corpse)
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
}
