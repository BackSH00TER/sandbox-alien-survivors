using Sandbox;

/// <summary>
/// Local-only drone model shown at a dead survivor's position, so the player can see where their UAV is, plus a
/// shadow on the ground beneath it for depth. Hidden while the survivor is alive.
/// </summary>
public sealed class GameUavVisual : Component
{
    private const float ArmReach = 46f;
    private const float RotorSpinDegreesPerSecond = 1800f;
    private static readonly Color BodyColor = new Color(0.35f, 0.62f, 0.95f);
    private static readonly Color ArmColor = new Color(0.85f, 0.88f, 0.9f);
    private static readonly Color RotorColor = new Color(1f, 1f, 1f);
    private static readonly Color BeaconColor = new Color(0.2f, 1f, 0.45f);

    private GamePlayer _survivor;
    private GameObject _drone;
    private GameObject _shadow;
    private ModelRenderer _beacon;
    private readonly List<GameObject> _rotors = new();
    private readonly List<GameObject> _skippedForShadow = new(); // reused each frame to avoid allocating
    private bool _isShown;

    protected override void OnStart()
    {
        _survivor = GetComponent<GamePlayer>();
        BuildDrone();
        BuildShadow();
        SetShown(false);
    }

    protected override void OnUpdate()
    {
        bool shouldShow = _survivor.IsValid() && _survivor.IsDead;
        if (shouldShow != _isShown)
            SetShown(shouldShow);

        if (!_isShown)
            return;

        _drone.LocalPosition = Vector3.Up * (System.MathF.Sin(Time.Now * 3f) * 3f); // gentle hover bob

        for (int i = 0; i < _rotors.Count; i++)
        {
            float direction = i % 2 == 0 ? 1f : -1f; // neighbouring rotors spin opposite ways
            _rotors[i].LocalRotation = Rotation.FromYaw(Time.Now * RotorSpinDegreesPerSecond * direction);
        }

        _beacon.Enabled = (int)(Time.Now * 3f) % 2 == 0;
        UpdateShadow();
    }

    private void SetShown(bool shown)
    {
        _isShown = shown;
        _drone.Enabled = shown;
        _shadow.Enabled = shown;
    }

    private void BuildDrone()
    {
        _drone = new GameObject(GameObject, true, "UAV Drone");
        _drone.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;

        AddBox(_drone, "Body", Vector3.Zero, new Vector3(34f, 34f, 14f), BodyColor);
        AddBox(_drone, "Arm A", Vector3.Zero, new Vector3(ArmReach * 2f, 7f, 5f), ArmColor, Rotation.FromYaw(45f));
        AddBox(_drone, "Arm B", Vector3.Zero, new Vector3(ArmReach * 2f, 7f, 5f), ArmColor, Rotation.FromYaw(-45f));

        for (int i = 0; i < 4; i++)
        {
            Vector3 hubPosition = Rotation.FromYaw(45f + 90f * i) * Vector3.Forward * ArmReach + Vector3.Up * 4f;
            var hub = new GameObject(_drone, true, $"Rotor {i}");
            hub.LocalPosition = hubPosition;
            AddBox(hub, "Blade 1", Vector3.Zero, new Vector3(48f, 5f, 2f), RotorColor);
            AddBox(hub, "Blade 2", Vector3.Zero, new Vector3(48f, 5f, 2f), RotorColor, Rotation.FromYaw(90f));
            _rotors.Add(hub);
        }

        var beacon = new GameObject(_drone, true, "Beacon");
        beacon.LocalPosition = Vector3.Up * 11f;
        beacon.LocalScale = 0.14f;
        _beacon = beacon.AddComponent<ModelRenderer>();
        _beacon.Model = Model.Load("models/dev/sphere.vmdl");
        _beacon.Tint = BeaconColor;
        _beacon.RenderType = ModelRenderer.ShadowRenderType.Off;
    }

    private void BuildShadow()
    {
        _shadow = new GameObject(GameObject, true, "UAV Shadow");
        _shadow.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
        _shadow.LocalScale = new Vector3(1.1f, 1.1f, 0.01f);
        var renderer = _shadow.AddComponent<ModelRenderer>();
        renderer.Model = Model.Load("models/dev/sphere.vmdl");
        renderer.Tint = new Color(0.03f, 0.03f, 0.03f);
        renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
    }

    // Keeps the shadow on the ground directly below the drone. Characters and crates are skipped so the
    // shadow stays on the floor instead of climbing onto whatever is underneath.
    private void UpdateShadow()
    {
        const int MaxSkippedObjects = 8;
        _skippedForShadow.Clear();
        SceneTraceResult ground = default;
        for (int i = 0; i <= MaxSkippedObjects; i++)
        {
            var trace = Scene.Trace.FromTo(WorldPosition, WorldPosition - Vector3.Up * 3000f)
                .IgnoreGameObjectHierarchy(GameObject);
            foreach (var skippedObject in _skippedForShadow)
                trace = trace.IgnoreGameObjectHierarchy(skippedObject);

            ground = trace.Run();
            var entity = ground.Hit && ground.GameObject.IsValid() ? ground.GameObject.GetComponentInParent<GameEntity>() : null;
            if (!entity.IsValid())
                break;

            _skippedForShadow.Add(entity.GameObject);
        }

        Vector3 groundPosition = ground.Hit ? ground.EndPosition : WorldPosition.WithZ(0f);
        _shadow.WorldPosition = groundPosition + Vector3.Up * 1.5f;
        _shadow.WorldRotation = Rotation.Identity;
    }

    private static void AddBox(GameObject parent, string name, Vector3 position, Vector3 size, Color color, Rotation rotation = default)
    {
        var part = new GameObject(parent, true, name);
        part.LocalPosition = position;
        part.LocalRotation = rotation == default ? Rotation.Identity : rotation;
        part.LocalScale = size / 50f;
        var renderer = part.AddComponent<ModelRenderer>();
        renderer.Model = Model.Load("models/dev/box.vmdl");
        renderer.Tint = color;
        renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
    }
}
