using Sandbox;

/// <summary>Per-player round stats. The host records values and every client reads them.</summary>
public sealed class GamePlayerStats : Component
{
	[Sync(SyncFlags.FromHost)] private NetDictionary<string, float> Values { get; set; } = new();
	[Sync(SyncFlags.FromHost)] public bool IsRoundOver { get; private set; }

	[Sync(SyncFlags.FromHost)] public NetList<GameStatBucket> HistoryBuckets { get; private set; } = new();
	[Sync(SyncFlags.FromHost)] public NetList<GameStatMoment> HistoryMoments { get; private set; } = new();
	[Sync(SyncFlags.FromHost)] public NetList<GameStatPoint> HistoryLevels { get; private set; } = new();
	[Sync(SyncFlags.FromHost)] public float HistoryDuration { get; private set; }

	private readonly GameStatHistory _history = new();
	private readonly List<GameStatMoment> _moments = new();
	private readonly List<GameStatPoint> _levels = new() { new(0f, 1f) };
	private GameManager _manager;

	public string PlayerName => GameObject.Network.Owner?.DisplayName ?? GameObject.Name;

	public float Get(string key) => Values.TryGetValue(key, out float value) ? value : 0f;

	/// <summary>Changes whenever any stat changes, so UI can tell when to redraw.</summary>
	public int ContentHash()
	{
		int hash = 0;
		foreach (KeyValuePair<string, float> pair in Values)
		{
			hash += System.HashCode.Combine(pair.Key, pair.Value);
		}

		return hash;
	}

	public void Add(string key, float amount)
	{
		if (!float.IsFinite(amount) || amount <= 0f)
		{
			Log.Error("Stat amounts must be finite and positive.");
			return;
		}

		if (!TryGetRoundSeconds(out var seconds))
		{
			return;
		}

		_history.Add(
			key: key,
			amount: amount,
			seconds: seconds
		);
		if (key == GameStatKeys.Deaths || key == GameStatKeys.TimesRevived || key.StartsWith("modifier."))
		{
			var row = GameStatCatalog.Sections.SelectMany(section => section.Rows).First(stat => stat.Key == key);
			var kind = key.StartsWith("modifier.") ? "modifier" : "survival";
			var icon = key == GameStatKeys.Deaths ? "/ui/icons/skull.svg" : "";
			if (kind == "modifier")
			{
				var definition = ModifierCatalog.All.First(modifier => GameStatKeys.ModifierPicked(modifier.Id) == key);
				icon = definition.Icon;
			}

			_moments.Add(new GameStatMoment(
				Seconds: seconds,
				Kind: kind,
				Label: row.Label,
				Icon: icon
			));
		}

		Values[key] = Get(key) + amount;
	}

	/// <summary>Host only. Level changes retain their exact time, independently of combat buckets.</summary>
	public void RecordLevel(int level)
	{
		if (level <= _levels[^1].Value)
		{
			Log.Error("Recorded player levels must increase.");
			return;
		}

		if (!TryGetRoundSeconds(out var seconds))
		{
			return;
		}

		_levels.Add(new GameStatPoint(seconds, level));
	}

	private bool TryGetRoundSeconds(out float seconds)
	{
		seconds = 0f;
		if (!Networking.IsHost)
		{
			Log.Error("Stats must be recorded on the host.");
			return false;
		}

		if (IsRoundOver)
		{
			return false;
		}

		if (!_manager.IsValid())
		{
			_manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		}

		if (!_manager.IsValid() || !_manager.TimelineReady)
		{
			Log.Error("Round stats require an initialized GameManager clock.");
			return false;
		}

		seconds = _manager.ElapsedSeconds;
		return true;
	}

	/// <summary>Host only. Adds to a player's stat.</summary>
	public static void Record(GamePlayer player, string key, float amount = 1f)
	{
		GamePlayerStats stats = player.Stats;
		if (!stats.IsValid())
		{
			Log.Error("GamePlayer requires a GamePlayerStats component.");
			return;
		}

		stats.Add(key, amount);
	}

	/// <summary>Host only. Called for every hit that removed health, including the killing blow.</summary>
	public static void RecordDamage(GameEntity attacker, GameEntity victim, float amount, GameDamageType type, bool wasKill)
	{
		if (victim is GamePlayer playerVictim)
		{
			Record(playerVictim, GameStatKeys.DamageTaken, amount);
			Record(playerVictim, GameStatKeys.DamageTakenByType(type), amount);
			if (wasKill)
			{
				Record(playerVictim, GameStatKeys.Deaths);
			}
		}

		if (victim is GameEnemy enemy && attacker is GamePlayer playerAttacker)
		{
			Record(playerAttacker, GameStatKeys.DamageDealt, amount);
			Record(playerAttacker, GameStatKeys.DamageDealtByType(type), amount);
			Record(playerAttacker, GameStatKeys.DamageDealtToEnemy(enemy.Kind), amount);
			if (enemy.Kind == GameEnemyKind.Boss)
			{
				Record(playerAttacker, GameStatKeys.DamageDealtToBoss, amount);
			}

			if (wasKill)
			{
				Record(playerAttacker, GameStatKeys.Kills);
				Record(playerAttacker, GameStatKeys.KillsByEnemy(enemy.Kind));
			}
		}
	}

	/// <summary>Host only. Tells every client the round is over so the results screen can open.</summary>
	public static void EndRound(Scene scene)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Only the host can finalize round stats.");
			return;
		}

		var manager = scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (!manager.IsValid())
		{
			Log.Error("Round stats require a GameManager to finalize history.");
			return;
		}

		foreach (GamePlayerStats stats in scene.GetAllComponents<GamePlayerStats>())
		{
			if (stats.IsRoundOver)
			{
				continue;
			}

			var buckets = stats._history.Snapshot();
			stats.HistoryBuckets.AddRange(buckets);
			stats.HistoryMoments.AddRange(stats._moments);
			stats.HistoryLevels.AddRange(stats._levels);
			stats.HistoryDuration = manager.ElapsedSeconds;
			stats.IsRoundOver = true;
		}
	}

	public static bool HasRoundEnded(Scene scene) => scene.GetAllComponents<GamePlayerStats>().Any(x => x.IsRoundOver);
}
