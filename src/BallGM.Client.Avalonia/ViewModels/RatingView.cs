using BallGM.Application.Players;
using BallGM.Domain.Seasons;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>One row of a rating's breakdown: a family and its contribution in rating points ("+1.2").</summary>
public sealed record RatingTermRow(string Family, string Value);

/// <summary>
/// A match rating as the pill shows it: the figure to one decimal, its band (a style class, always
/// beside the figure itself, so colour is never the only signal), and the breakdown its flyout lists.
/// Formatting only: the figure, band and reconciled breakdown all come from <see cref="StatFormulas"/>.
/// </summary>
public sealed record RatingView(int Tenths, RatingBand Band, string Tooltip, IReadOnlyList<RatingTermRow> Breakdown)
{
    public string Text => TextOf(Tenths);

    /// <summary>The one way a rating is printed: one decimal out of ten.</summary>
    public static string TextOf(int tenths) => StatColumns.Fixed(tenths / 10.0);

    public double Value => Tenths / 10.0;

    public bool IsOutstanding => Band == RatingBand.Outstanding;

    public bool IsGood => Band == RatingBand.Good;

    public bool IsPoor => Band == RatingBand.Poor;

    public bool HasBreakdown => Breakdown.Count > 0;

    /// <summary>One game's rating with its breakdown, or null for a player who did not play.</summary>
    public static RatingView? ForGame(CountingStats game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (StatFormulas.MatchRatingFor(game) is not { } rating)
        {
            return null;
        }

        return new RatingView(
            rating.Tenths,
            rating.Band,
            $"Match rating {StatColumns.Fixed(rating.Tenths / 10.0)}: what this player added in {game.Minutes} minutes, compared with an average player in the same minutes.",
            rating.TermsInTenths.Select(term => new RatingTermRow(term.Family, StatColumns.Signed(term.Hundredths / 10.0))).ToList());
    }

    /// <summary>The mean of a run of games' ratings, with no breakdown: a mean of games has no one set of terms.</summary>
    public static RatingView? ForAverage(RatingAverage? average) => average is null
        ? null
        : new RatingView(
            average.Tenths,
            average.Band,
            $"Average match rating {StatColumns.Fixed(average.Tenths / 10.0)} over {average.Games} game{(average.Games == 1 ? string.Empty : "s")} played.",
            []);
}
