namespace BallGM.Domain.Contracts;

/// <summary>
/// Who, if anyone, decides whether a contract season happens. Named for the party holding the
/// decision rather than after any real-world agreement's option vocabulary.
/// </summary>
public enum ContractOptionKind
{
    /// <summary>The season is not optional: it happens as written.</summary>
    None = 0,

    /// <summary>The team decides whether the season happens.</summary>
    Team = 1,

    /// <summary>The player decides whether the season happens.</summary>
    Player = 2,
}

/// <summary>
/// Whether an option season's decision has been taken yet. A pending option season carries no cap
/// charge — it only becomes a charge once exercised — and a declined one never will.
/// </summary>
public enum ContractOptionStatus
{
    /// <summary>No option exists on this season.</summary>
    NotApplicable = 0,

    /// <summary>The option exists and has not been decided.</summary>
    Pending = 1,

    /// <summary>The option was taken up: the season happens.</summary>
    Exercised = 2,

    /// <summary>The option was turned down: the season, and everything after it, does not happen.</summary>
    Declined = 3,
}
