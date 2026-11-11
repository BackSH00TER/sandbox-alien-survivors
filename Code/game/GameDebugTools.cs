using Sandbox;

internal static class GameDebugTools
{
	public static void Kill(GamePlayer player)
	{
		if (player.IsDead)
		{
			return;
		}

		// Killing on purpose overrides god mode.
		player.GodMode = false;
		player.TakeDamage(
			amount: player.Health,
			bypassArmor: true
		);
	}

	public static void ToggleGodMode(GamePlayer player)
	{
		player.GodMode = !player.GodMode;
	}

	public static void ToggleAttackHitboxes(GamePlayer player)
	{
		player.ShowAttackHitboxes = !player.ShowAttackHitboxes;
	}

	public static void GrantModifier(GamePlayer player, GameModifier modifier)
	{
		var definition = ModifierCatalog.All.FirstOrDefault(candidate => candidate.Id == modifier);
		if (definition == null)
		{
			Log.Warning($"Rejected an unknown debug modifier: {modifier}.");
			return;
		}

		if (player.ModifierStacks(modifier) >= definition.MaxPicks)
		{
			Log.Warning($"Debug modifier '{modifier}' is already at its maximum stack count.");
			return;
		}

		var granting = new HashSet<GameModifier>();
		foreach (var prerequisite in definition.Prerequisites)
		{
			if (!GrantPrerequisite(player, prerequisite, granting))
			{
				return;
			}
		}

		definition.ApplyTo(player);
	}

	public static void SpawnFlarePickup(GamePlayer player)
	{
		var position = player.WorldPosition + player.Controller.EyeAngles.ToRotation().Forward * 80f;
		var definition = GameLootCatalog.Get(LootKind.Flare);
		GameLootPickup.Spawn(
			groundPosition: position,
			definition: definition
		);
	}

	public static void SpawnArmorPickup(GamePlayer player)
	{
		if (TryGetSpawnPosition(player, out var position))
		{
			var definition = GameLootCatalog.Get(LootKind.Armor);
			GameLootPickup.Spawn(
				groundPosition: position,
				definition: definition
			);
		}
	}

	public static void SpawnCrate(GamePlayer player)
	{
		if (!TryGetSpawnPosition(player, out var position))
		{
			return;
		}

		var spawner = player.Scene.GetAllComponents<GameCrateSpawner>().FirstOrDefault();
		var rifle = spawner?.Rifle ?? ResourceLibrary.Get<GameWeaponDefinition>("weapons/rifle.weapon");
		var shotgun = spawner?.Shotgun ?? ResourceLibrary.Get<GameWeaponDefinition>("weapons/shotgun.weapon");
		if (rifle == null || shotgun == null)
		{
			Log.Error("Debug crates require rifle and shotgun weapon resources.");
			return;
		}

		if (!rifle.Validate() || !shotgun.Validate())
		{
			return;
		}

		GameSupplyCrate.Spawn(
			groundPosition: position,
			rifle: rifle,
			shotgun: shotgun
		);
	}

	public static void DrawAttackRange(GamePlayer player)
	{
		if (!GameDebugPanel.IsEnabled)
		{
			return;
		}

		const int segments = 64;
		var center = player.WorldPosition + Vector3.Up * 2f;
		var previous = center + Vector3.Forward * player.EffectiveAttackRange;
		for (var i = 1; i <= segments; i++)
		{
			var angle = i * (System.MathF.PI * 2f / segments);
			var point = center + new Vector3(
				System.MathF.Cos(angle),
				System.MathF.Sin(angle),
				0f
			) * player.EffectiveAttackRange;
			player.DebugOverlay.Line(
				from: previous,
				to: point,
				color: Color.Yellow,
				duration: 0f
			);
			previous = point;
		}
	}

	public static void DrawPunchHitbox(
		GameEntity entity,
		Vector3 origin,
		Vector3 forward,
		float range,
		bool hit,
		Vector3 targetPosition
	)
	{
		if (!GameDebugPanel.IsEnabled)
		{
			return;
		}

		const float duration = 0.25f;
		const int segments = 24;
		var color = hit ? Color.Green : Color.Red;
		var planarForward = new Vector3(
			forward.x,
			forward.y,
			0f
		);
		var planarLength = planarForward.Length;
		// Punch uses 3D distance but a horizontal facing test.
		if (planarLength >= GameEntity.PunchFacingThreshold)
		{
			var halfAngle = System.MathF.Acos((GameEntity.PunchFacingThreshold / planarLength).Clamp(0f, 1f));
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
					var point = origin + height + new Vector3(
						System.MathF.Cos(angle),
						System.MathF.Sin(angle),
						0f
					) * radius;
					if (i > 0)
					{
						entity.DebugOverlay.Line(
							from: previous,
							to: point,
							color: color,
							duration: duration
						);
					}

					if (i == 0 || i == segments)
					{
						entity.DebugOverlay.Line(
							from: origin,
							to: point,
							color: color,
							duration: duration
						);
					}

					previous = point;
				}
			}

			for (var side = -1; side <= 1; side++)
			{
				var angle = yaw + side * halfAngle;
				var direction = new Vector3(
					System.MathF.Cos(angle),
					System.MathF.Sin(angle),
					0f
				);
				var previous = origin - Vector3.Up * range;
				for (var i = 1; i <= segments; i++)
				{
					var elevation = -System.MathF.PI / 2f + System.MathF.PI * i / segments;
					var point = origin + range * (direction * System.MathF.Cos(elevation) + Vector3.Up * System.MathF.Sin(elevation));
					entity.DebugOverlay.Line(
						from: previous,
						to: point,
						color: color,
						duration: duration
					);
					previous = point;
				}
			}
		}

		if (hit)
		{
			entity.DebugOverlay.Line(
				from: origin + Vector3.Up * GameEntity.PunchTraceHeight,
				to: targetPosition + Vector3.Up * GameEntity.PunchTraceHeight,
				color: Color.Yellow,
				duration: duration
			);
		}
	}

	public static void DrawProjectileHitbox(
		GameWeaponProjectile projectile,
		Vector3 position,
		float radius
	)
	{
		if (!GameDebugPanel.IsEnabled)
		{
			return;
		}

		projectile.DebugOverlay.Sphere(
			sphere: new Sphere(position, radius),
			color: Color.Yellow,
			duration: 0f
		);
	}

	public static void DrawProjectileTravel(
		GameWeaponProjectile projectile,
		Vector3 start,
		Vector3 end,
		Color color,
		float radius
	)
	{
		if (!GameDebugPanel.IsEnabled)
		{
			return;
		}

		projectile.DebugOverlay.Line(
			from: start,
			to: end,
			color: color,
			duration: 0.25f
		);
		projectile.DebugOverlay.Sphere(
			sphere: new Sphere(end, radius),
			color: color,
			duration: 0.25f
		);
	}

	private static bool TryGetSpawnPosition(GamePlayer player, out Vector3 position)
	{
		position = default;
		if (player.IsDead || player.IsRoundOver || !GameDebugPanel.IsEnabled)
		{
			return false;
		}

		if (!player.Controller.IsValid())
		{
			Log.Error("Debug spawning requires a player controller.");
			return false;
		}

		var yawRotation = Rotation.FromYaw(player.Controller.EyeAngles.yaw);
		var candidate = player.WorldPosition + yawRotation.Forward * 120f;
		var ground = player.Scene.Trace
			.FromTo(candidate + Vector3.Up * 64f, candidate - Vector3.Up * 256f)
			.IgnoreGameObjectHierarchy(player.GameObject)
			.IgnoreDynamic()
			.Run();
		if (!ground.Hit || ground.StartedSolid || ground.Normal.z < 0.9f
			|| (ground.GameObject.IsValid() && ground.GameObject.GetComponentInParent<GameEntity>().IsValid()))
		{
			Log.Warning("Cannot debug-spawn here: no suitable ground in front of the player.");
			return false;
		}

		var bounds = new BBox(
			new Vector3(
				-21f,
				-21f,
				1f
			),
			new Vector3(
				21f,
				21f,
				46f
			)
		);
		var clearance = player.Scene.Trace
			.Box(bounds, ground.EndPosition, ground.EndPosition)
			.IgnoreGameObjectHierarchy(player.GameObject)
			.Run();
		if (clearance.Hit || clearance.StartedSolid)
		{
			Log.Warning("Cannot debug-spawn here: the space in front of the player is blocked.");
			return false;
		}

		position = ground.EndPosition;
		return true;
	}

	private static bool GrantPrerequisite(
		GamePlayer player,
		GameModifier modifier,
		HashSet<GameModifier> granting
	)
	{
		if (player.ModifierStacks(modifier) > 0)
		{
			return true;
		}

		if (!granting.Add(modifier))
		{
			Log.Error($"Modifier prerequisite cycle detected while granting '{modifier}'.");
			return false;
		}

		var definition = ModifierCatalog.Get(modifier);
		foreach (var prerequisite in definition.Prerequisites)
		{
			if (!GrantPrerequisite(player, prerequisite, granting))
			{
				return false;
			}
		}

		definition.ApplyTo(player);
		granting.Remove(modifier);
		return true;
	}
}
