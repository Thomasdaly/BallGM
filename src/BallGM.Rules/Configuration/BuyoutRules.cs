using BallGM.Domain.Cap;
using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>
/// Whether this league has contract buyouts, and the guardrails around them (<c>docs/franchise-realism.md</c>
/// → F4). A buyout is a release the player agrees to: he gives back part of what he is owed to be let
/// go, and the rest stays on the releasing team's books as dead money. Unguarded, it is the
/// competitor's failure — a market where teams buy out good players and they sign wherever they like
/// every season — so the section states the guardrails the real agreement uses, generically:
/// <list type="bullet">
/// <item>the player may give back at most <see cref="MaximumGiveBackPercent"/> per cent of his remaining
/// guaranteed money;</item>
/// <item>the team that released him may not sign him again for <see cref="ReleasingTeamWaitSeasons"/>
/// season(s) — no buying a player out and re-signing him cheaper;</item>
/// <item>a team over <see cref="HighEarnerBarAbove"/> after a signing may not sign a released player whose
/// salary before his release was above the league's standard over-cap allowance — so a released star
/// cannot simply join the richest roster.</item>
/// </list>
/// Absent, a league has no buyouts and none of these restrictions.
/// </summary>
public sealed record BuyoutRules
{
    private const string InvalidGiveBackCode = "ruleset.invalid_buyout_give_back";
    private const string InvalidWaitCode = "ruleset.invalid_buyout_wait";

    private BuyoutRules(int maximumGiveBackPercent, int releasingTeamWaitSeasons, CapThresholdKind? highEarnerBarAbove)
    {
        MaximumGiveBackPercent = maximumGiveBackPercent;
        ReleasingTeamWaitSeasons = releasingTeamWaitSeasons;
        HighEarnerBarAbove = highEarnerBarAbove;
    }

    /// <summary>A league with no buyouts.</summary>
    public static BuyoutRules None { get; } = new(0, 0, null);

    public bool IsConfigured => MaximumGiveBackPercent > 0;

    /// <summary>The most of his remaining guaranteed money a player may give back in a buyout, in whole per cent.</summary>
    public int MaximumGiveBackPercent { get; }

    /// <summary>Seasons the releasing team must wait before signing a player it released (0 for no wait).</summary>
    public int ReleasingTeamWaitSeasons { get; }

    /// <summary>The line above which a team may not sign a released high earner, or null for no such bar.</summary>
    public CapThresholdKind? HighEarnerBarAbove { get; }

    public static DomainOperationResult<BuyoutRules> Create(int maximumGiveBackPercent, int releasingTeamWaitSeasons, CapThresholdKind? highEarnerBarAbove)
    {
        if (maximumGiveBackPercent is < 1 or > 100)
        {
            return DomainOperationResult<BuyoutRules>.Failure(new DomainError(
                InvalidGiveBackCode, $"A buyout give-back of {maximumGiveBackPercent}% is not a rule: it is 1 to 100 per cent."));
        }

        if (releasingTeamWaitSeasons is < 0 or > 5)
        {
            return DomainOperationResult<BuyoutRules>.Failure(new DomainError(
                InvalidWaitCode, $"A wait of {releasingTeamWaitSeasons} seasons before re-signing a released player is not a rule: it is 0 to 5."));
        }

        return DomainOperationResult<BuyoutRules>.Success(new BuyoutRules(maximumGiveBackPercent, releasingTeamWaitSeasons, highEarnerBarAbove));
    }
}
