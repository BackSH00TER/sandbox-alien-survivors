using Sandbox;

/// <summary>Per-player round stats. The host records values and every client reads them.</summary>
public sealed class PlayerStats : Component
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

    /// <summary>Host only. Adds to a stat of the player that owns the survivor.</summary>
    public static void Record(SurvivorController survivor, string key, float amount = 1f)
    {
        PlayerStats stats = survivor.GetComponent<PlayerStats>();
        if (!stats.IsValid())
        {
            Log.Error("SurvivorController requires a PlayerStats component.");
            return;
        }

        stats.Add(key, amount);
    }

    /// <summary>Host only. Called for every hit that removed health, including the killing blow.</summary>
    public static void RecordDamage(Entity attacker, Entity victim, float amount, DamageType type, bool wasKill)
    {
        if (victim is SurvivorController survivorVictim)
        {
            Record(survivorVictim, StatKeys.DamageTaken, amount);
            Record(survivorVictim, StatKeys.DamageTakenByType(type), amount);
            if (wasKill)
                Record(survivorVictim, StatKeys.Deaths);
        }

        if (victim is Enemy enemy && attacker is SurvivorController survivorAttacker)
        {
            Record(survivorAttacker, StatKeys.DamageDealt, amount);
            Record(survivorAttacker, StatKeys.DamageDealtByType(type), amount);
            Record(survivorAttacker, StatKeys.DamageDealtToEnemy(enemy.Kind), amount);
            if (enemy.Kind == EnemyKind.Boss)
                Record(survivorAttacker, StatKeys.DamageDealtToBoss, amount);
            if (wasKill)
            {
                Record(survivorAttacker, StatKeys.Kills);
                Record(survivorAttacker, StatKeys.KillsByEnemy(enemy.Kind));
            }
        }
    }

    /// <summary>Host only. Tells every client the round is over so the results screen can open.</summary>
    public static void EndRound(Scene scene)
    {
        foreach (PlayerStats stats in scene.GetAllComponents<PlayerStats>())
            stats.IsRoundOver = true;
    }

    public static bool HasRoundEnded(Scene scene) => scene.GetAllComponents<PlayerStats>().Any(x => x.IsRoundOver);
}
