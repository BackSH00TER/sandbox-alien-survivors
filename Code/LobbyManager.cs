using System.Linq;
using Sandbox;

/// <summary>
/// Lobby state and flow controller. Spawn ownership lives in <see cref="LobbyNetworkSpawner"/>.
/// Tracks everyone's <see cref="PlayerReadyState"/>; the host decides when the game starts.
/// </summary>
public sealed class LobbyManager : Component
{
	[Group("Scene Transition")]
	[Property] public string GameScene { get; set; }

	/// <summary>Number of players currently in the lobby.</summary>
	public int PlayerCount => Scene.GetAllComponents<PlayerReadyState>().Count();

	/// <summary>Number of players currently readied up.</summary>
	public int ReadyCount => Scene.GetAllComponents<PlayerReadyState>().Count(x => x.IsReady);

	/// <summary>True when at least one player is in the lobby and every player is readied up.</summary>
	public bool AreAllPlayersReady => PlayerCount > 0 && ReadyCount == PlayerCount;

	/// <summary>Host-only. Changes every client to the game scene if everyone is ready.</summary>
	public void StartGame()
	{
		if (!Networking.IsHost)
			return;

		if (!AreAllPlayersReady)
			return;

		if (string.IsNullOrWhiteSpace(GameScene))
		{
			Log.Error("Cannot start game because GameScene is not set.");
			return;
		}

		SceneLoadOptions options = new SceneLoadOptions();
		bool wasSceneSet = options.SetScene(GameScene);
		if (!wasSceneSet)
		{
			Log.Error($"Could not set game scene: {GameScene}");
			return;
		}

		bool wasSceneChanged = Game.ChangeScene(options);
		if (!wasSceneChanged)
			Log.Error($"Could not change to game scene: {GameScene}");
	}
}
