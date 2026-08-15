using BallGM.Application.Leagues;
using BallGM.Application.Trades;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// One thing the rules had to say, with its code kept alongside the sentence. The code is shown, not
/// hidden: when a GM disputes a rejection, "which rule" is the first question, and a screen that
/// only shows prose cannot answer it.
/// </summary>
public sealed record TradeFindingRow(string RuleCode, string Explanation, string? TeamName)
{
    public static TradeFindingRow From(TradeFindingLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new TradeFindingRow(line.RuleCode, line.Explanation, line.TeamName);
    }

    public string Heading => TeamName is null ? RuleCode : $"{TeamName} · {RuleCode}";
}

/// <summary>One team's books on the far side of the proposed trade, formatted for reading.</summary>
public sealed record TradeOutcomeRow(
    string TeamName,
    string SalaryLine,
    string PayrollLine,
    string RosterLine,
    string PickLine,
    string ThresholdLine)
{
    public static TradeOutcomeRow From(TradeTeamOutcomeLine outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var change = outcome.PayrollChange switch
        {
            > 0 => $"up {MoneyDisplay.ToMillions(outcome.PayrollChange)}",
            < 0 => $"down {MoneyDisplay.ToMillions(Math.Abs(outcome.PayrollChange))}",
            _ => "unchanged",
        };

        // The strictest line the team ends up over, so the row says what the trade costs them in
        // freedom as well as in money.
        var crossed = outcome.ThresholdsAfter.LastOrDefault(threshold => threshold.IsOver);
        var nextLine = outcome.ThresholdsAfter.FirstOrDefault(threshold => !threshold.IsOver);

        var thresholdLine = crossed is null
            ? nextLine is null
                ? "Past every configured line."
                : $"Under every line — {MoneyDisplay.ToMillions(nextLine.SignedDistance)} below the {Spaced(nextLine.ThresholdName)}."
            : $"Over the {Spaced(crossed.ThresholdName)} by {MoneyDisplay.ToMillions(Math.Abs(crossed.SignedDistance))} after the trade.";

        return new TradeOutcomeRow(
            outcome.TeamName,
            $"Takes back {MoneyDisplay.ToMillions(outcome.IncomingSalary)}, sends out {MoneyDisplay.ToMillions(outcome.OutgoingSalary)}",
            $"Payroll {MoneyDisplay.ToMillions(outcome.PayrollBefore)} → {MoneyDisplay.ToMillions(outcome.PayrollAfter)} ({change})",
            $"Roster {outcome.RosterCountBefore} → {outcome.RosterCountAfter}",
            $"Picks {outcome.PicksBefore} → {outcome.PicksAfter}",
            thresholdLine);
    }

    /// <summary>Threshold names arrive from the rules layer as enum names; the screen reads them as words.</summary>
    private static string Spaced(string thresholdName) =>
        string.Concat(thresholdName.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $" {char.ToLowerInvariant(character)}" : $"{char.ToLowerInvariant(character)}"));
}

/// <summary>
/// A player as the trade form offers them. Wraps the read model rather than binding to it directly so
/// the salary is shown in the same formatted units as every other money figure in the client — a
/// trade machine quoting raw smallest units is one a GM has to do arithmetic against.
/// </summary>
public sealed record TradePlayerRow(RosterSpot Spot, string FullName, string Position, int Overall, string Salary, bool IsInjured)
{
    public static TradePlayerRow From(RosterSpot spot)
    {
        ArgumentNullException.ThrowIfNull(spot);

        return new TradePlayerRow(
            spot,
            spot.FullName,
            spot.Position,
            spot.Overall,
            MoneyDisplay.ToMillions(spot.CapCharge),
            spot.IsInjured);
    }
}

/// <summary>A draft pick as the trade form offers it: what it is, and what is riding on it.</summary>
public sealed record TradePickRow(string PickId, string Label, string State, string? Protection)
{
    public static TradePickRow From(int draftSeason, PickAssetSummary asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return new TradePickRow(
            asset.PickId,
            $"{draftSeason} · R{asset.Round} {asset.OriginalFranchiseName}",
            asset.State,
            asset.ProtectionSummary);
    }

    public bool IsConditional => Protection is not null;
}
