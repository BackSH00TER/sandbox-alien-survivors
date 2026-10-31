using Sandbox;

/// <summary>Local-only placeholder flames, shown from the enemy's synchronized fire stack count.</summary>
public sealed class BurningVisual : Component
{
	private const int FlameCount = 5;
	private const float FlameRadius = 14f;
	private Enemy _enemy;
	private GameObject _visualRoot;
	private readonly List<LineRenderer> _flames = new();

	protected override void OnStart()
	{
		_enemy = GetComponent<Enemy>();
		if (!_enemy.IsValid())
		{
			Log.Error("BurningVisual requires an Enemy component.");
			Enabled = false;
		}
	}

	protected override void OnUpdate()
	{
		var isBurning = _enemy.IsValid() && _enemy.Active && _enemy.Health > 0f
			&& _enemy.EffectStackCount(DamageType.Fire) > 0;
		if (isBurning && !_visualRoot.IsValid())
			BuildFlames();
		if (_visualRoot.IsValid() && _visualRoot.Enabled != isBurning)
			_visualRoot.Enabled = isBurning;
		if (!isBurning)
			return;

		UpdateFlames();
	}

	private void BuildFlames()
	{
		_flames.Clear();
		_visualRoot = new GameObject(GameObject, true, "Burning Visual");
		_visualRoot.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;

		// Line renderers stay separate from the model's blood and hit-flash material passes.
		for (int flameIndex = 0; flameIndex < FlameCount; flameIndex++)
		{
			var flameObject = new GameObject(_visualRoot, true, $"Flame {flameIndex + 1}");
			flameObject.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
			var flame = flameObject.AddComponent<LineRenderer>();
			flame.UseVectorPoints = true;
			flame.VectorPoints = new List<Vector3> { Vector3.Zero, Vector3.Zero, Vector3.Zero };
			flame.Face = SceneLineObject.FaceMode.Cylinder;
			flame.CylinderSegments = 6;
			flame.Opaque = false;
			flame.Additive = true;
			flame.CastShadows = false;
			flame.Lighting = false;
			_flames.Add(flame);
		}
	}

	private void UpdateFlames()
	{
		for (int flameIndex = 0; flameIndex < _flames.Count; flameIndex++)
		{
			var flame = _flames[flameIndex];
			var angle = flameIndex * (2f * System.MathF.PI / FlameCount);
			var outward = new Vector3(System.MathF.Cos(angle), System.MathF.Sin(angle), 0f);
			var rise = (Time.Now * 1.6f + (float)flameIndex / FlameCount) % 1f;
			var flicker = System.MathF.Sin(Time.Now * 11f + flameIndex * 2.7f);
			var basePosition = WorldPosition + outward * FlameRadius + Vector3.Up * (4f + rise * 24f);

			flame.VectorPoints[0] = basePosition;
			flame.VectorPoints[1] = basePosition + Vector3.Up * 14f + outward * (flicker * 3f);
			flame.VectorPoints[2] = basePosition + Vector3.Up * (28f + flicker * 6f) - outward * 2f;
			flame.Width = (8f + flicker * 1.5f) * (1f - rise * 0.4f);
			flame.Color = new Color(1f, 0.25f + rise * 0.5f, 0.03f, 0.85f * (1f - rise));
		}
	}

	protected override void OnDisabled()
	{
		if (_visualRoot.IsValid())
			_visualRoot.Destroy();
		_flames.Clear();
	}
}
