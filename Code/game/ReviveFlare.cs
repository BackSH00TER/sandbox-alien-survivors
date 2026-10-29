using Sandbox;

/// <summary>
/// Red flare fired straight up when an alive survivor walks over a flare pickup. It rises to <see cref="HangHeight"/>,
/// glows for <see cref="LifetimeSeconds"/>, dims over the final <see cref="DimSeconds"/>, then burns out.
/// </summary>
public sealed class ReviveFlare : Component
{
    [Property, Range(10f, 600f)] public float LifetimeSeconds { get; set; } = 120f;
    [Property, Range(1f, 120f), Description("Seconds before burnout that the flare starts dimming and then blinks.")]
    public float DimSeconds { get; set; } = 30f;
    [Property, Range(50f, 2000f), Description("Height above the launch point the flare hangs at. Should match the dead player's UAV altitude.")]
    public float HangHeight { get; set; } = 300f;
    [Property, Range(100f, 5000f)] public float RiseSpeed { get; set; } = 900f;
    [Property, Range(10f, 500f), Description("How close a dead player's UAV must get to the flare to claim it.")]
    public float ClaimRadius { get; set; } = 90f;

    /// <summary>Seconds until the flare burns out. Counted down by the host and synced to everyone.</summary>
    [Sync(SyncFlags.FromHost)] public float Remaining { get; private set; }
    [Sync(SyncFlags.FromHost)] public Vector3 GroundPosition { get; private set; }

    private const float BlinkSeconds = 8f;
    private static readonly Color FlareColor = new Color(1f, 0.15f, 0.1f);
    private ModelRenderer _glow;
    private PointLight _light;
    private LineRenderer _beam;

    /// <summary>Host only. Fires a flare straight up from the given ground position.</summary>
    public static ReviveFlare Spawn(Vector3 groundPosition)
    {
        if (!Networking.IsHost)
        {
            Log.Error("Revive flares must be spawned on the host.");
            return null;
        }

        var obj = new GameObject(true, "Revive Flare");
        obj.WorldPosition = groundPosition + Vector3.Up * 40f;
        var flare = obj.AddComponent<ReviveFlare>();
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
        var visual = new GameObject(GameObject, true, "Flare Visual");
        visual.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
        visual.LocalScale = 0.4f;
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

    protected override void OnUpdate()
    {
        var brightness = Brightness;
        var visible = Remaining > BlinkSeconds || (int)(Time.Now * 6f) % 2 == 0;
        var pulse = 1f + System.MathF.Sin(Time.Now * 8f) * 0.08f;

        if (_glow.IsValid())
        {
            _glow.Enabled = visible;
            _glow.GameObject.LocalScale = 0.4f * pulse;
            _glow.Tint = FlareColor * brightness;
        }

        if (_light.IsValid())
        {
            _light.LightColor = FlareColor * brightness;
            _light.Radius = 450f * brightness;
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
    private bool TryClaim()
    {
        SurvivorController claimer = Scene.GetAllComponents<SurvivorController>()
            .Where(x => x.IsDead && (x.WorldPosition - WorldPosition).LengthSquared <= ClaimRadius * ClaimRadius)
            .OrderBy(x => (x.WorldPosition - WorldPosition).LengthSquared)
            .FirstOrDefault();
        if (claimer == null || !claimer.ReviveAt(GroundPosition))
            return false;

        GameObject.Destroy();
        return true;
    }
}
