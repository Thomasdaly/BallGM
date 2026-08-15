using BallGM.Application.Leagues;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The pick-ownership board: every franchise's future drafts, what it controls in each, and what is
/// riding on the conditional ones. Everything shown comes from <see cref="PickBoardSummary"/> —
/// the view model arranges rows and formats nothing about the rules themselves, because what a
/// protection means is a rules answer, not a presentation one.
/// </summary>
public sealed class PickBoardViewModel : ViewModelBase
{
    private readonly PickBoardSummary _board;
    private PickBoardRow? _selectedFranchise;
    private PickAssetRow? _selectedAsset;
    private TeamSummary? _team;

    public PickBoardViewModel(LeagueOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);

        _board = overview.PickBoard;
        SeasonYear = overview.SeasonYear;
        Rows = _board.Franchises.Select(PickBoardRow.From).ToList();
        DraftSeasons = _board.DraftSeasons;
        SelectedFranchise = Rows.FirstOrDefault();
    }

    public string Title => "Pick board";

    public int SeasonYear { get; }

    public string SeasonLine =>
        $"Drafts {_board.FirstDraftSeason} to {_board.FirstDraftSeason + _board.DraftCount - 1} · {_board.RoundCount} rounds · the {SeasonYear} draft has already been settled";

    public IReadOnlyList<int> DraftSeasons { get; }

    public IReadOnlyList<PickBoardRow> Rows { get; }

    /// <summary>
    /// Follows the team chosen in the shell. Picks belong to franchises rather than to a season's
    /// squad, so the board resolves the team's franchise by name rather than pretending the two
    /// identifiers are interchangeable.
    /// </summary>
    public TeamSummary? Team
    {
        get => _team;
        set
        {
            if (!SetProperty(ref _team, value) || value is null)
            {
                return;
            }

            var match = Rows.FirstOrDefault(row =>
                string.Equals(row.FranchiseName, value.FranchiseName, StringComparison.Ordinal));

            if (match is not null)
            {
                SelectedFranchise = match;
            }
        }
    }

    public PickBoardRow? SelectedFranchise
    {
        get => _selectedFranchise;
        set
        {
            if (!SetProperty(ref _selectedFranchise, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(SelectedFranchiseName));
            RaisePropertyChanged(nameof(Assets));
            RaisePropertyChanged(nameof(Summary));
            SelectedAsset = Assets.FirstOrDefault(asset => asset.Protection is not null) ?? Assets.FirstOrDefault();
        }
    }

    public string SelectedFranchiseName => _selectedFranchise?.FranchiseName ?? "No franchise selected";

    /// <summary>Every asset the selected franchise has a stake in, oldest draft first.</summary>
    public IReadOnlyList<PickAssetRow> Assets =>
        _selectedFranchise is null
            ? []
            : _board.Franchises
                .Where(row => row.FranchiseId == _selectedFranchise.FranchiseId)
                .SelectMany(row => row.Drafts)
                .SelectMany(cell => cell.Assets.Select(asset => PickAssetRow.From(cell.DraftSeason, asset)))
                .ToList();

    /// <summary>
    /// The one line a GM reads first: how many of the next drafts' assets are conditional. A count of
    /// picks alone would hide the fact that half of them may never arrive.
    /// </summary>
    public string Summary
    {
        get
        {
            if (_selectedFranchise is null)
            {
                return "Select a franchise.";
            }

            var assets = Assets;
            var conditional = assets.Count(asset => asset.Protection is not null);
            var owned = assets.Count(asset => string.Equals(asset.State, "Own", StringComparison.Ordinal));

            return $"{assets.Count} assets across the next {_board.DraftCount} drafts — {owned} held outright, {conditional} carrying a condition.";
        }
    }

    public PickAssetRow? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (SetProperty(ref _selectedAsset, value))
            {
                RaisePropertyChanged(nameof(HasSelectedAsset));
            }
        }
    }

    public bool HasSelectedAsset => _selectedAsset is not null;
}
