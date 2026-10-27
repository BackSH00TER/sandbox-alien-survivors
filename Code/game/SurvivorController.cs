using Sandbox;

public sealed class SurvivorController : Component
{
	[Property, RequireComponent] public PlayerController Controller { get; set; }
	[Property] public Vector3 CameraOffset { get; set; } = new Vector3(-500f, 0f, 800f);

	private bool _missingCameraLogged;

	protected override void OnStart()
	{
		Controller.UseCameraControls = false;
		Controller.UseLookControls = false;
		Controller.UseInputControls = false;
		Controller.RunByDefault = false;
		Controller.AltMoveButton = "";
		Controller.RunSpeed = Controller.WalkSpeed;
	}

	protected override void OnUpdate()
	{
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
		camera.WorldPosition = WorldPosition + CameraOffset;
		camera.WorldRotation = Rotation.LookAt(-CameraOffset, Vector3.Up);

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
		if (IsProxy)
			return;

		var cameraRotation = Rotation.LookAt(-CameraOffset, Vector3.Up);
		var movementRotation = Rotation.FromYaw(cameraRotation.Angles().yaw);
		Controller.WishVelocity = Controller.Mode.UpdateMove(movementRotation, Input.AnalogMove);
	}

	protected override void OnPreRender()
	{
		if (Controller.Renderer.IsValid())
			Controller.Renderer.WorldRotation = Controller.EyeAngles.ToRotation();
	}

	protected override void OnDisabled()
	{
		if (!IsProxy && Controller.IsValid())
			Controller.WishVelocity = Vector3.Zero;
	}
}
