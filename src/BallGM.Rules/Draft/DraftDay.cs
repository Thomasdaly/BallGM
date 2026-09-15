using BallGM.Domain.Common;
using BallGM.Domain.Draft;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.AI;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Draft;

/// <summary>One team's selection: which prospect, at which slot.</summary>
public sealed record DraftSelection(TeamId TeamId, Prospect Prospect, int Round, int SelectionNumber);

/// <summary>What one draft produced: the class it drew from, the order it drew in, and who took whom.</summary>
public sealed record DraftDayOutcome(
    DraftClass? Class,
    DraftOrderSnapshot? Order,
    IReadOnlyList<DraftSelection> Selections,
    IReadOnlyList<RuleFinding> Notes);

/// <summary>
/// Turns a concluded season's final standings into an actual draft: generates this league's class
/// (<see cref="Rules.Draft.ProspectGenerator"/>), draws the order (<see cref="Rules.Draft.DraftLottery"/>),
/// and walks the order slot by slot, handing each team whichever prospect
/// <see cref="DraftDecisionModel.Recommend"/> would take there.
/// <para>
/// Every selection goes through the same front office the diagnostics preview does — the best
/// prospect at a position the team needs, read through <see cref="ScoutingModel"/> rather than the
/// hidden <see cref="Prospect.TrueRating"/>, falling back to the best-scouted prospect left when no
/// need matches. This runs uniformly for every team, with no distinction between a human's team and
/// anyone else's, because no draft-day UI exists yet for a human to have picked any other way — see
/// <c>docs/architecture.md</c> → "AI turn execution: acting on a candidate" for why that is the
/// smallest viable call rather than a permanent answer to who gets a say.
/// </para>
/// <para>
/// A pick's owner is resolved through <see cref="DraftAssetBook"/> where a matching asset is
/// registered, and falls back to the slot's original franchise where it is not — a league that does
/// not track a pick this far into the future was never going to have traded it, so the fallback is
/// the correct answer rather than a compromise one.
/// </para>
/// </summary>
public sealed class DraftDay
{
    private const string NoDraftCode = "draft_day.no_draft";
    private const string NoGeneratorCode = "draft_day.no_generator";
    private const string ClassExhaustedCode = "draft_day.class_exhausted";
    private const string NoTeamForPickCode = "draft_day.no_team_for_pick";

    public DomainOperationResult<DraftDayOutcome> Run(
        Season draftSeason,
        IReadOnlyList<SeasonHistoryTeamRecord> finalStandings,
        IReadOnlyCollection<Team> teams,
        IReadOnlyDictionary<PlayerId, Player> playersById,
        DraftAssetBook draftAssets,
        DraftRules draftRules,
        DraftClassRules classRules,
        DraftLotteryRules lotteryRules,
        ScoutingRules scoutingRules,
        IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(draftSeason);
        ArgumentNullException.ThrowIfNull(finalStandings);
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(playersById);
        ArgumentNullException.ThrowIfNull(draftAssets);
        ArgumentNullException.ThrowIfNull(draftRules);
        ArgumentNullException.ThrowIfNull(classRules);
        ArgumentNullException.ThrowIfNull(lotteryRules);
        ArgumentNullException.ThrowIfNull(scoutingRules);
        ArgumentNullException.ThrowIfNull(random);

        if (!draftRules.HasDraft)
        {
            return DomainOperationResult<DraftDayOutcome>.Success(new DraftDayOutcome(
                null,
                null,
                [],
                [new RuleFinding(NoDraftCode, "This league holds no draft, so no selections were made.")]));
        }

        if (!classRules.IsConfigured)
        {
            return DomainOperationResult<DraftDayOutcome>.Success(new DraftDayOutcome(
                null,
                null,
                [],
                [new RuleFinding(
                    NoGeneratorCode,
                    "This league does not procedurally generate its own draft classes, so no selections were made.")]));
        }

        var classResult = ProspectGenerator.Generate(
            new DraftClassId(SortableId.NewId()),
            draftSeason,
            classRules,
            random);

        if (classResult.IsFailure)
        {
            return DomainOperationResult<DraftDayOutcome>.Failure(classResult.Errors.ToArray());
        }

        var franchiseByTeam = teams.ToDictionary(team => team.Id, team => team.FranchiseId);
        var teamByFranchise = teams.ToDictionary(team => team.FranchiseId, team => team);

        var reverseStandingsOrder = finalStandings
            .OrderByDescending(row => row.Position)
            .Select(row => franchiseByTeam.GetValueOrDefault(row.TeamId))
            .Where(franchiseId => franchiseId is not null)
            .Select(franchiseId => franchiseId!)
            .ToList();

        var lotteryResult = DraftLottery.Run(draftSeason, reverseStandingsOrder, draftRules, lotteryRules, random);
        if (lotteryResult.IsFailure)
        {
            return DomainOperationResult<DraftDayOutcome>.Failure(lotteryResult.Errors.ToArray());
        }

        var pool = classResult.Value.Prospects.ToList();
        var selections = new List<DraftSelection>();
        var notes = new List<RuleFinding>();

        foreach (var slot in lotteryResult.Value.Slots.OrderBy(slot => slot.Round).ThenBy(slot => slot.SelectionNumber))
        {
            if (pool.Count == 0)
            {
                notes.Add(new RuleFinding(
                    ClassExhaustedCode,
                    $"The {draftSeason.Year} draft class ran out of prospects after {selections.Count} selection(s); round {slot.Round}, pick {slot.SelectionNumber} was not made."));
                break;
            }

            var ownerFranchiseId = ResolveOwner(draftAssets, draftSeason, slot.Round, slot.OriginalFranchiseId);
            if (!teamByFranchise.TryGetValue(ownerFranchiseId, out var team))
            {
                notes.Add(new RuleFinding(
                    NoTeamForPickCode,
                    $"No active team controls franchise '{ownerFranchiseId.Value}''s round {slot.Round} pick, so selection {slot.SelectionNumber} was skipped."));
                continue;
            }

            var recommendation = DraftDecisionModel.Recommend(
                team.Id, team, playersById, pool, team.RosterLimits, scoutingRules, draftSeason)
                ?? throw new InvalidOperationException(
                    "DraftDecisionModel.Recommend returned no recommendation for a non-empty prospect pool.");

            var prospect = pool.First(candidate => candidate.Id == recommendation.ProspectId);

            pool.Remove(prospect);
            selections.Add(new DraftSelection(team.Id, prospect, slot.Round, slot.SelectionNumber));
            notes.AddRange(recommendation.Rationale);
        }

        return DomainOperationResult<DraftDayOutcome>.Success(new DraftDayOutcome(
            classResult.Value, lotteryResult.Value, selections, notes));
    }

    private static FranchiseId ResolveOwner(DraftAssetBook draftAssets, Season draftSeason, int round, FranchiseId originalFranchiseId)
    {
        var pick = draftAssets.Find(draftSeason, round, originalFranchiseId);
        if (pick is null)
        {
            return originalFranchiseId;
        }

        return draftAssets.Ownership(pick.Id)?.CurrentOwnerFranchiseId ?? originalFranchiseId;
    }
}
