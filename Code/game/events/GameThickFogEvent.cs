using Sandbox;

/// <summary>Starts temporary weather through the shared world-event dispatcher.</summary>
public sealed class GameThickFogEvent : Component, IGameEventAction
{
	[Property, Range(0.01f, 5f), Description("Peak volumetric fog density. Timing is 60 seconds with 20-second fades.")] public float Strength { get; set; } = 1.2f;
	private bool _overlapLogged;
	private bool _groundFailureLogged;

	public bool TryStart()
	{
		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (!Networking.IsHost || !manager.IsValid() || manager.State != GameState.InProgress)
		{
			Log.Error("Thick fog events require the host and an active round.");
			return false;
		}

		if (!float.IsFinite(Strength) || Strength <= 0f || Strength > 5f)
		{
			Log.Error("Thick fog strength must be finite and greater than zero, up to five.");
			return false;
		}

		if (Scene.GetAllComponents<GameThickFog>().Any(fog => fog.Active))
		{
			if (!_overlapLogged)
			{
				Log.Warning("Thick fog is already active; the event scheduler will retry after it clears.");
				_overlapLogged = true;
			}
			return false;
		}

		_overlapLogged = false;
		var ground = Scene.Trace.FromTo(
			from: WorldPosition + Vector3.Up * 512f,
			to: WorldPosition - Vector3.Up * 2000f
		).IgnoreDynamic().Run();
		if (!ground.Hit || ground.StartedSolid || ground.Normal.z < 0.5f || ground.Component is not Collider)
		{
			if (!_groundFailureLogged)
			{
				Log.Warning("Thick fog requires an arena ground collider beneath its event action; the event scheduler will retry.");
				_groundFailureLogged = true;
			}
			return false;
		}

		var collider = (Collider)ground.Component;
		var arenaBounds = collider.LocalBounds.Transform(collider.WorldTransform);
		_groundFailureLogged = false;
		var fog = GameThickFog.Spawn(arenaBounds: arenaBounds, strength: Strength);
		return fog.IsValid();
	}
}
