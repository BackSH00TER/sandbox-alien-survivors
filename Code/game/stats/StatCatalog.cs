public sealed class StatRow
{
    public string Label { get; }
    public string Key { get; }

    public StatRow(string label, string key)
    {
        Label = label;
        Key = key;
    }
}

public sealed class StatSection
{
    public string Title { get; }
    public IReadOnlyList<StatRow> Rows { get; }
    public bool ShouldHideEmptyRows { get; }

    public StatSection(string title, IReadOnlyList<StatRow> rows, bool shouldHideEmptyRows = false)
    {
        Title = title;
        Rows = rows;
        ShouldHideEmptyRows = shouldHideEmptyRows;
    }
}

/// <summary>What the end of round screen shows, in order. Add a stat by adding a row here.</summary>
public static class StatCatalog
{
    public static IReadOnlyList<StatSection> Sections { get; } = new List<StatSection>
    {
        new StatSection("Combat", new List<StatRow>
        {
            new StatRow("Damage dealt", StatKeys.DamageDealt),
            new StatRow("Damage dealt to boss", StatKeys.DamageDealtToBoss),
            new StatRow("Enemies killed", StatKeys.Kills)
        }),
        new StatSection("Damage dealt by type",
            RowsFor<DamageType>(type => new StatRow(type.ToString(), StatKeys.DamageDealtByType(type))), true),
        new StatSection("Damage dealt to enemy types",
            RowsFor<EnemyKind>(kind => new StatRow(kind.ToString(), StatKeys.DamageDealtToEnemy(kind))), true),
        new StatSection("Kills by enemy type",
            RowsFor<EnemyKind>(kind => new StatRow(kind.ToString(), StatKeys.KillsByEnemy(kind))), true),
        new StatSection("Survival", new List<StatRow>
        {
            new StatRow("Damage taken", StatKeys.DamageTaken),
            new StatRow("Deaths", StatKeys.Deaths),
            new StatRow("Times revived", StatKeys.TimesRevived)
        }),
        new StatSection("Damage taken by type",
            RowsFor<DamageType>(type => new StatRow(type.ToString(), StatKeys.DamageTakenByType(type))), true),
        new StatSection("Upgrades",
            RowsFor<GameModifier>(modifier => new StatRow(ModifierCatalog.Name(modifier), StatKeys.ModifierPicked(modifier))), true)
    };

    private static List<StatRow> RowsFor<T>(System.Func<T, StatRow> buildRow) where T : struct, System.Enum
    {
        return System.Enum.GetValues<T>().Select(buildRow).ToList();
    }
}
