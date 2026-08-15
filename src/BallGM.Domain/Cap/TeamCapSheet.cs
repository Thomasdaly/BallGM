using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Cap;

/// <summary>
/// One team's finances for one season: what is committed, what is dead, and where that total sits
/// against every configured threshold. Produced by the cap ledger (<c>BallGM.Rules.Cap</c>), which
/// owns the comparison; this type is the shape the result travels in.
/// </summary>
public sealed record TeamCapSheet
{
    public TeamCapSheet(
        TeamId teamId,
        Season season,
        Money committedSalary,
        Money deadMoney,
        Money totalPayroll,
        IReadOnlyList<CapCharge> charges,
        IReadOnlyList<ThresholdStanding> thresholds)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(committedSalary);
        ArgumentNullException.ThrowIfNull(deadMoney);
        ArgumentNullException.ThrowIfNull(totalPayroll);
        ArgumentNullException.ThrowIfNull(charges);
        ArgumentNullException.ThrowIfNull(thresholds);

        TeamId = teamId;
        Season = season;
        CommittedSalary = committedSalary;
        DeadMoney = deadMoney;
        TotalPayroll = totalPayroll;
        Charges = charges;
        Thresholds = thresholds;
    }

    public TeamId TeamId { get; }

    public Season Season { get; }

    /// <summary>Charges from live contracts.</summary>
    public Money CommittedSalary { get; }

    /// <summary>Charges with no live contract behind them.</summary>
    public Money DeadMoney { get; }

    public Money TotalPayroll { get; }

    public IReadOnlyList<CapCharge> Charges { get; }

    public IReadOnlyList<ThresholdStanding> Thresholds { get; }

    public ThresholdStanding StandingFor(CapThresholdKind kind) =>
        Thresholds.Single(standing => standing.Kind == kind);
}
