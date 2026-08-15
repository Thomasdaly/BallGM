using BallGM.Domain.Common;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;

namespace BallGM.Domain.DraftAssets;

/// <summary>
/// Every draft pick a league knows about, each paired with the single ownership record that says
/// who controls it. The book is the reason duplicate ownership cannot happen: a pick is registered
/// once, gets exactly one <see cref="PickOwnership"/>, and is reachable only through it.
/// <para>
/// Lookup by coordinates — draft season, round, original franchise — is a first-class operation
/// rather than a convenience, because that is how a rollover finds the pick an obligation moves to.
/// </para>
/// </summary>
public sealed class DraftAssetBook
{
    private const string DuplicatePickCode = "draft_assets.duplicate_pick";
    private const string DuplicateCoordinatesCode = "draft_assets.duplicate_pick_coordinates";
    private const string UnknownPickCode = "draft_assets.unknown_pick";
    private const string LeagueMismatchCode = "draft_assets.league_mismatch";

    private readonly Dictionary<DraftPickId, DraftPick> _picks = [];
    private readonly Dictionary<DraftPickId, PickOwnership> _ownerships = [];
    private readonly Dictionary<(int SeasonYear, int Round, string OriginalFranchiseId), DraftPickId> _byCoordinates = [];

    public DraftAssetBook(LeagueId leagueId)
    {
        ArgumentNullException.ThrowIfNull(leagueId);
        LeagueId = leagueId;
    }

    public LeagueId LeagueId { get; }

    public IReadOnlyCollection<DraftPick> Picks => _picks.Values;

    public int Count => _picks.Count;

    /// <summary>
    /// Registers a pick and opens its ownership record. A pick already registered — by identifier or
    /// by coordinates — is a structured failure: two assets claiming to be the same franchise's
    /// first-rounder in the same draft is precisely the corruption this book exists to prevent.
    /// </summary>
    public DomainOperationResult Register(DraftPick pick, FranchiseId? initialOwnerFranchiseId = null)
    {
        ArgumentNullException.ThrowIfNull(pick);

        if (pick.LeagueId != LeagueId)
        {
            return DomainOperationResult.Failure(
                new DomainError(
                    LeagueMismatchCode,
                    $"Pick '{pick.Id.Value}' belongs to league '{pick.LeagueId.Value}' and cannot be registered in the book for league '{LeagueId.Value}'."));
        }

        if (_picks.ContainsKey(pick.Id))
        {
            return DomainOperationResult.Failure(
                new DomainError(DuplicatePickCode, $"Pick '{pick.Id.Value}' is already registered."));
        }

        var coordinates = CoordinatesOf(pick.DraftSeason, pick.Round, pick.OriginalFranchiseId);
        if (_byCoordinates.ContainsKey(coordinates))
        {
            return DomainOperationResult.Failure(
                new DomainError(
                    DuplicateCoordinatesCode,
                    $"The {pick.DraftSeason.Year} round {pick.Round} pick originally belonging to franchise '{pick.OriginalFranchiseId.Value}' is already registered."));
        }

        var ownershipResult = PickOwnership.Create(pick.Id, initialOwnerFranchiseId ?? pick.OriginalFranchiseId);
        if (ownershipResult.IsFailure)
        {
            return DomainOperationResult.Failure(ownershipResult.Errors.ToArray());
        }

        _picks.Add(pick.Id, pick);
        _ownerships.Add(pick.Id, ownershipResult.Value);
        _byCoordinates.Add(coordinates, pick.Id);

        return DomainOperationResult.Success;
    }

    public DraftPick? Pick(DraftPickId pickId)
    {
        ArgumentNullException.ThrowIfNull(pickId);
        return _picks.GetValueOrDefault(pickId);
    }

    public PickOwnership? Ownership(DraftPickId pickId)
    {
        ArgumentNullException.ThrowIfNull(pickId);
        return _ownerships.GetValueOrDefault(pickId);
    }

    public DraftPick? Find(Season draftSeason, int round, FranchiseId originalFranchiseId)
    {
        ArgumentNullException.ThrowIfNull(draftSeason);
        ArgumentNullException.ThrowIfNull(originalFranchiseId);

        return _byCoordinates.TryGetValue(CoordinatesOf(draftSeason, round, originalFranchiseId), out var pickId)
            ? _picks[pickId]
            : null;
    }

    /// <summary>The picks in one draft, ordered by round and then by originating franchise, so callers iterate deterministically.</summary>
    public IReadOnlyList<DraftPick> PicksInDraft(Season draftSeason)
    {
        ArgumentNullException.ThrowIfNull(draftSeason);

        return _picks.Values
            .Where(pick => pick.DraftSeason == draftSeason)
            .OrderBy(pick => pick.Round)
            .ThenBy(pick => pick.OriginalFranchiseId.Value, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every pick a franchise controls today, whatever franchise it originally belonged to.</summary>
    public IReadOnlyList<DraftPick> PicksControlledBy(FranchiseId franchiseId)
    {
        ArgumentNullException.ThrowIfNull(franchiseId);

        return _ownerships.Values
            .Where(ownership => ownership.CurrentOwnerFranchiseId == franchiseId)
            .Select(ownership => _picks[ownership.PickId])
            .OrderBy(pick => pick.DraftSeason.Year)
            .ThenBy(pick => pick.Round)
            .ThenBy(pick => pick.OriginalFranchiseId.Value, StringComparer.Ordinal)
            .ToList();
    }

    public DomainOperationResult Transfer(DraftPickId pickId, FranchiseId toFranchiseId)
    {
        ArgumentNullException.ThrowIfNull(pickId);
        ArgumentNullException.ThrowIfNull(toFranchiseId);

        var ownership = Ownership(pickId);
        return ownership is null
            ? UnknownPick(pickId)
            : ownership.TransferTo(toFranchiseId);
    }

    public DomainOperationResult Encumber(DraftPickId pickId, PickEncumbrance encumbrance)
    {
        ArgumentNullException.ThrowIfNull(pickId);
        ArgumentNullException.ThrowIfNull(encumbrance);

        var ownership = Ownership(pickId);
        return ownership is null
            ? UnknownPick(pickId)
            : ownership.Encumber(encumbrance);
    }

    private static DomainOperationResult UnknownPick(DraftPickId pickId) =>
        DomainOperationResult.Failure(
            new DomainError(UnknownPickCode, $"Pick '{pickId.Value}' is not registered in this league's draft assets."));

    private static (int SeasonYear, int Round, string OriginalFranchiseId) CoordinatesOf(
        Season draftSeason,
        int round,
        FranchiseId originalFranchiseId) =>
        (draftSeason.Year, round, originalFranchiseId.Value);
}
