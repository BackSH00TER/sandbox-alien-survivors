using Sandbox;

/// <summary>
/// Red flare fired straight up when an alive player walks over a flare pickup. It rises to <see cref="HangHeight"/>,
/// glows for <see cref="LifetimeSeconds"/>, dims over the final <see cref="DimSeconds"/>, then burns out.
/// </summary>
public sealed class GameReviveFlare : Component
{
    [Property, Range(10f, 600f)] public float LifetimeSeconds { get; set; } = 120f;
    [Property, Range(1f, 120f), Description("Seconds before burnout that the flare starts dimming and then blinks.")]
    public float DimSeconds { get; set; } = 30f;
    [Property, Range(50f, 2000f), Description("Height above the launch point the flare hangs at. Kept below the dead player's UAV altitude so the flare shows under the drone.")]
    public float HangHeight { get; set; } = 400f;
    [Property, Range(100f, 5000f)] public float RiseSpeed { get; set; } = 900f;
    [Property, Range(10f, 500f), Description("How close sideways a dead player's UAV must get to the flare to claim it.")]
    public float ClaimRadius { get; set; } = 90f;
    [Property, Range(0f, 1000f), Description("How far above or below the flare the UAV can be and still claim it.")]
    public float ClaimHeightTolerance { get; set; } = 300f;
    [Property, Group("Light"), Range(100f, 2000f), Description("How far the flare's red light reaches at full strength.")]
    public float LightRadius { get; set; } = 1000f;
    [Property, Group("Light"), Range(0.1f, 10f), Description("Brightness multiplier of the flare's red light.")]
    public float LightIntensity { get; set; } = 7.0f;

    /// <summary>Seconds until the flare burns out. Counted down by the host and synced to everyone.</summary>
    [Sync(SyncFlags.FromHost)] public float Remaining { get; private set; }
    [Sync(SyncFlags.FromHost)] public Vector3 GroundPosition { get; private set; }

    private const float BlinkSeconds = 8f;
    private const float GlowScale = 0.4f; // sphere model scale of the glowing flare head
    private static readonly Color FlareColor = new Color(1f, 0.15f, 0.1f);
    private ModelRenderer _glow;
    private PointLight _light;
    private LineRenderer _beam;
    private float _flickerSeed;

    /// <summary>Host only. Fires a flare straight up from the given ground position.</summary>
    public static GameReviveFlare Spawn(Vector3 groundPosition)
    {
        if (!Networking.IsHost)
        {
            Log.Error("Revive flares must be spawned on the host.");
            return null;
        }

        var obj = new GameObject(true, "Revive Flare");
        obj.WorldPosition = groundPosition + Vector3.Up * 40f;
        var flare = obj.AddComponent<GameReviveFlare>();
        flare.GroundPosition = groundPosition;
        flare.Remaining = flare.LifetimeSeconds;
        if (!obj.NetworkSpawn())
        {
            obj.Destroy();
            Log.Error("Failed to network-spawn revive flare.");
            return null;
        }

        return flare;
    }

    /// <summary>1 while the flare is at full strength, fading toward 0.1 as it burns out.</summary>
    public float Brightness => Remaining >= DimSeconds ? 1f : (Remaining / DimSeconds).Clamp(0.1f, 1f);

    protected override void OnStart()
    {
        _flickerSeed = Game.Random.Float(0f, 100f);
        var visual = new GameObject(GameObject, true, "Flare Visual");
        visual.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
        visual.LocalScale = GlowScale;
        _glow = visual.AddComponent<ModelRenderer>();
        _glow.Model = Model.Load("models/dev/sphere.vmdl");
        _glow.RenderType = ModelRenderer.ShadowRenderType.Off;

        _light = visual.AddComponent<PointLight>();
        _light.Shadows = false;

        _beam = GameObject.AddComponent<LineRenderer>();
        _beam.Flags |= ComponentFlags.NotNetworked | ComponentFlags.NotSaved;
        _beam.UseVectorPoints = true;
        _beam.VectorPoints = new List<Vector3> { Vector3.Zero, Vector3.Zero };
        _beam.Width = 8f;
        _beam.Face = SceneLineObject.FaceMode.Cylinder;
        _beam.CylinderSegments = 12;
        _beam.Opaque = false;
        _beam.Additive = true;
        _beam.CastShadows = false;
        _beam.Lighting = false;
    }

    // Layered sine waves at unrelated speeds give an uneven, crackling flicker instead of a steady pulse.
    private float Flicker()
    {
        var t = Time.Now + _flickerSeed;
        var flicker = 0.8f + 0.12f * System.MathF.Sin(t * 23f) + 0.08f * System.MathF.Sin(t * 37.3f + 1.7f)
            + 0.06f * System.MathF.Sin(t * 61f + 4.2f);
        return flicker.Clamp(0.5f, 1.15f);
    }

    protected override void OnUpdate()
    {
        var brightness = Brightness;
        var flicker = Flicker();
        var visible = Remaining > BlinkSeconds || (int)(Time.Now * 6f) % 2 == 0;
        var pulse = 1f + System.MathF.Sin(Time.Now * 8f) * 0.08f;

        if (_glow.IsValid())
        {
            _glow.Enabled = visible;
            _glow.GameObject.LocalScale = GlowScale * pulse;
            _glow.Tint = FlareColor * brightness * flicker;
        }

        if (_light.IsValid())
        {
            _light.Enabled = visible;
            _light.LightColor = FlareColor * (brightness * flicker * LightIntensity);
            _light.Radius = LightRadius * brightness * (0.85f + 0.15f * flicker);
        }

        if (_beam.IsValid())
        {
            _beam.VectorPoints[0] = GroundPosition + Vector3.Up * 4f;
            _beam.VectorPoints[1] = WorldPosition;
            _beam.Color = FlareColor.WithAlpha(0.35f * brightness);
        }
    }

    protected override void OnFixedUpdate()
    {
        if (!Networking.IsHost || IsProxy)
            return;

        var height = System.MathF.Min(GroundPosition.z + HangHeight, WorldPosition.z + RiseSpeed * Time.Delta);
        WorldPosition = WorldPosition.WithZ(height);

        if (TryClaim())
            return;

        Remaining -= Time.Delta;
        if (Remaining <= 0f)
            GameObject.Destroy();
    }

    // The closest dead player's UAV within reach claims the flare. One flare revives one player.
    // Reach is measured sideways with a height allowance, since the drone flies above the flare.
    private bool TryClaim()
    {
        GamePlayer claimer = Scene.GetAllComponents<GamePlayer>()
            .Where(x => x.IsDead && CanClaim(x))
            .OrderBy(x => (x.WorldPosition - WorldPosition).WithZ(0f).LengthSquared)
            .FirstOrDefault();
        if (claimer == null || !claimer.ReviveAt(GroundPosition))
            return false;

        GameObject.Destroy();
        return true;
    }

    private bool CanClaim(GamePlayer uav)
    {
        Vector3 offset = uav.WorldPosition - WorldPosition;
        return offset.WithZ(0f).LengthSquared <= ClaimRadius * ClaimRadius
            && System.MathF.Abs(offset.z) <= ClaimHeightTolerance;
    }
}
