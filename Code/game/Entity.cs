using Sandbox;

public abstract class Entity : Component
{
	[Property, Range( 1f, 1000f )] public float MaxHealth { get; set; } = 100f;
	[Property, Range( 0.01f, 1f )] public float HitFlashDuration { get; set; } = 0.12f;
	[Sync( SyncFlags.FromHost )] public float Health { get; private set; } = 100f;

	private readonly Dictionary<ModelRenderer, Material> _originalMaterials = new();
	private Material _flashMaterial;
	private float _flashEndTime;

	protected override void OnStart()
	{
		if (Networking.IsHost)
			Health = MaxHealth;
	}

	public void TakeDamage(float amount)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Damage must be applied on the host.");
			return;
		}

		if (!float.IsFinite(amount) || amount <= 0f)
		{
			Log.Error("Damage must be a finite positive amount.");
			return;
		}

		Health = System.MathF.Max(0f, Health - amount);
		FlashHit();
	}

	protected void Punch<T>(PlayerController controller, float range, float damage) where T : Entity
	{
		T target = null;
		var closestDistanceSquared = range * range;
		foreach (var candidate in Scene.GetAllComponents<T>())
		{
			if (!candidate.Active || candidate == this)
				continue;

			var offset = candidate.WorldPosition - WorldPosition;
			var direction = new Vector3(offset.x, offset.y, 0f);
			if (offset.LengthSquared > closestDistanceSquared
				|| (direction.LengthSquared > 0.001f && direction.Normal.Dot(controller.EyeAngles.ToRotation().Forward) < 0.5f))
				continue;

			var trace = Scene.Trace
				.FromTo(WorldPosition + Vector3.Up * 36f, candidate.WorldPosition + Vector3.Up * 36f)
				.IgnoreGameObjectHierarchy(GameObject)
				.Run();
			if (trace.Hit && trace.GameObject.GetComponentInParent<Entity>() != candidate)
				continue;

			target = candidate;
			closestDistanceSquared = offset.LengthSquared;
		}

		if (target != null)
			target.TakeDamage(damage);
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void FlashHit()
	{
		_flashMaterial ??= Material.Create("entity_hit_flash", "shaders/hit_flash.shader");
		_flashEndTime = Time.Now + HitFlashDuration;
		ApplyHitFlash();
	}

	private void ApplyHitFlash()
	{
		foreach (var renderer in GetComponentsInChildren<ModelRenderer>())
		{
			if (!_originalMaterials.ContainsKey(renderer))
				_originalMaterials.Add(renderer, renderer.MaterialOverride);

			renderer.MaterialOverride = _flashMaterial;
		}
	}

	protected override void OnUpdate()
	{
		if (_originalMaterials.Count == 0)
			return;

		if (Time.Now < _flashEndTime)
			ApplyHitFlash();
		else
			RestoreMaterials();
	}

	private void RestoreMaterials()
	{
		foreach (var entry in _originalMaterials)
		{
			if (entry.Key.IsValid())
				entry.Key.MaterialOverride = entry.Value;
		}

		_originalMaterials.Clear();
	}

	protected override void OnDisabled()
	{
		RestoreMaterials();
	}
}
