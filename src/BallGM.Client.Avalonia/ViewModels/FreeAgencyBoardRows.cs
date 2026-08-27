using BallGM.Application.Negotiations;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// One position column on the board: what this team already has there, and who is available for it.
/// <para>
/// The column is the point of the screen. A GM reading a league-wide "best available" list learns
/// who the best free agent is and nothing about whether they need one, so the market is presented
/// against their own depth or not at all.
/// </para>
/// </summary>
public sealed record BoardColumnRow(
    string Position,
    string DepthLine,
    IReadOnlyList<BoardDepthRow> OwnPlayers,
    IReadOnlyList<BoardCandidateRow> Candidates)
{
    public static BoardColumnRow From(BoardPositionColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        var depth = column.OwnDepth switch
        {
            0 => "Nobody rostered here",
            1 => "1 rostered",
            _ => $"{column.OwnDepth} rostered",
        };

        return new BoardColumnRow(
            column.Position,
            depth,
            column.OwnPlayers.Select(BoardDepthRow.From).ToList(),
            column.BestAvailable.Select(BoardCandidateRow.From).ToList());
    }

    public bool HasCandidates => Candidates.Count > 0;

    public bool HasOwnPlayers => OwnPlayers.Count > 0;
}

/// <summary>One player already on the roster at a position, with how long they are tied up for.</summary>
public sealed record BoardDepthRow(string PlayerId, string FullName, int Overall, string ContractLine)
{
    public static BoardDepthRow From(BoardDepthLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var contract = line.ContractSeasonsRemaining switch
        {
            <= 0 => "expiring",
            1 => "1 season left",
            var seasons => $"{seasons} seasons left",
        };

        return new BoardDepthRow(line.PlayerId, line.FullName, line.Overall, contract);
    }
}

/// <summary>
/// One available player in a column. The asking price is shown when this league has one, and its
/// absence is stated rather than rendered as a zero — an open market gives a player no range to be
/// placed inside, and a board that printed "$0.0M" would be describing a different league.
/// </summary>
public sealed record BoardCandidateRow(
    string PlayerId,
    string FullName,
    int Overall,
    string? OurOfferId,
    string Detail,
    string AskLine,
    string MarketLine,
    string OurOfferLine,
    string CounterLine,
    bool HasCounter)
{
    public static BoardCandidateRow From(BoardCandidateLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var service = line.SeasonsOfService == 1 ? "1 season" : $"{line.SeasonsOfService} seasons";

        var ask = line.AskingPrice is { } asking
            ? $"Asking about {MoneyDisplay.ToMillions(asking)}"
            : "No asking price — this league sets no salary range";

        var market = line.NegotiationState == "None"
            ? "No market open"
            : line.LiveOfferCount switch
            {
                0 => $"{line.NegotiationState} · nothing on the table",
                1 => $"{line.NegotiationState} · 1 offer on the table",
                var count => $"{line.NegotiationState} · {count} offers on the table",
            };

        var ours = line is { HasOurOffer: true, OurFirstSeasonCompensation: { } compensation, OurSeasonCount: { } seasons }
            ? $"Our offer: {MoneyDisplay.ToMillions(compensation)} × {seasons} season(s)"
            : "We have nothing on the table";

        var counter = line is { CounterofferFirstSeasonCompensation: { } counterAmount, CounterofferSeasonCount: { } counterSeasons }
            ? $"They countered: {MoneyDisplay.ToMillions(counterAmount)} × {counterSeasons} season(s)"
            : string.Empty;

        return new BoardCandidateRow(
            line.PlayerId,
            line.FullName,
            line.Overall,
            line.OurOfferId,
            $"Age {line.Age} · {service} of service",
            ask,
            market,
            ours,
            counter,
            counter.Length > 0);
    }
}

/// <summary>
/// One factor's say on one offer, as the board shows it. Four rows, never a total: a GM who was
/// outbid has to be able to read which factor beat them.
/// </summary>
public sealed record PreferenceFactorRow(string Factor, string ScoreLine, string Explanation)
{
    public static PreferenceFactorRow From(PreferenceFactorLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new PreferenceFactorRow(
            line.Factor,
            $"{line.Score}/100 (±{line.MaterialityBand} unnoticed)",
            line.Explanation);
    }
}

/// <summary>
/// Where one team's offer finished, with the factor breakdown behind it. An excluded offer keeps its
/// own reason: an offer the league would not permit and an offer the player would not accept are
/// different answers, and a board that showed both as "lost" would be hiding the actionable one.
/// </summary>
public sealed record MarketStandingRow(
    string TeamName,
    string RankLabel,
    string Terms,
    string Narrative,
    bool Won,
    bool Excluded,
    IReadOnlyList<PreferenceFactorRow> Factors,
    IReadOnlyList<SigningFindingRow> Exclusions)
{
    public static MarketStandingRow From(MarketStandingLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var rank = line.Rank switch
        {
            0 when !line.IsSignable => "Out — illegal",
            0 => "Out — refused",
            1 => "Signed",
            var place => $"#{place}",
        };

        return new MarketStandingRow(
            line.TeamName,
            rank,
            $"{MoneyDisplay.ToMillions(line.FirstSeasonCompensation)} × {line.SeasonCount} season(s)",
            line.Narrative,
            line.Rank == 1,
            line.Rank == 0,
            line.Factors.Select(PreferenceFactorRow.From).ToList(),
            line.Exclusions.Select(SigningFindingRow.From).ToList());
    }

    public bool HasExclusions => Exclusions.Count > 0;
}

/// <summary>One negotiation this team is exposed to, summarised for the side panel.</summary>
public sealed record BoardNegotiationRow(string PlayerName, string State, string Detail)
{
    public static BoardNegotiationRow From(NegotiationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new BoardNegotiationRow(
            summary.PlayerName,
            summary.State,
            $"{summary.LiveOfferCount} live of {summary.TotalOfferCount} offer(s) · {summary.CounterofferCount} counter(s)");
    }
}
