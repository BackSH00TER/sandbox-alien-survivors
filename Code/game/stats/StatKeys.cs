/// <summary>Every stat key in one place. Keys per enum value are built from the enum, so new values need no extra code.</summary>
public static class StatKeys
{
    public const string DamageDealt = "damage_dealt";
    public const string DamageDealtToBoss = "damage_dealt.boss";
    public const string DamageTaken = "damage_taken";
    public const string ArmorGained = "armor_gained";
    public const string Kills = "kills";
    public const string Deaths = "deaths";
    public const string TimesRevived = "times_revived";

    public static string DamageDealtByType(DamageType type) => $"damage_dealt.type.{type}";
    public static string DamageDealtToEnemy(EnemyKind kind) => $"damage_dealt.enemy.{kind}";
    public static string DamageTakenByType(DamageType type) => $"damage_taken.type.{type}";
    public static string KillsByEnemy(EnemyKind kind) => $"kills.enemy.{kind}";
    public static string ModifierPicked(Modifier modifier) => $"modifier.{modifier}";
}
