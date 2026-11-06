using Sandbox;

/// <summary>Short-lived local arc, broadcast by the host for both impacts and chain hops.</summary>
public sealed class ShockVisual : Component
{
	private const float Lifetime = 0.18f;
	private const int SegmentCount = 8;
	private Vector3 _start;
	private Vector3 _end;
	private float _expiresAt;
	private LineRenderer _arc;

	public static void Spawn(
		Scene scene,
		Vector3 start,
		Vector3 end
	)
	{
		var visualObject = scene.CreateObject();
		visualObject.Name = "Shock Arc";
		visualObject.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		var visual = visualObject.AddComponent<ShockVisual>();
		visual._start = start;
		visual._end = end;
		visual._expiresAt = Time.Now + Lifetime;
		visual._arc = visualObject.AddComponent<LineRenderer>();
		visual._arc.UseVectorPoints = true;
		visual._arc.VectorPoints = new List<Vector3>();
		for (int index = 0; index <= SegmentCount; index++)
		{
			visual._arc.VectorPoints.Add(start);
		}

		visual._arc.Face = SceneLineObject.FaceMode.Cylinder;
		visual._arc.CylinderSegments = 6;
		visual._arc.Opaque = false;
		visual._arc.Additive = true;
		visual._arc.CastShadows = false;
		visual._arc.Lighting = false;
		visual.UpdateArc();
	}

	protected override void OnUpdate()
	{
		if (Time.Now >= _expiresAt)
		{
			GameObject.Destroy();
			return;
		}

		UpdateArc();
	}

	private void UpdateArc()
	{
		var delta = _end - _start;
		var sideways = Vector3.Cross(delta.Normal, Vector3.Up);
		if (sideways.LengthSquared < 0.01f)
		{
			sideways = Vector3.Forward;
		}

		sideways = sideways.Normal;
		for (int index = 0; index <= SegmentCount; index++)
		{
			var fraction = index / (float)SegmentCount;
			var position = _start + delta * fraction;
			if (index > 0 && index < SegmentCount)
			{
				var flicker = System.MathF.Sin(Time.Now * 90f + index * 17f);
				position += sideways * (flicker * 9f) + Vector3.Up * (flicker * 4f);
			}

			_arc.VectorPoints[index] = position;
		}

		var fade = (_expiresAt - Time.Now) / Lifetime;
		_arc.Width = 0.5f + fade * 1.25f;
		_arc.Color = new Color(
			r: 0.25f,
			g: 0.8f,
			b: 1f,
			a: fade
		);
	}
}
