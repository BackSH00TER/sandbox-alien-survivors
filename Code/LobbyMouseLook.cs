using System;
using Sandbox;

/// <summary>
/// Makes a lobby character look and reach toward its owner's mouse cursor.
/// The owner publishes a world-space target; every client applies it locally so all see the same pose.
/// </summary>
public sealed class LobbyMouseLook : Component
{
    [Property] public PlayerController Controller { get; set; }

    /// <summary>Max yaw away from the spawn facing. Kept under the controller's body-turn limit so the body never rotates.</summary>
    [Property, Range(0f, 45f)] public float MaxYaw { get; set; } = 40f;

    [Property, Range(0f, 60f)] public float MaxPitch { get; set; } = 35f;

    [Property] public bool ShouldMoveArms { get; set; } = true;

    /// <summary>Hand reach, as a fraction of arm length, when the cursor is right next to the shoulder. Low values fold the elbow.</summary>
    [Property, Range(0.1f, 1f)] public float MinHandReachFraction { get; set; } = 0.35f;

    /// <summary>Hand reach, as a fraction of arm length, when the cursor is far away. Kept just under 1 so the arm never locks straight.</summary>
    [Property, Range(0.1f, 1f)] public float MaxHandReachFraction { get; set; } = 0.95f;

    /// <summary>Distance from the shoulder to the cursor at which the arm is fully extended.</summary>
    [Property] public float FullReachDistance { get; set; } = 50f;

    /// <summary>How quickly the head and arms catch up to the cursor. Higher is snappier.</summary>
    [Property, Range(1f, 30f)] public float Smoothing { get; set; } = 8f;

    /// <summary>How much the wrists flick toward the cursor's direction of travel while the mouse moves. 0 keeps the hands rigid.</summary>
    [Property, Range(0f, 3f)] public float WristBend { get; set; } = 2f;

    /// <summary>World-space point the owner's cursor is aiming at, on the plane through the character's head.</summary>
    [Sync] public Vector3 LookTarget { get; set; }

    private Rotation _smoothedAim;
    private Rotation _aimLead;
    private bool _hasSmoothedAim;
    private Vector3 _rightHandOffset;
    private Vector3 _leftHandOffset;
    private bool _hasHandOffsets;

    // The rig's hand bones are mirrored, so the same orientation shows up rolled 180 degrees on the right hand.
    private const float RightHandRollDegrees = 180f;

    private const float MaxWristDegrees = 35f;

    protected override void OnStart()
    {
        if (Controller == null)
            Controller = GetComponent<PlayerController>();

        if (!IsProxy)
            Mouse.Visibility = MouseVisibility.Visible;
    }

    protected override void OnUpdate()
    {
        if (Controller == null || Controller.Renderer == null)
            return;

        if (!IsProxy)
            PublishLookTarget();

        if (LookTarget == Vector3.Zero)
            return;

        Vector3 lookDirection = ApplyEyeAngles();

        if (ShouldMoveArms)
        {
            _rightHandOffset = ApplyHandIk("hand_right", "arm_upper_R", "arm_lower_R", "hand_R", lookDirection, _rightHandOffset, RightHandRollDegrees);
            _leftHandOffset = ApplyHandIk("hand_left", "arm_upper_L", "arm_lower_L", "hand_L", lookDirection, _leftHandOffset, 0f);
            _hasHandOffsets = true;
        }
        else
        {
            Controller.Renderer.ClearIk("hand_right");
            Controller.Renderer.ClearIk("hand_left");
        }
    }

    protected override void OnDisabled()
    {
        if (!IsProxy)
            Mouse.Visibility = MouseVisibility.Auto;

        if (Controller?.Renderer == null)
            return;

        Controller.Renderer.ClearIk("hand_right");
        Controller.Renderer.ClearIk("hand_left");
    }

    private Vector3 GetHeadPosition()
    {
        return WorldPosition + Vector3.Up * (Controller.BodyHeight - Controller.EyeDistanceFromTop);
    }

    private void PublishLookTarget()
    {
        CameraComponent camera = Scene.Camera;
        if (camera == null)
            return;

        Ray ray = camera.ScreenPixelToRay(Mouse.Position);
        Vector3 cameraForward = camera.WorldRotation.Forward;

        float rayDot = Vector3.Dot(ray.Forward, cameraForward);
        if (rayDot < 0.0001f)
            return;

        float planeDistance = Vector3.Dot(GetHeadPosition() - camera.WorldPosition, cameraForward);
        LookTarget = ray.Position + ray.Forward * (planeDistance / rayDot);
    }

    // Aims the controller's eyes/head at the target, limited to a cone around the spawn facing.
    private Vector3 ApplyEyeAngles()
    {
        Vector3 localDirection = WorldRotation.Inverse * (LookTarget - GetHeadPosition());
        Angles localAngles = Rotation.LookAt(localDirection).Angles();
        localAngles.yaw = Math.Clamp(localAngles.yaw, -MaxYaw, MaxYaw);
        localAngles.pitch = Math.Clamp(localAngles.pitch, -MaxPitch, MaxPitch);

        Rotation targetAim = WorldRotation * localAngles.ToRotation();
        _smoothedAim = _hasSmoothedAim ? Rotation.Lerp(_smoothedAim, targetAim, GetSmoothingBlend()) : targetAim;
        _hasSmoothedAim = true;

        // How far the smoothed aim still has to travel. Zero when the mouse is still, so wrist motion only shows while it moves.
        _aimLead = targetAim * _smoothedAim.Inverse;

        Controller.EyeAngles = _smoothedAim.Angles();
        return _smoothedAim.Forward;
    }

    // Frame-rate independent exponential damping factor.
    private float GetSmoothingBlend()
    {
        return 1f - MathF.Exp(-Smoothing * Time.Delta);
    }

    // Places the hand relative to the shoulder. The reach grows with the cursor's distance from the shoulder, so the elbow folds when the cursor is close and straightens as it moves away. Returns the smoothed offset.
    private Vector3 ApplyHandIk(string ikName, string upperBoneName, string lowerBoneName, string handBoneName, Vector3 lookDirection, Vector3 previousOffset, float rollDegrees)
    {
        SkinnedModelRenderer renderer = Controller.Renderer;
        if (!renderer.TryGetBoneTransform(upperBoneName, out Transform upper)
            || !renderer.TryGetBoneTransform(lowerBoneName, out Transform lower)
            || !renderer.TryGetBoneTransform(handBoneName, out Transform hand))
            return previousOffset;

        float armLength = (lower.Position - upper.Position).Length + (hand.Position - lower.Position).Length;
        float distanceToTarget = (LookTarget - upper.Position).Length;
        float reachBlend = Math.Clamp(distanceToTarget / FullReachDistance, 0f, 1f);
        float reachFraction = MathX.Lerp(MinHandReachFraction, MaxHandReachFraction, reachBlend);

        Vector3 desiredOffset = lookDirection * (armLength * reachFraction);
        Vector3 offset = _hasHandOffsets ? Vector3.Lerp(previousOffset, desiredOffset, GetSmoothingBlend()) : desiredOffset;

        Angles lead = _aimLead.Angles();
        Rotation wristFlick = new Angles(
            Math.Clamp(lead.pitch * WristBend, -MaxWristDegrees, MaxWristDegrees),
            Math.Clamp(lead.yaw * WristBend, -MaxWristDegrees, MaxWristDegrees),
            0f).ToRotation();

        Rotation handRotation = wristFlick * Rotation.LookAt(offset) * Rotation.FromAxis(Vector3.Forward, rollDegrees);
        renderer.SetIk(ikName, new Transform(upper.Position + offset, handRotation));
        return offset;
    }
}
