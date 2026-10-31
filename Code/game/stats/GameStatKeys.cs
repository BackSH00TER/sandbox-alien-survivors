/// <summary>Every stat key in one place. Keys per enum value are built from the enum, so new values need no extra code.</summary>
public static class GameStatKeys
{
    public const string DamageDealt = "damage_dealt";
    public const string DamageDealtToBoss = "damage_dealt.boss";
    public const string DamageTaken = "damage_taken";
    public const string Kills = "kills";
    public const string Deaths = "deaths";
    public const string TimesRevived = "times_revived";

    public static string DamageDealtByType(GameDamageType type) => $"damage_dealt.type.{type}";
    public static string DamageDealtToEnemy(GameEnemyKind kind) => $"damage_dealt.enemy.{kind}";
    public static string DamageTakenByType(GameDamageType type) => $"damage_taken.type.{type}";
    public static string KillsByEnemy(GameEnemyKind kind) => $"kills.enemy.{kind}";
    public static string ModifierPicked(GameModifier modifier) => $"modifier.{modifier}";
}
