using Sandbox;

public enum GameState
{
	InProgress,
	Ended
}

public sealed class GameManager : Component
{
	[Group("Game Settings")]
	[Property, Sync(SyncFlags.FromHost)] public int CountdownTime { get; set; } = 60;
	[Group("Game Settings"), Description("Ends the game as soon as every survivor is dead, since nobody is left to fire a revive flare. Turn off while debugging revives.")]
	[Property] public bool ShouldEndWhenAllDead { get; set; } = false;
	[Sync(SyncFlags.FromHost)] public GameState State { get; set; } = GameState.InProgress;

	[Group("Scene Transition")]
	[Property] public string LobbyScene { get; set; }

	private float _countdownAccumulator;

	protected override void OnUpdate()
	{
		if (!Networking.IsHost || State != GameState.InProgress)
			return;

		if (ShouldEndWhenAllDead && AreAllSurvivorsDead())
		{
			EndGame();
			return;
		}

		_countdownAccumulator += Time.Delta;

		while (_countdownAccumulator >= 1f && CountdownTime > 0)
		{
			CountdownTime--;
			_countdownAccumulator -= 1f;
		}

		if (CountdownTime <= 0)
			EndGame();
	}

	private bool AreAllSurvivorsDead()
	{
		List<SurvivorController> survivors = Scene.GetAllComponents<SurvivorController>().ToList();
		return survivors.Count > 0 && survivors.All(x => x.IsDead);
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
