namespace BallGM.Application.Packs;

/// <summary>How much a finding matters to whether the game will run.</summary>
public enum PackFindingSeverity
{
    /// <summary>Worth knowing; changes nothing. An asset another pack overrides, a rule a preset approximates.</summary>
    Info,

    /// <summary>The game runs, but something will look or behave differently from what the packs say — a missing face falls back to the drawn silhouette.</summary>
    Warning,

    /// <summary>The game will not load or will not play this combination. The load is refused until it is fixed.</summary>
    Error,
}

/// <summary>
/// The order the checker works in, which is also the order the game loads in: the pack library,
/// the setup that picks from it, the packs those depend on, the league settings, the league itself,
/// a played season, and the cosmetic layers on top.
/// </summary>
public enum PackCheckStage
{
    Library,
    Setup,
    Dependencies,
    Settings,
    League,
    Season,
    Cosmetics,
}

/// <summary>
/// One thing the pack checker has to say. <see cref="Code"/> is stable and machine-readable (what a
/// screen keys off and a bug report quotes); <see cref="Message"/> is what the player reads;
/// <see cref="Fix"/>, where there is an obvious one, is what to do about it.
/// <para>
/// Not <c>RuleFinding</c>: that type is a rule's verdict on a transaction, keyed by team. A pack
/// finding needs a severity — the checker's whole output is the difference between "will not load"
/// and "loads, with fallbacks" — and the pack it is about.
/// </para>
/// </summary>
public sealed record PackFinding(string Code, PackFindingSeverity Severity, PackCheckStage Stage, string Message)
{
    /// <summary>The pack the finding is about, by id. Absent for a finding about the setup as a whole.</summary>
    public string? PackId { get; init; }

    /// <summary>What to do about it, when there is one obvious thing.</summary>
    public string? Fix { get; init; }

    public static PackFinding Error(string code, PackCheckStage stage, string message, string? packId = null, string? fix = null) =>
        new(code, PackFindingSeverity.Error, stage, message) { PackId = packId, Fix = fix };

    public static PackFinding Warning(string code, PackCheckStage stage, string message, string? packId = null, string? fix = null) =>
        new(code, PackFindingSeverity.Warning, stage, message) { PackId = packId, Fix = fix };

    public static PackFinding Info(string code, PackCheckStage stage, string message, string? packId = null) =>
        new(code, PackFindingSeverity.Info, stage, message) { PackId = packId };
}
