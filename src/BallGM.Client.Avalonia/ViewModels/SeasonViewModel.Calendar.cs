using System.Globalization;
using BallGM.Client.Avalonia.Controls;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The calendar as the T8 template draws it (spec 04 S12): the month as game chips or as a list,
/// the viewed team's form, and the league's latest results, each of which opens its box score.
/// </summary>
public sealed partial class SeasonViewModel
{
    private int _viewIndex;

    public IReadOnlyList<string> ViewOptions { get; } = ["Month", "List"];

    /// <summary>0 the month grid, 1 the month as a list.</summary>
    public int ViewIndex
    {
        get => _viewIndex;
        set
        {
            if (SetProperty(ref _viewIndex, value))
            {
                RaisePropertyChanged(nameof(IsMonthView));
                RaisePropertyChanged(nameof(IsListView));
            }
        }
    }

    public bool HasRecentResults => RecentResults.Count > 0;

    public bool IsMonthView => _viewIndex == 0;

    public bool IsListView => _viewIndex == 1;

    public IReadOnlyList<CalendarDay> CalendarDays => MonthCells
        .Select(cell =>
        {
            var day = int.TryParse(cell.DayOfMonth, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
            var game = cell.HasGame
                ? new CalendarGame(cell.GameId ?? string.Empty, cell.OpponentId, cell.Opponent?.Initials ?? string.Empty, cell.IsHome, cell.IsPlayed ? cell.Detail : null, cell.IsPlayed ? cell.IsWin : null, cell.IsBackToBack)
                : null;
            return new CalendarDay(day, day > 0 && cell.IsInSeason, cell.IsToday, game)
            {
                Note = cell.IsUpcoming ? cell.Detail : null,
                Marker = cell.Marker,
                Tip = cell.Tip,
            };
        })
        .ToList();

    public IReadOnlyList<TableColumn> MonthListColumns { get; } =
    [
        new("day", "DAY", 72, IsFrozen: true, CanSort: false),
        new("venue", "", 48, CanSort: false),
        new("opp", "OPPONENT", 260, IsFrozen: false, CanSort: false),
        new("result", "RESULT", 96, CanSort: false),
        new("detail", "SCORE / RECORD", 140, IsNumeric: true, CanSort: false),
        new("b2b", "", 72, CanSort: false),
    ];

    public IReadOnlyList<TableRow> MonthListRows => MonthCells
        .Where(cell => cell.HasGame)
        .Select(cell => new TableRow(
            cell.GameId ?? cell.DayOfMonth,
            [
                new TextCell(cell.DayOfMonth, IsStrong: cell.IsToday),
                new TextCell(cell.IsHome ? "vs" : "@"),
                new TeamCell(cell.OpponentName ?? cell.Matchup, cell.OpponentId ?? string.Empty),
                new DeltaCell(cell.Result, cell.IsWin, cell.IsLoss),
                new TextCell(cell.Detail, IsNumeric: true),
                new StatusCell(cell.IsBackToBack ? "B2B" : string.Empty, StatusKind.Warning),
            ],
            IsHighlighted: cell.IsToday))
        .ToList();

    public System.Windows.Input.ICommand OpenListRowCommand => ShowGameCommand;

    public IReadOnlyList<FormResult> FormResults => Form.Select(chip => new FormResult(chip.IsWin, chip.Tip)).ToList();

    /// <summary>The open pre-game is today's: it can be simulated from the sheet.</summary>
    public bool CanSimGame => _sheet is { IsPreview: true } && _season is not null && _selectedFixture is { } fixture
        && _schedule.Any(day => day.Day == _season.Calendar.CurrentDay && day.Fixtures.Any(line => line.GameId == fixture.GameId));

    /// <summary>Plays today (every game on it) and shows the box score of the game that was open.</summary>
    public System.Windows.Input.ICommand SimGameCommand => new RelayCommand(() =>
    {
        if (!CanSimGame || _selectedFixture is not { } fixture)
        {
            return;
        }

        AdvanceBy(1);
        ShowGame(fixture.GameId);
        LoadSheet();
        RaisePropertyChanged(nameof(CanSimGame));
    });

    /// <summary>Opens a game's sheet from outside the calendar (the header's GO TO GAME).</summary>
    public void OpenGame(string gameId)
    {
        ShowGame(gameId);
        RaisePropertyChanged(nameof(CanSimGame));
    }

    private void RaiseCalendar()
    {
        RaisePropertyChanged(nameof(CalendarDays));
        RaisePropertyChanged(nameof(MonthListRows));
        RaisePropertyChanged(nameof(FormResults));
    }
}
