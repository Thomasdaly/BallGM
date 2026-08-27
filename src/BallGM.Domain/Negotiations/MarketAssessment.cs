using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Players;
using BallGM.Domain.Transactions;

namespace BallGM.Domain.Negotiations;

/// <summary>
/// One offer's standing in a resolving market: how the player reads it, whether it is still a legal
/// signing at all, and where it finished.
/// <para>
/// Legality and preference are kept apart because they fail differently. An offer the player likes
/// best but that no signing route pays for is not a near miss to be shaded down — it is out, with a
/// rule code saying which line the team crossed since it was made.
/// </para>
/// </summary>
/// <param name="Rank">1 for the offer the player took, ascending; 0 for an offer that was excluded.</param>
public sealed record MarketOfferStanding(
    Offer Offer,
    OfferPreference Preference,
    bool IsSignable,
    IReadOnlyList<RuleFinding> Exclusions,
    int Rank,
    string Narrative)
{
    public bool WasExcluded => Rank == 0;
}

/// <summary>
/// What would happen if this market resolved now, with the arithmetic behind it and none of the
/// consequences. Assembling it never touches the league or the negotiation — the free-agency board
/// re-asks this question every time anything changes, and a check that mutates is a check nobody can
/// run speculatively.
/// <para>
/// The three finding lists are the same three the trade and signing engines use, and
/// <see cref="Notes"/> means the same thing in all three: a rule this league does not configure, so
/// a check that never ran stays distinguishable from a check that ran and approved.
/// </para>
/// </summary>
/// <param name="TieBreakUsed">
/// Whether a seeded draw decided the winner. True only where the preference comparison declared
/// itself unable to separate the leaders on any factor — and it is surfaced rather than hidden,
/// because "the coin landed that way" is a better answer than a fabricated reason.
/// </param>
public sealed record MarketAssessment(
    NegotiationId NegotiationId,
    PlayerId PlayerId,
    SeasonDay Day,
    MarketResolutionMode Mode,
    IReadOnlyList<MarketOfferStanding> Standings,
    IReadOnlyList<Offer> ExpiringOffers,
    OfferId? AcceptedOfferId,
    bool TieBreakUsed,
    IReadOnlyList<RuleFinding> Warnings,
    IReadOnlyList<RuleFinding> Notes,
    string Narrative)
{
    /// <summary>Whether an offer on the table would be taken. False is a real outcome, not an error.</summary>
    public bool WouldSign => AcceptedOfferId is not null;

    public MarketOfferStanding? Winner =>
        AcceptedOfferId is null ? null : Standings.FirstOrDefault(standing => standing.Offer.Id == AcceptedOfferId);

    /// <summary>Offers in finishing order, excluded ones last.</summary>
    public IReadOnlyList<MarketOfferStanding> Ordered =>
        Standings
            .OrderBy(standing => standing.WasExcluded ? int.MaxValue : standing.Rank)
            .ThenBy(standing => standing.Offer.Id.Value, StringComparer.Ordinal)
            .ToList();
}

/// <summary>
/// A market that has resolved: the assessment that justified it, the contract that now exists where
/// one was signed, and the ledger lines that recorded it. <see cref="Contract"/> is <c>null</c> when
/// the market resolved on nobody, which is an outcome rather than a failure.
/// </summary>
public sealed record MarketExecution(
    MarketAssessment Assessment,
    Contract? Contract,
    SigningRouteKind? Route,
    IReadOnlyList<TransactionEntry> LedgerEntries)
{
    public bool PlayerSigned => Contract is not null;

    public int LedgerEntryCount => LedgerEntries.Count;
}
