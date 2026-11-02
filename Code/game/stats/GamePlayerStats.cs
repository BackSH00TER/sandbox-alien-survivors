using Sandbox;

/// <summary>Per-player round stats. The host records values and every client reads them.</summary>
public sealed class GamePlayerStats : Component
{
    [Sync(SyncFlags.FromHost)] private NetDictionary<string, float> Values { get; set; } = new();
    [Sync(SyncFlags.FromHost)] public bool IsRoundOver { get; private set; }

    public string PlayerName => GameObject.Network.Owner?.DisplayName ?? GameObject.Name;

    public float Get(string key) => Values.TryGetValue(key, out float value) ? value : 0f;

    /// <summary>Changes whenever any stat changes, so UI can tell when to redraw.</summary>
    public int ContentHash()
    {
        int hash = 0;
        foreach (KeyValuePair<string, float> pair in Values)
            hash += System.HashCode.Combine(pair.Key, pair.Value);

        return hash;
    }

    public void Add(string key, float amount)
    {
        if (!Networking.IsHost)
        {
            Log.Error("Stats must be recorded on the host.");
            return;
        }

        if (!float.IsFinite(amount) || amount <= 0f)
        {
            Log.Error("Stat amounts must be finite and positive.");
            return;
        }

        Values[key] = Get(key) + amount;
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
                Record(playerVictim, GameStatKeys.Deaths);
        }

        if (victim is GameEnemy enemy && attacker is GamePlayer playerAttacker)
        {
            Record(playerAttacker, GameStatKeys.DamageDealt, amount);
            Record(playerAttacker, GameStatKeys.DamageDealtByType(type), amount);
            Record(playerAttacker, GameStatKeys.DamageDealtToEnemy(enemy.Kind), amount);
            if (enemy.Kind == GameEnemyKind.Boss)
                Record(playerAttacker, GameStatKeys.DamageDealtToBoss, amount);
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
        foreach (GamePlayerStats stats in scene.GetAllComponents<GamePlayerStats>())
            stats.IsRoundOver = true;
    }

    public static bool HasRoundEnded(Scene scene) => scene.GetAllComponents<GamePlayerStats>().Any(x => x.IsRoundOver);
}
