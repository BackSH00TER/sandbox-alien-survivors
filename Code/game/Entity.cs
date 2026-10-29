using Sandbox;

public abstract class Entity : Component
{
	[Property, Range(1f, 1000f), Sync(SyncFlags.FromHost)] public float MaxHealth { get; set; } = 100f;
	[Property, Range(0.01f, 1f)] public float HitFlashDuration { get; set; } = 0.12f;
	[Sync(SyncFlags.FromHost)] public float Health { get; private set; } = 100f;
	[Property, Group("Debug")] public bool ShowAttackHitboxes { get; set; } = true;
	public virtual bool CanTakeDamage => true;

	private const float PunchFacingThreshold = 0.5f;
	private const float PunchTraceHeight = 36f;
	private readonly Dictionary<ModelRenderer, Material> _originalMaterials = new();
	private Material _flashMaterial;
	private float _flashEndTime;
	internal bool IsHitFlashing => Time.Now < _flashEndTime;

	protected override void OnStart()
	{
		var blood = GameObject.GetOrAddComponent<BloodiedModel>();
		blood.Flags |= ComponentFlags.NotNetworked | ComponentFlags.NotSaved;
		if (Networking.IsHost)
			Health = MaxHealth;
	}

	public void TakeDamage(float amount, Vector3 hitDirection = default)
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

		if (!CanTakeDamage)
			return;

		var previousHealth = Health;
		Health = System.MathF.Max(0f, Health - amount);
		FlashHit();
		OnDamaged(hitDirection);
		if (previousHealth > 0f && Health <= 0f)
			OnHealthDepleted();
	}

	protected virtual void OnHealthDepleted()
	{
	}

	public bool RestoreHealth(float amount)
	{
		if (!Networking.IsHost || !float.IsFinite(amount) || amount <= 0f)
		{
			Log.Error("Healing requires a finite positive amount on the host.");
			return false;
		}

		if (Health <= 0f || Health >= MaxHealth)
			return false;

		Health = System.MathF.Min(MaxHealth, Health + amount);
		return true;
	}

	/// <summary>Host only. Brings a dead entity back with the given health. Does nothing if it is already alive.</summary>
	public bool Revive(float health)
	{
		if (!Networking.IsHost || !float.IsFinite(health) || health <= 0f)
		{
			Log.Error("Reviving requires a finite positive amount on the host.");
			return false;
		}

		if (Health > 0f)
			return false;

		Health = System.MathF.Min(MaxHealth, health);
		return true;
	}

	protected void IncreaseMaxHealth(float amount)
	{
		if (!Networking.IsHost || !float.IsFinite(amount) || amount <= 0f)
		{
			Log.Error("Maximum health increases require a finite positive amount on the host.");
			return;
		}

		MaxHealth += amount;
		if (Health > 0f)
			Health = System.MathF.Min(MaxHealth, Health + amount);
	}

	protected virtual void OnDamaged(Vector3 hitDirection)
	{
	}

	protected void Punch<T>(PlayerController controller, float range, float damage) where T : Entity
	{
		T target = null;
		var closestDistanceSquared = range * range;
		foreach (var candidate in Scene.GetAllComponents<T>())
		{
			if (!candidate.Active || !candidate.CanTakeDamage || candidate == this)
				continue;

			var offset = candidate.WorldPosition - WorldPosition;
			var direction = new Vector3(offset.x, offset.y, 0f);
			if (offset.LengthSquared > closestDistanceSquared
				|| (direction.LengthSquared > 0.001f && direction.Normal.Dot(controller.EyeAngles.ToRotation().Forward) < PunchFacingThreshold))
				continue;

			var trace = Scene.Trace
				.FromTo(WorldPosition + Vector3.Up * PunchTraceHeight, candidate.WorldPosition + Vector3.Up * PunchTraceHeight)
				.IgnoreGameObjectHierarchy(GameObject)
				.Run();
			if (trace.Hit && trace.GameObject.GetComponentInParent<Entity>() != candidate)
				continue;

			target = candidate;
			closestDistanceSquared = offset.LengthSquared;
		}

		if (ShowAttackHitboxes)
			DrawPunchHitbox(WorldPosition, controller.EyeAngles.ToRotation().Forward, range,
				target != null, target?.WorldPosition ?? WorldPosition);

		if (target != null)
			target.TakeDamage(damage, (target.WorldPosition - WorldPosition).Normal);
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	private void DrawPunchHitbox(Vector3 origin, Vector3 forward, float range, bool hit, Vector3 targetPosition)
	{
		if (!ShowAttackHitboxes || !GameHUD.IsDebugEnabled(Scene))
			return;

		const float duration = 0.25f;
		const int segments = 24;
		var color = hit ? Color.Green : Color.Red;
		var planarForward = new Vector3(forward.x, forward.y, 0f);
		var planarLength = planarForward.Length;
		if (planarLength >= PunchFacingThreshold)
		{
			// Punch uses 3D distance but a horizontal facing test.
			var halfAngle = System.MathF.Acos((PunchFacingThreshold / planarLength).Clamp(0f, 1f));
			var yaw = System.MathF.Atan2(forward.y, forward.x);
			for (var elevationIndex = -1; elevationIndex <= 1; elevationIndex++)
			{
				var elevation = elevationIndex * System.MathF.PI / 4f;
				var radius = range * System.MathF.Cos(elevation);
				var height = Vector3.Up * (range * System.MathF.Sin(elevation));
				var previous = origin;
				for (var i = 0; i <= segments; i++)
				{
					var angle = yaw - halfAngle + 2f * halfAngle * i / segments;
					var point = origin + height + new Vector3(System.MathF.Cos(angle), System.MathF.Sin(angle), 0f) * radius;
					if (i > 0)
						DebugOverlay.Line(previous, point, color, duration);
					if (i == 0 || i == segments)
						DebugOverlay.Line(origin, point, color, duration);
					previous = point;
				}
			}

			for (var side = -1; side <= 1; side++)
			{
				var angle = yaw + side * halfAngle;
				var direction = new Vector3(System.MathF.Cos(angle), System.MathF.Sin(angle), 0f);
				var previous = origin - Vector3.Up * range;
				for (var i = 1; i <= segments; i++)
				{
					var elevation = -System.MathF.PI / 2f + System.MathF.PI * i / segments;
					var point = origin + range * (direction * System.MathF.Cos(elevation) + Vector3.Up * System.MathF.Sin(elevation));
					DebugOverlay.Line(previous, point, color, duration);
					previous = point;
				}
			}
		}

		if (hit)
			DebugOverlay.Line(origin + Vector3.Up * PunchTraceHeight,
				targetPosition + Vector3.Up * PunchTraceHeight, Color.Yellow, duration);
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
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

	protected void RestoreMaterials()
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
