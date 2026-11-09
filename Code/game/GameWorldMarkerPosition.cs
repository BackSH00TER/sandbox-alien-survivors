using Sandbox;

public readonly record struct GameWorldMarkerPosition(Vector2 Position, float ArrowAngle)
{
	private const float EdgeX = 0.44f;

	public static GameWorldMarkerPosition Project(
		CameraComponent camera,
		Vector3 position,
		float edgeY = 0.42f
	)
	{
		var screen = camera.PointToScreenNormal(position);
		var isInFront = (position - camera.WorldPosition).Dot(camera.WorldRotation.Forward) > 0f;
		var offset = new Vector2(screen.x - 0.5f, screen.y - 0.5f);
		if (!isInFront)
		{
			offset = -offset;
		}

		var isOnScreen = isInFront && System.MathF.Abs(offset.x) <= EdgeX && System.MathF.Abs(offset.y) <= edgeY;
		if (isOnScreen)
		{
			var markerPosition = new Vector2(screen.x, screen.y - 0.07f);
			return new GameWorldMarkerPosition(Position: markerPosition, ArrowAngle: 90f);
		}

		var scale = System.MathF.Max(System.MathF.Abs(offset.x) / EdgeX, System.MathF.Abs(offset.y) / edgeY);
		if (scale < 0.0001f)
		{
			scale = 1f;
		}

		var edgePosition = new Vector2(0.5f + offset.x / scale, 0.5f + offset.y / scale);
		var angle = System.MathF.Atan2(offset.y, offset.x) * 180f / System.MathF.PI;
		return new GameWorldMarkerPosition(Position: edgePosition, ArrowAngle: angle);
	}
}
