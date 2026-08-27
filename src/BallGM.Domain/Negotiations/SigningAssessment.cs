using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Transactions;

namespace BallGM.Domain.Negotiations;

/// <summary>
/// How a team is paying for a signing. A signing is legal only if some route permits it, and the
/// route is recorded on the transaction, because "you signed him" without "using what" is an audit
/// trail that cannot answer the next question a GM asks.
/// <para>
/// Every deferred route in the mechanism inventory — incumbent retention, post-room, periodic,
/// injury replacement — is a variation on <em>eligibility</em>, so it arrives as another member here
/// and another entry in the route table, never as a branch inside an existing route.
/// </para>
/// </summary>
public enum SigningRouteKind
{
    /// <summary>
    /// The degenerate route: this league configures no soft cap, so no amount is restricted. Not an
    /// exception, and not a licence — it is what "there is no line" means when a GM asks how they
    /// are allowed to pay someone.
    /// </summary>
    UnrestrictedSigning = 0,

    /// <summary>Sign anyone up to the gap between payroll and the soft cap.</summary>
    CapRoom = 1,

    /// <summary>Always available regardless of payroll, at the league's compensation floor.</summary>
    MinimumSalary = 2,

    /// <summary>One fixed allowance usable above the soft cap, possibly withdrawn above a higher line.</summary>
    StandardOverCapAllowance = 3,
}

/// <summary>
/// What one route has to say about one offer. Three states, not two: a route can permit, refuse, or
/// not apply at all — and "this league configures no such line" is an answer rather than a refusal,
/// which is why <see cref="Applicable"/> is separate from <see cref="Permits"/>.
/// </summary>
/// <param name="MaximumFirstSeasonCompensation">
/// The most this route could pay in the offer's first season, or <c>null</c> where the route sets no
/// limit at all. What a GM needs in order to negotiate against a refusal is the figure, not the word.
/// </param>
public sealed record SigningRouteEvaluation(
    SigningRouteKind Kind,
    bool Applicable,
    bool Permits,
    Money? MaximumFirstSeasonCompensation,
    string RuleCode,
    string Explanation);

/// <summary>
/// The verdict on an offer, with the arithmetic that produced it. Assembling this never touches
/// league state: an offer screen's whole job is speculative runs, and validation that mutates is
/// validation nobody can run speculatively.
/// <para>
/// Three lists, the same three the trade engine uses and for the same reason.
/// <see cref="Notes"/> carries the rules this league does not configure, so a check that never ran
/// stays distinguishable from a check that ran and approved. Notes are not warnings: nothing is
/// wrong with a league that sets no maximum salary, and styling it as a caution would say otherwise.
/// </para>
/// </summary>
public sealed record SigningAssessment(
    Offer Offer,
    IReadOnlyList<RuleFinding> Violations,
    IReadOnlyList<RuleFinding> Warnings,
    IReadOnlyList<RuleFinding> Notes,
    IReadOnlyList<SigningRouteEvaluation> Routes,
    Money PayrollBefore,
    Money PayrollAfter,
    int RosterCountBefore,
    int RosterCountAfter,
    Money? CapRoomBefore)
{
    public bool IsLegal => Violations.Count == 0;

    /// <summary>
    /// The route that pays for this signing: the first that permits it, in the order the rules layer
    /// evaluated them, which runs cheapest-for-the-team first. <c>null</c> when nothing permits it.
    /// </summary>
    public SigningRouteEvaluation? PermittingRoute => Routes.FirstOrDefault(route => route.Permits);
}

/// <summary>
/// A completed signing: the contract that now exists, the assessment that justified it, and the
/// ledger entries that recorded it.
/// </summary>
public sealed record SigningExecution(
    SigningAssessment Assessment,
    Contract Contract,
    SigningRouteKind Route,
    IReadOnlyList<TransactionEntry> LedgerEntries)
{
    public int LedgerEntryCount => LedgerEntries.Count;
}
