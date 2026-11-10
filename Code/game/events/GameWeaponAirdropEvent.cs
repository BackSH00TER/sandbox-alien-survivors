using Sandbox;

/// <summary>Weapon delivery placement and execution through the shared world-event dispatcher.</summary>
public sealed class GameWeaponAirdropEvent : Component, IGameEventAction
{
	private bool _placementFailureLogged;

	public bool TryStart()
	{
		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (!Networking.IsHost || !manager.IsValid() || manager.State != GameState.InProgress)
		{
			Log.Error("Weapon airdrop events require the host and an active round.");
			return false;
		}

		var spawner = Scene.GetAllComponents<GameCrateSpawner>().FirstOrDefault(item => item.Active);
		if (!spawner.IsValid() || !spawner.TryFindPosition(out var position))
		{
			if (!_placementFailureLogged)
			{
				Log.Warning("Weapon airdrop requires an active crate spawner and clear ground; the event scheduler will retry.");
				_placementFailureLogged = true;
			}
			return false;
		}

		_placementFailureLogged = false;
		var airdrop = GameWeaponAirdrop.Spawn(
			groundPosition: position,
			rifle: spawner.Rifle,
			shotgun: spawner.Shotgun
		);
		return airdrop.IsValid();
	}
}
