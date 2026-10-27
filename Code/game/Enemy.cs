using Sandbox;

public sealed class Enemy : Entity
{
	[Property, RequireComponent] public PlayerController Controller { get; set; }

	protected override void OnStart()
	{
		Controller.UseInputControls = false;
		Controller.UseLookControls = false;
		Controller.UseCameraControls = false;

		if (!Networking.IsHost || IsProxy)
			return;

		var dresser = GetComponent<Dresser>();
		if (dresser == null)
		{
			Log.Error("Enemy must include a Dresser.");
			GameObject.Destroy();
			return;
		}

		dresser.Source = Dresser.ClothingSource.Manual;
		dresser.Randomize();
	}

	protected override void OnFixedUpdate()
	{
		if (!Networking.IsHost || IsProxy)
			return;

		Controller.WishVelocity = Vector3.Zero;

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (gameManager != null && gameManager.State != GameState.InProgress)
			return;

		PlayerController target = null;
		var nearestDistanceSquared = float.PositiveInfinity;

		foreach (var player in Scene.GetAllComponents<PlayerController>())
		{
			if (!player.Active || player.GameObject.GetComponent<Enemy>() != null)
				continue;

			var distanceSquared = (player.WorldPosition - WorldPosition).LengthSquared;
			if (distanceSquared < nearestDistanceSquared)
			{
				target = player;
				nearestDistanceSquared = distanceSquared;
			}
		}

		if (target == null)
			return;

		var offset = target.WorldPosition - WorldPosition;
		var direction = new Vector3(offset.x, offset.y, 0f);
		var stoppingDistance = Controller.BodyRadius + target.BodyRadius;
		if (direction.LengthSquared <= stoppingDistance * stoppingDistance)
			return;

		Controller.EyeAngles = Rotation.LookAt(direction).Angles();
		Controller.WishVelocity = direction.Normal * Controller.WalkSpeed;
	}

	protected override void OnDisabled()
	{
		if (Networking.IsHost && !IsProxy && Controller.IsValid())
			Controller.WishVelocity = Vector3.Zero;
	}
}
