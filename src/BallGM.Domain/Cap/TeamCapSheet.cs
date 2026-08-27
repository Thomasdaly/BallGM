using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Cap;

/// <summary>
/// One team's finances for one season: what is committed to players, what is dead, what is held for
/// roster spots still to fill, and where that total sits against every threshold the league configures. Produced by the cap ledger (<c>BallGM.Rules.Cap</c>), which
/// owns the comparison; this type is the shape the result travels in.
/// </summary>
public sealed record TeamCapSheet
{
    public TeamCapSheet(
        TeamId teamId,
        Season season,
        Money committedSalary,
        Money deadMoney,
        Money rosterHolds,
        Money totalPayroll,
        IReadOnlyList<CapCharge> charges,
        IReadOnlyList<ThresholdStanding> thresholds)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(committedSalary);
        ArgumentNullException.ThrowIfNull(deadMoney);
        ArgumentNullException.ThrowIfNull(rosterHolds);
        ArgumentNullException.ThrowIfNull(totalPayroll);
        ArgumentNullException.ThrowIfNull(charges);
        ArgumentNullException.ThrowIfNull(thresholds);

        TeamId = teamId;
        Season = season;
        CommittedSalary = committedSalary;
        DeadMoney = deadMoney;
        RosterHolds = rosterHolds;
        TotalPayroll = totalPayroll;
        Charges = charges;
        Thresholds = thresholds;
    }

    public TeamId TeamId { get; }

    public Season Season { get; }

    /// <summary>Charges from live contracts.</summary>
    public Money CommittedSalary { get; }

    /// <summary>Guaranteed money owed to a released player.</summary>
    public Money DeadMoney { get; }

    /// <summary>
    /// Placeholder charges for roster spots the team has not filled. Broken out from
    /// <see cref="CommittedSalary"/> so a GM can see how much of their payroll is players and how
    /// much is spots they still have to fill — but included in <see cref="TotalPayroll"/>, and so in
    /// every threshold comparison, because room that has to be spent on a roster spot is not room.
    /// <para>
    /// That includes the payroll floor, which is the arguable case: an empty roster counts its holds
    /// towards the league's minimum spend. It is one payroll or it is three, and a payroll figure
    /// that means something different depending on which line it is being compared against is the
    /// kind of arithmetic nobody can explain to a player. The floor is reporting-only until the
    /// milestone that gives it a penalty; if the answer should change, that is where it changes.
    /// </para>
    /// </summary>
    public Money RosterHolds { get; }

    public Money TotalPayroll { get; }

    public IReadOnlyList<CapCharge> Charges { get; }

    /// <summary>
    /// One standing per <em>configured</em> threshold, in ascending order of the amount. A league
    /// that configures no thresholds at all produces an empty list — which is the truth, rather than
    /// a set of zeroes every team is over.
    /// </summary>
    public IReadOnlyList<ThresholdStanding> Thresholds { get; }

    /// <summary>
    /// The standing against one threshold, or <c>null</c> when this league does not configure that
    /// line. Nullable rather than throwing: "this league has no luxury tax" is an answer, and a
    /// caller that has to guard a <c>.Single(...)</c> with a try/catch has not been given one.
    /// </summary>
    public ThresholdStanding? StandingFor(CapThresholdKind kind) =>
        Thresholds.SingleOrDefault(standing => standing.Kind == kind);
}
