using BallGM.Application.Seasons;
using BallGM.Domain.Common;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>One concluded season in the history list: its label and champion.</summary>
public sealed record HistorySeasonRow(int Year, string Season, string Champion);

/// <summary>
/// The league's history (S27, franchise F8): every concluded season, newest first, and the selected
/// season's champion and awards — single awards with the figures that decided them, all-league and
/// all-defensive teams by team. Presentation only: everything comes from <c>LeagueSession.SeasonHistory()</c>.
/// </summary>
public sealed class HistoryViewModel : ViewModelBase
{
    private readonly Func<DomainOperationResult<IReadOnlyList<SeasonHistorySummary>>> _history;
    private IReadOnlyList<SeasonHistorySummary> _seasons = [];
    private HistorySeasonRow? _selected;

    public HistoryViewModel(Func<DomainOperationResult<IReadOnlyList<SeasonHistorySummary>>> history)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        Refresh();
    }

    public string Title => "History";

    public IReadOnlyList<HistorySeasonRow> Seasons { get; private set; } = [];

    public bool HasSeasons => Seasons.Count > 0;

    public HistorySeasonRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                RaiseSelection();
            }
        }
    }

    private SeasonHistorySummary? SelectedSeason => _selected is null ? null : _seasons.FirstOrDefault(season => season.SeasonYear == _selected.Year);

    public string SelectedTitle => SelectedSeason is { } season ? $"{season.SeasonLabel} season" : string.Empty;

    public string SelectedChampion => SelectedSeason is { } season ? $"Champion: {season.ChampionTeamName ?? "none crowned"}" : string.Empty;

    /// <summary>The selected season's single awards, each with its winner, team and the figures behind it.</summary>
    public IReadOnlyList<SeasonReviewAward> Awards => SelectedSeason?.Awards
        .GroupBy(award => award.AwardCode)
        .Where(group => group.Count() == 1)
        .Select(group => group.Single())
        .Select(award => new SeasonReviewAward(award.AwardName, award.PlayerName, award.TeamName ?? string.Empty, award.Explanation))
        .ToList() ?? [];

    /// <summary>The selected season's team awards: one line per team, "All-League first team: …".</summary>
    public IReadOnlyList<string> Teams => SelectedSeason?.Awards
        .GroupBy(award => award.AwardCode)
        .Where(group => group.Count() > 1)
        .SelectMany(group => group
            .GroupBy(award => award.Selection)
            .OrderBy(team => team.Key)
            .Select(team => $"{group.First().AwardName} {Ordinal(team.Key)} team: {string.Join(", ", team.Select(award => award.PlayerName))}"))
        .ToList() ?? [];

    public bool HasAwards => Awards.Count > 0 || Teams.Count > 0;

    public void Refresh()
    {
        var history = _history();
        _seasons = history.IsSuccess ? history.Value : [];
        Seasons = _seasons
            .OrderByDescending(season => season.SeasonYear)
            .Select(season => new HistorySeasonRow(season.SeasonYear, season.SeasonLabel, season.ChampionTeamName ?? "—"))
            .ToList();
        RaisePropertyChanged(nameof(Seasons));
        RaisePropertyChanged(nameof(HasSeasons));
        Selected = Seasons.FirstOrDefault(row => row.Year == _selected?.Year) ?? Seasons.FirstOrDefault();
        RaiseSelection();
    }

    private void RaiseSelection()
    {
        RaisePropertyChanged(nameof(SelectedTitle));
        RaisePropertyChanged(nameof(SelectedChampion));
        RaisePropertyChanged(nameof(Awards));
        RaisePropertyChanged(nameof(Teams));
        RaisePropertyChanged(nameof(HasAwards));
    }

    private static string Ordinal(int value) => value switch
    {
        1 => "first",
        2 => "second",
        3 => "third",
        4 => "fourth",
        _ => "fifth",
    };
}
