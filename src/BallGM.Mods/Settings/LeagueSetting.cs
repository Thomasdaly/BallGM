namespace BallGM.Mods.Settings;

/// <summary>What kind of value a league setting takes, which is how an override of it is checked before the ruleset reader sees it.</summary>
public enum SettingValueKind
{
    /// <summary>A whole number in <see cref="LeagueSetting.Minimum"/>–<see cref="LeagueSetting.Maximum"/>.</summary>
    Integer,

    /// <summary>An amount of money in the currency's smallest units, never negative.</summary>
    Money,

    /// <summary>A whole percentage (125 means 125%).</summary>
    Percent,

    /// <summary>Hundredths of a percent (7000 means 70%).</summary>
    BasisPoints,

    Boolean,

    /// <summary>One of <see cref="LeagueSetting.Choices"/>.</summary>
    Choice,

    /// <summary>Free text.</summary>
    Text,

    /// <summary>A list of whole numbers, each in range.</summary>
    IntegerList,

    /// <summary>A list of <see cref="LeagueSetting.Choices"/>, each at most once.</summary>
    ChoiceList,

    /// <summary>A list of rows, each an object with exactly <see cref="LeagueSetting.Columns"/>.</summary>
    Table,

    /// <summary>A whole optional section; the only override it takes is <c>null</c>, removing the section.</summary>
    Section,
}

/// <summary>
/// One league setting: a field of the ruleset file, addressed by its dotted key (<c>softCap</c>,
/// <c>capMechanics.taxRatesPercent</c>), with the prose a settings screen shows beside it.
/// <para>
/// <see cref="Optional"/> is the ruleset format's central idea carried up to the settings layer: an
/// optional setting can be switched <em>off</em> (overridden with <c>null</c>), and off means the
/// league does not have that rule — not that the rule is set to zero. A league with no hard cap and a
/// league with a hard cap of nought are different leagues.
/// </para>
/// </summary>
public sealed record LeagueSetting(string Key, string Group, string Label, SettingValueKind Kind, string Description)
{
    public bool Optional { get; init; } = true;

    public long? Minimum { get; init; }

    public long? Maximum { get; init; }

    /// <summary>A short unit for display: "players", "days", "%".</summary>
    public string? Unit { get; init; }

    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>For a <see cref="SettingValueKind.Table"/>: the field names every row carries.</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>The JSON path segments: <c>capMechanics.taxRatesPercent</c> is <c>["capMechanics", "taxRatesPercent"]</c>.</summary>
    public IReadOnlyList<string> Path => Key.Split('.');
}
