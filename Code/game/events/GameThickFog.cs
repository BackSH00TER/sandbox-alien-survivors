using Sandbox;

/// <summary>Host-owned weather clock; each peer builds temporary rolling fog strata and volumes.</summary>
public sealed class GameThickFog : Component
{
	private const int BanksPerAxis = 6;
	private const int BankCount = BanksPerAxis * BanksPerAxis;

	[Sync(SyncFlags.FromHost)] private TimeSince FogClock { get; set; }
	[Sync(SyncFlags.FromHost)] private float PeakStrength { get; set; }
	[Sync(SyncFlags.FromHost)] private Vector3 ArenaHalfSize { get; set; }

	private GameObject _visuals;
	private Fog _heightFog;
	private readonly List<VolumetricFogVolume> _volumes = new();
	private readonly List<ModelRenderer> _layers = new();
	private Material _layerMaterial;

	public static GameThickFog Spawn(BBox arenaBounds, float strength)
	{
		if (!Networking.IsHost || !float.IsFinite(strength) || strength <= 0f || strength > 5f)
		{
			Log.Error("Thick fog requires the host and a finite strength greater than zero, up to five.");
			return null;
		}

		var arenaHalfSize = arenaBounds.Size * 0.5f;
		if (!arenaHalfSize.IsFinite || arenaHalfSize.x <= 0f || arenaHalfSize.y <= 0f)
		{
			Log.Error("Thick fog requires finite arena bounds with positive horizontal dimensions.");
			return null;
		}

		var obj = new GameObject(enabled: true, name: "Thick Fog Weather");
		obj.WorldPosition = new Vector3(
			x: arenaBounds.Center.x,
			y: arenaBounds.Center.y,
			z: arenaBounds.Maxs.z
		);
		var fog = obj.AddComponent<GameThickFog>();
		fog.FogClock = 0f;
		fog.PeakStrength = strength;
		fog.ArenaHalfSize = arenaHalfSize;
		if (!obj.NetworkSpawn())
		{
			obj.Destroy();
			Log.Error("Failed to network-spawn thick fog weather.");
			return null;
		}

		return fog;
	}

	protected override void OnUpdate()
	{
		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		float age = FogClock;
		if (!manager.IsValid() || manager.State != GameState.InProgress || age >= GameFogTiming.DurationSeconds)
		{
			RemoveVisuals();
			if (Networking.IsHost)
			{
				if (manager.IsValid() && manager.State == GameState.InProgress)
				{
					manager.RecordRoundMoment("Thick fog cleared");
				}
				GameObject.Destroy();
			}
			return;
		}

		EnsureVisuals();
		var strength = GameFogTiming.StrengthAt(elapsedSeconds: age);
		var heightOpacity = (PeakStrength * 0.12f).Clamp(0f, 0.3f) * strength;
		_heightFog.HeightColor = new Color(
			r: 0.82f,
			g: 0.88f,
			b: 0.92f,
			a: heightOpacity
		);
		for (var layer = 0; layer < _layers.Count; layer++)
		{
			var renderer = _layers[layer];
			var height = GameFogDrift.LayerHeightAt(layer: layer, elapsedSeconds: age);
			renderer.LocalPosition = Vector3.Up * height;
			var opacity = (PeakStrength * (0.5f - layer * 0.075f)).Clamp(0f, 0.7f) * strength;
			renderer.Attributes.Set("FogAge", age);
			renderer.Attributes.Set("FogOpacity", opacity);
		}
		var halfWidth = ArenaHalfSize.x + GameFogDrift.EdgePadding;
		var spacingX = halfWidth * 2f / BanksPerAxis;
		var spacingY = ArenaHalfSize.y * 2f / BanksPerAxis;
		var centerIndex = (BanksPerAxis - 1) * 0.5f;
		for (var index = 0; index < _volumes.Count; index++)
		{
			var column = index % BanksPerAxis;
			var row = index / BanksPerAxis;
			var initialX = (column - centerIndex) * spacingX + row % 2 * spacingX * 0.5f;
			var x = GameFogDrift.PositionAt(
				initialPosition: initialX,
				elapsedSeconds: age,
				halfWidth: halfWidth
			);
			var y = (row - centerIndex) * spacingY + System.MathF.Sin(index * 1.7f) * spacingY * 0.2f;
			var height = GameFogDrift.LayerHeightAt(layer: index % GameFogDrift.LayerCount, elapsedSeconds: age);
			var position = new Vector3(
				x: x,
				y: y,
				z: height
			);
			var volume = _volumes[index];
			volume.LocalPosition = position;
			// Fade banks at the arena boundary before wrapping them back upwind.
			var edgeDistance = halfWidth - System.MathF.Abs(x);
			var edgeFade = (edgeDistance / GameFogDrift.EdgePadding).Clamp(0f, 1f);
			var density = 0.08f + index % 3 * 0.02f;
			volume.Strength = PeakStrength * strength * edgeFade * density;
		}
		_visuals.Enabled = true;
	}

	private void EnsureVisuals()
	{
		if (_visuals.IsValid() && _heightFog.IsValid()
			&& _volumes.Count == BankCount && _volumes.All(volume => volume.IsValid())
			&& _layers.Count == GameFogDrift.LayerCount && _layers.All(layer => layer.IsValid()))
		{
			return;
		}

		RemoveVisuals();
		_visuals = new GameObject(
			parent: GameObject,
			enabled: false,
			name: "Local Weather Fog"
		);
		_visuals.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		_heightFog = _visuals.AddComponent<Fog>();
		_heightFog.FogEnabled = true;
		_heightFog.DistanceEnabled = false;
		_heightFog.HeightEnabled = true;
		_heightFog.HeightOffset = 8f;
		_heightFog.HeightGradientLength = 80f;
		_heightFog.HeightExponent = 2f;
		_heightFog.HeightColorSource = FogColorSource.Constant;
		_heightFog.HeightColor = new Color(
			r: 0.82f,
			g: 0.88f,
			b: 0.92f,
			a: 0f
		);
		_heightFog.SkyAffect = 0f;
		_layerMaterial ??= Material.Create("rolling_fog", "shaders/rolling_fog.shader");
		var plane = Model.Load("models/dev/plane.vmdl");
		var planeSize = plane.Bounds.Size;
		var halfSize = ArenaHalfSize + Vector3.One * GameFogDrift.EdgePadding;
		for (var layer = 0; layer < GameFogDrift.LayerCount; layer++)
		{
			var obj = new GameObject(
				parent: _visuals,
				enabled: true,
				name: $"Rolling Fog Layer {layer + 1}"
			);
			obj.LocalScale = new Vector3(
				x: halfSize.x * 2f / planeSize.x,
				y: halfSize.y * 2f / planeSize.y,
				z: 1f
			);
			var renderer = obj.AddComponent<ModelRenderer>();
			renderer.Model = plane;
			renderer.MaterialOverride = _layerMaterial;
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
			var wind = GameFogDrift.LayerWindAt(layer);
			var layerSettings = new Vector4(
				x: wind.x,
				y: wind.y,
				z: 100f + layer * 35f,
				w: layer
			);
			var bounds = new Vector4(
				x: WorldPosition.x,
				y: WorldPosition.y,
				z: halfSize.x,
				w: halfSize.y
			);
			renderer.Attributes.Set("FogLayer", layerSettings);
			renderer.Attributes.Set("FogBounds", bounds);
			_layers.Add(renderer);
		}
		for (var index = 0; index < BankCount; index++)
		{
			var bank = new GameObject(
				parent: _visuals,
				enabled: true,
				name: $"Drifting Fog Bank {index + 1}"
			);
			var volume = bank.AddComponent<VolumetricFogVolume>();
			var bankHalfSize = new Vector3(
				x: 220f + index % 3 * 50f,
				y: 120f + index % 2 * 60f,
				z: 35f + index % GameFogDrift.LayerCount * 10f
			);
			volume.Bounds = new BBox(-bankHalfSize, bankHalfSize);
			volume.FalloffExponent = 0.6f;
			volume.Color = new Color(
				r: 0.82f,
				g: 0.88f,
				b: 0.92f
			);
			volume.Strength = 0f;
			_volumes.Add(volume);
		}
	}

	protected override void OnDisabled()
	{
		RemoveVisuals();
	}

	protected override void OnDestroy()
	{
		RemoveVisuals();
	}

	private void RemoveVisuals()
	{
		if (_visuals.IsValid())
		{
			_visuals.Destroy();
		}
		_visuals = null;
		_heightFog = null;
		_volumes.Clear();
		_layers.Clear();
	}
}
