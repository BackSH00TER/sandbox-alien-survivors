using Sandbox;

/// <summary>Local splash-radius marker, broadcast by the host through the shooter.</summary>
public sealed class SplashVisual : Component
{
	private const float Lifetime = 0.6f;
	private const int SegmentCount = 64;
	private float _expiresAt;
	private LineRenderer _ring;

	public static void Spawn(
		Scene scene,
		Vector3 center,
		float radius
	)
	{
		if (scene == null || !float.IsFinite(radius) || radius <= 0f)
		{
			Log.Error("Splash visuals require a scene and a finite positive radius.");
			return;
		}

		var visualObject = scene.CreateObject();
		visualObject.Name = "Splash Impact";
		visualObject.WorldPosition = center;
		visualObject.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		var visual = visualObject.AddComponent<SplashVisual>();
		visual._expiresAt = Time.Now + Lifetime;
		visual._ring = visualObject.AddComponent<LineRenderer>();
		visual._ring.UseVectorPoints = true;
		visual._ring.VectorPoints = new List<Vector3>();
		var ringCenter = center + Vector3.Up * 4f;
		for (int index = 0; index <= SegmentCount; index++)
		{
			var angle = index * (System.MathF.PI * 2f / SegmentCount);
			var cosine = System.MathF.Cos(angle);
			var sine = System.MathF.Sin(angle);
			var offset = new Vector3(
				x: cosine,
				y: sine,
				z: 0f
			) * radius;
			visual._ring.VectorPoints.Add(ringCenter + offset);
		}

		visual._ring.Face = SceneLineObject.FaceMode.Cylinder;
		visual._ring.CylinderSegments = 6;
		visual._ring.Opaque = false;
		visual._ring.Additive = true;
		visual._ring.CastShadows = false;
		visual._ring.Lighting = false;
		visual.UpdateRing();
	}

	protected override void OnUpdate()
	{
		if (Time.Now >= _expiresAt)
		{
			GameObject.Destroy();
			return;
		}

		UpdateRing();
	}

	private void UpdateRing()
	{
		var fade = ((_expiresAt - Time.Now) / Lifetime).Clamp(0f, 1f);
		_ring.Width = 4f + 4f * fade;
		_ring.Color = new Color(
			r: 1f,
			g: 1f,
			b: 1f,
			a: fade
		);
	}
}
