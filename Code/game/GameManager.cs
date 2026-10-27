using Sandbox;

public enum GameState
{
	InProgress,
	Ended
}

public sealed class GameManager : Component
{
	[Group( "Game Settings" )]
	[Property, Sync( SyncFlags.FromHost )] public int CountdownTime { get; set; } = 60;
	[Sync( SyncFlags.FromHost )] public GameState State { get; set; } = GameState.InProgress;

	[Group( "Scene Transition" )]
	[Property] public string LobbyScene { get; set; }

	private float _countdownAccumulator;

	protected override void OnUpdate()
	{
		if (!Networking.IsHost || State != GameState.InProgress)
			return;

		_countdownAccumulator += Time.Delta;

		while (_countdownAccumulator >= 1f && CountdownTime > 0)
		{
			CountdownTime--;
			_countdownAccumulator -= 1f;
		}

		if (CountdownTime <= 0)
			EndGame();
	}

	private void EndGame()
	{
		State = GameState.Ended;

		if (string.IsNullOrWhiteSpace(LobbyScene))
		{
			Log.Error("Cannot end game because LobbyScene is not set.");
			return;
		}

		var options = new SceneLoadOptions();
		if (!options.SetScene(LobbyScene))
		{
			Log.Error($"Could not set lobby scene: {LobbyScene}");
			return;
		}

		if (!Game.ChangeScene(options))
			Log.Error($"Could not change to lobby scene: {LobbyScene}");
	}
}
