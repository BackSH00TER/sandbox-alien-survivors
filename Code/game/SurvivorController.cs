using Sandbox;

public sealed class SurvivorController : Entity
{
	[Property, RequireComponent] public PlayerController Controller { get; set; }
	[Property, Range( 0.25f, 4f )] public float Zoom { get; set; } = 1.5f;
	[Property, Range( 0.1f, 10f )] public float PunchInterval { get; set; } = 1f;
	[Property, Range( 1f, 200f )] public float PunchRange { get; set; } = 64f;
	[Property, Range( 1f, 100f )] public float PunchDamage { get; set; } = 10f;

	[Sync( SyncFlags.FromHost )] private float NextPunchTime { get; set; }

	private static readonly Vector3 BaseCameraOffset = new Vector3(-500f, 0f, 800f);
	private bool _missingCameraLogged;

	protected override void OnStart()
	{
		base.OnStart();
		Controller.UseCameraControls = false;
		Controller.UseLookControls = false;
		Controller.UseInputControls = false;
		Controller.RunByDefault = false;
		Controller.AltMoveButton = "";
		Controller.RunSpeed = Controller.WalkSpeed;

		if (Networking.IsHost)
			NextPunchTime = Time.Now + System.MathF.Max(0.1f, PunchInterval);
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if (IsProxy)
			return;

		Mouse.Visibility = MouseVisibility.Visible;

		var camera = Scene.Camera;
		if (!camera.IsValid())
		{
			if (!_missingCameraLogged)
			{
				Log.Error("SurvivorController requires an active scene camera.");
				_missingCameraLogged = true;
			}

			return;
		}

		_missingCameraLogged = false;
		camera.WorldPosition = WorldPosition + BaseCameraOffset / Zoom.Clamp(0.25f, 4f);
		camera.WorldRotation = Rotation.LookAt(-BaseCameraOffset, Vector3.Up);

		var ray = camera.ScreenPixelToRay(Mouse.Position);
		var result = Scene.Trace
			.Ray(ray, 10000f)
			.IgnoreGameObjectHierarchy(GameObject)
			.Run();

		Vector3 aimPosition;
		if (result.Hit)
		{
			aimPosition = result.EndPosition;
		}
		else
		{
			if (System.MathF.Abs(ray.Forward.z) < 0.0001f)
				return;

			var distance = (WorldPosition.z - ray.Position.z) / ray.Forward.z;
			if (distance < 0f)
				return;

			aimPosition = ray.Position + ray.Forward * distance;
		}

		var offset = aimPosition - WorldPosition;
		var direction = new Vector3(offset.x, offset.y, 0f);
		if (direction.LengthSquared > 0.001f)
			Controller.EyeAngles = Rotation.LookAt(direction, Vector3.Up).Angles();
	}

	protected override void OnFixedUpdate()
	{
		if (!IsProxy)
		{
			var cameraRotation = Rotation.LookAt(-BaseCameraOffset, Vector3.Up);
			var movementRotation = Rotation.FromYaw(cameraRotation.Angles().yaw);
			Controller.WishVelocity = Controller.Mode.UpdateMove(movementRotation, Input.AnalogMove);
		}

		if (!Networking.IsHost)
			return;

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (gameManager != null && gameManager.State != GameState.InProgress)
			return;

		if (Time.Now >= NextPunchTime)
		{
			NextPunchTime = Time.Now + System.MathF.Max(0.1f, PunchInterval);
			PlayPunch();
			Punch<Enemy>(Controller, PunchRange, PunchDamage);
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PlayPunch()
	{
		if (!Controller.Renderer.IsValid())
		{
			Log.Error("SurvivorController requires an animated renderer to punch.");
			return;
		}

		Controller.Renderer.Set("holdtype", (int)Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.Punch);
		Controller.Renderer.Set("b_attack", true);
	}

	protected override void OnPreRender()
	{
		if (Controller.Renderer.IsValid())
			Controller.Renderer.WorldRotation = Controller.EyeAngles.ToRotation();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if (!IsProxy && Controller.IsValid())
			Controller.WishVelocity = Vector3.Zero;
	}
}
