using System.Globalization;
using BallGM.Application.Seasons;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>One award on the season review: the award, the winner and his team, and the figures behind it.</summary>
public sealed record SeasonReviewAward(string AwardName, string PlayerName, string TeamName, string Explanation);

/// <summary>
/// What a concluded season left behind, as the calendar shows it until the next season starts: the
/// champion, the awards (each single award, and the first all-league and all-defensive teams), what the
/// summer did — re-signings, options, buyouts, the draft, free agency and trades — and its headlines.
/// Presentation only: every figure comes from <see cref="SeasonConclusionSummary"/>.
/// </summary>
public sealed class SeasonReview
{
    /// <summary>How many of the summer's headlines the review lists.</summary>
    public const int HeadlineCount = 14;

    private static readonly string[] HeadlineCodes =
    [
        "ai_turn.trade_executed",
        "ai_trade.veteran_sale",
        "development.injury_aftermath",
        "ai_trade.salary_dump",
        "ai_offseason.offer_sheet_matched",
        "ai_buyout.agreed",
        "retirement.voluntary_drawn",
        "retirement.mandatory_age",
        "ai_offseason.player_choice",
    ];

    private SeasonReview(SeasonConclusionSummary summary)
    {
        Title = $"{SeasonLabel(summary.ConcludedSeasonYear)} season review";
        ChampionLine = summary.ChampionTeamName is null ? "No champion was crowned." : $"Champion: {summary.ChampionTeamName}";
        Awards = summary.Awards
            .Where(award => award.Selection == 1)
            .GroupBy(award => award.AwardCode)
            .Select(group => group.Count() == 1
                ? new SeasonReviewAward(group.First().AwardName, group.First().PlayerName, group.First().TeamName ?? string.Empty, group.First().Explanation)
                : new SeasonReviewAward($"{group.First().AwardName} — first team", string.Join(", ", group.Select(award => award.PlayerName)), string.Empty, string.Empty))
            .ToList();
        Summer =
            $"{summary.PlayersReSigned} re-signed · {summary.OptionsDecided} options decided · {summary.PlayersBoughtOut} bought out · " +
            $"{summary.QualifyingOffersTendered} qualifying offers · {summary.SalaryDumps} salary dumps · {summary.VeteranSales} veterans sold · " +
            $"{summary.PlayersDrafted} draft picks signed · {summary.MarketSignings} free-agent signings · {summary.AiTradesExecuted} trades · " +
            $"{summary.PlayersRetired.Count} retired";
        Headlines = summary.Notes
            .Where(note => HeadlineCodes.Contains(note.RuleCode))
            .OrderBy(note => Array.IndexOf(HeadlineCodes, note.RuleCode))
            .Take(HeadlineCount)
            .Select(note => note.Explanation)
            .ToList();
        Retired = summary.PlayersRetired.Count == 0 ? string.Empty : $"Retired: {string.Join(", ", summary.PlayersRetired.Take(12))}{(summary.PlayersRetired.Count > 12 ? $" and {summary.PlayersRetired.Count - 12} more" : string.Empty)}";
        NextSeason = $"Next: the {SeasonLabel(summary.NextSeasonYear)} season. Continue to start it.";
    }

    public string Title { get; }

    public string ChampionLine { get; }

    public IReadOnlyList<SeasonReviewAward> Awards { get; }

    public string Summer { get; }

    public IReadOnlyList<string> Headlines { get; }

    public bool HasHeadlines => Headlines.Count > 0;

    public string Retired { get; }

    public bool HasRetired => Retired.Length > 0;

    public string NextSeason { get; }

    public static SeasonReview From(SeasonConclusionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new SeasonReview(summary);
    }

    private static string SeasonLabel(int year) =>
        string.Create(CultureInfo.InvariantCulture, $"{year}–{(year + 1) % 100:00}");
}
