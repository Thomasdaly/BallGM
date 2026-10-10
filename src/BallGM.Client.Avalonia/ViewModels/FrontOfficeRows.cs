using BallGM.Application.AI;
using BallGM.Application.Players;
using BallGM.Application.Seasons;
using BallGM.Domain.Seasons;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// One thing an AI model had to say, with its code kept alongside the sentence — the same reasoning
/// <see cref="TradeFindingRow"/> already states for why the code is shown rather than hidden.
/// </summary>
public sealed record AIFindingRow(string RuleCode, string Explanation, string? TeamName)
{
    public static AIFindingRow From(AIFindingLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new AIFindingRow(line.RuleCode, line.Explanation, line.TeamName);
    }

    public string Heading => TeamName is null ? RuleCode : $"{TeamName} · {RuleCode}";
}

/// <summary>One position's read from the roster-needs assessment.</summary>
public sealed record PositionalNeedRow(string Position, string Severity, string Explanation)
{
    public static PositionalNeedRow From(PositionalNeedLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new PositionalNeedRow(line.Position, line.Severity, line.Explanation);
    }
}

/// <summary>
/// One trade this team's front office might pursue. The payroll line is read from the same
/// <see cref="TradeOutcomeRow"/>-shaped arithmetic a human proposal's assessment already carries —
/// this is not a second, thinner explanation of the same trade.
/// </summary>
public sealed record TradeTargetRow(
    string CounterpartyTeamName,
    string IncomingPlayerName,
    string OutgoingPlayerName,
    string PayrollLine,
    IReadOnlyList<AIFindingRow> Rationale)
{
    public string SummaryLine => $"Send {OutgoingPlayerName} to {CounterpartyTeamName} for {IncomingPlayerName}";

    /// <summary>What each side of the trade has produced this regular season. Read-only: the AI still values on Overall.</summary>
    public string IncomingProduction { get; init; } = string.Empty;

    public string OutgoingProduction { get; init; } = string.Empty;

    public static TradeTargetRow From(TradeTargetLine line, string viewedTeamId, IReadOnlyDictionary<string, PlayerSeasonTotals>? season = null)
    {
        ArgumentNullException.ThrowIfNull(line);

        var outcome = line.Assessment.Teams.FirstOrDefault(team => team.TeamId == viewedTeamId);
        var payrollLine = outcome is null
            ? "No payroll change reported."
            : $"Payroll {MoneyDisplay.ToMillions(outcome.PayrollBefore)} → {MoneyDisplay.ToMillions(outcome.PayrollAfter)}";

        return new TradeTargetRow(
            line.CounterpartyTeamName,
            line.IncomingPlayerName,
            line.OutgoingPlayerName,
            payrollLine,
            line.Rationale.Select(AIFindingRow.From).ToList())
        {
            IncomingProduction = $"{line.IncomingPlayerName}: {ProductionLine.For(season?.GetValueOrDefault(line.IncomingPlayerId))}",
            OutgoingProduction = $"{line.OutgoingPlayerName}: {ProductionLine.For(season?.GetValueOrDefault(line.OutgoingPlayerId))}",
        };
    }
}

/// <summary>One free-agent offer this team's front office might make, at the player's own asking price.</summary>
public sealed record FreeAgentTargetRow(
    string PlayerName,
    string OfferLine,
    IReadOnlyList<AIFindingRow> Rationale)
{
    /// <summary>What he has produced this regular season, if he has played. Read-only: the AI still values on Overall.</summary>
    public string Production { get; init; } = string.Empty;

    public static FreeAgentTargetRow From(FreeAgentTargetLine line, IReadOnlyDictionary<string, PlayerSeasonTotals>? season = null)
    {
        ArgumentNullException.ThrowIfNull(line);

        var offerLine = $"{DisplayText.Count(line.Assessment.SeasonCount, "season")} at {MoneyDisplay.ToMillions(line.Assessment.FirstSeasonCompensation)} per year";

        return new FreeAgentTargetRow(line.PlayerName, offerLine, line.Rationale.Select(AIFindingRow.From).ToList())
        {
            Production = ProductionLine.For(season?.GetValueOrDefault(line.PlayerId)),
        };
    }
}

/// <summary>
/// The draft-decision preview: which prospect this team's front office would take against a freshly
/// generated preview class. See <c>IFrontOfficeAdvisor.PreviewDraftRecommendation</c> for why this is
/// a preview and not a forecast of the actual future draft — the screen restates that plainly rather
/// than leaving a GM to assume otherwise.
/// </summary>
public sealed record DraftPreviewRow(string ProspectName, string Position, IReadOnlyList<AIFindingRow> Rationale)
{
    public string SummaryLine => $"{ProspectName} · {Position}";

    public static DraftPreviewRow From(DraftRecommendationLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new DraftPreviewRow(line.ProspectName, line.Position, line.Rationale.Select(AIFindingRow.From).ToList());
    }
}

/// <summary>
/// A player's season in one line, per game, from the shared stat columns: points, rebounds, assists,
/// steals, blocks, turnovers, fouls, plus-minus and his average match rating. What a GM glances at
/// beside a target's name.
/// </summary>
public static class ProductionLine
{
    /// <summary>What every production line says about itself.</summary>
    public const string Tooltip = "This regular season, per game. Shown for reading; the front office still values players on overall.";

    private static readonly StatColumn[] Columns =
        [StatColumns.Points, StatColumns.Rebounds, StatColumns.Assists, StatColumns.Steals, StatColumns.Blocks, StatColumns.Turnovers, StatColumns.PersonalFouls, StatColumns.PlusMinus];

    public static string For(PlayerSeasonTotals? season)
    {
        if (season is not { Stats.Games: > 0 } played)
        {
            return "no games this regular season";
        }

        var figures = string.Join(" · ", Columns.Select(column => $"{column.Format(played.Stats, StatBasis.PerGame)} {column.Header}"));
        var rating = played.AverageRating is { } average ? $" · {RatingView.TextOf(average.Tenths)} RTG" : string.Empty;
        return $"{figures}{rating} in {DisplayText.Count(played.Stats.Games, "game")}";
    }
}
