namespace BallGM.Domain.Negotiations;

/// <summary>
/// When a free agent decides. Not a limit, so absence does not mean "this league has no such rule" —
/// every league resolves offers somehow. This is a mode with a finite set of behaviours, and like
/// the other mode fields in the ruleset it takes a documented default when the file leaves it out.
/// </summary>
public enum MarketResolutionMode
{
    /// <summary>
    /// Offers accumulate during a window and the market resolves at an explicit point, with offers
    /// ordered deterministically by a stated key rather than by arrival. The default, because the
    /// alternative makes the outcome depend on the order the UI happened to submit things in.
    /// </summary>
    ResolutionPoint = 0,

    /// <summary>A player decides the instant an offer lands. Simpler, and order-of-arrival dependent.</summary>
    Immediate = 1,
}
