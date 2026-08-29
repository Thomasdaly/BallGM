using BallGM.Application.Negotiations;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Application.Leagues;

/// <summary>
/// The free-agency market half of the session: opening a negotiation, putting offers and
/// counteroffers into it, and resolving the whole thing at a point.
/// <para>
/// Separate file, one class. Milestone 6a's <c>AssessOffer</c>/<c>SubmitOffer</c> pair is still the
/// right way to sign a player nobody else wants, and is untouched. This is what happens when
/// somebody else does want them.
/// </para>
/// </summary>
public sealed partial class LeagueSession
{
    private const string UnknownPlayerCode = "negotiation_request.unknown_player";
    private const string NoNegotiationCode = "negotiation_request.no_negotiation_for_player";
    private const string AlreadyOpenCode = "negotiation_request.already_open";
    private const string NotAFreeAgentCode = "negotiation_request.player_is_not_a_free_agent";
    private const string NegotiationPlayerMissingCode = "negotiation_request.negotiation_player_not_in_league";
    private const string BoardUnknownTeamCode = "free_agency_board.unknown_team";

    /// <summary>
    /// Every negotiation this session is holding, in-flight and finished. Domain aggregates rather
    /// than read models, because the one consumer that needs them whole is the serializer that
    /// writes a save — a screen reads <see cref="NegotiationSummary"/> instead.
    /// </summary>
    public IReadOnlyCollection<Negotiation> Negotiations => _negotiations.Values.ToArray();

    /// <summary>The negotiation over one player, if this session is holding one.</summary>
    public Negotiation? NegotiationFor(string playerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        return _negotiations.GetValueOrDefault(playerId);
    }

    /// <summary>
    /// Opens a market for one unsigned player. Refuses a player already under contract: a negotiation
    /// over somebody who cannot be signed would collect offers no route could ever pay for, and the
    /// refusal a GM would eventually get should arrive before they have made three of them.
    /// </summary>
    public DomainOperationResult<NegotiationSummary> OpenNegotiation(string playerId, int day)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);

        if (_snapshot is null)
        {
            return NotLoaded<NegotiationSummary>();
        }

        if (_negotiations.ContainsKey(playerId))
        {
            return DomainOperationResult<NegotiationSummary>.Failure(new DomainError(
                AlreadyOpenCode,
                $"A negotiation over player '{playerId}' is already running in this session."));
        }

        var player = _snapshot.Players.FirstOrDefault(candidate => candidate.Id.Value == playerId);
        if (player is null)
        {
            return DomainOperationResult<NegotiationSummary>.Failure(new DomainError(
                UnknownPlayerCode,
                $"Player '{playerId}' is not in this league."));
        }

        if (!IsFreeAgent(player, _snapshot))
        {
            return DomainOperationResult<NegotiationSummary>.Failure(new DomainError(
                NotAFreeAgentCode,
                $"{player.FullName} is already under contract, so there is no free-agency market over them."));
        }

        var dayResult = ToSeasonDay(day);
        if (dayResult.IsFailure)
        {
            return DomainOperationResult<NegotiationSummary>.Failure(dayResult.Errors.ToArray());
        }

        var negotiationResult = Negotiation.Open(new NegotiationId(SortableId.NewId()), player.Id, dayResult.Value);
        if (negotiationResult.IsFailure)
        {
            return DomainOperationResult<NegotiationSummary>.Failure(negotiationResult.Errors.ToArray());
        }

        _negotiations[playerId] = negotiationResult.Value;
        return Summarize(negotiationResult.Value, dayResult.Value);
    }

    /// <summary>
    /// Adopts a negotiation this session did not open — the load half of a save round trip. The
    /// player has to be in the loaded league, because a negotiation over somebody the league has
    /// never heard of is a save and a league that do not belong together.
    /// </summary>
    public DomainOperationResult<NegotiationSummary> AdoptNegotiation(Negotiation negotiation)
    {
        ArgumentNullException.ThrowIfNull(negotiation);

        if (_snapshot is null)
        {
            return NotLoaded<NegotiationSummary>();
        }

        var player = _snapshot.Players.FirstOrDefault(candidate => candidate.Id == negotiation.PlayerId);
        if (player is null)
        {
            return DomainOperationResult<NegotiationSummary>.Failure(new DomainError(
                NegotiationPlayerMissingCode,
                $"This negotiation is over player '{negotiation.PlayerId.Value}', who is not in the loaded league."));
        }

        _negotiations[negotiation.PlayerId.Value] = negotiation;
        return Summarize(negotiation, negotiation.LastActivityOn);
    }

    /// <summary>
    /// Puts a team's terms on the table, opening the player's market if nobody had yet. Opening on
    /// first contact rather than as a separate step because a GM making an offer has already decided
    /// they are in this market, and a screen that made them say so twice would be a screen with a
    /// button that does nothing.
    /// </summary>
    public DomainOperationResult<NegotiationSummary> PlaceOffer(OfferRequest request, int day)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_snapshot is null)
        {
            return NotLoaded<NegotiationSummary>();
        }

        var dayResult = ToSeasonDay(day);
        if (dayResult.IsFailure)
        {
            return DomainOperationResult<NegotiationSummary>.Failure(dayResult.Errors.ToArray());
        }

        var partiesResult = ResolveParties(request, _snapshot);
        if (partiesResult.IsFailure)
        {
            return DomainOperationResult<NegotiationSummary>.Failure(partiesResult.Errors.ToArray());
        }

        var (team, player) = partiesResult.Value;

        if (!_negotiations.ContainsKey(player.Id.Value))
        {
            var opened = OpenNegotiation(player.Id.Value, day);
            if (opened.IsFailure)
            {
                return opened;
            }
        }

        var negotiation = _negotiations[player.Id.Value];

        var offerResult = BuildOffer(request, team, player);
        if (offerResult.IsFailure)
        {
            return DomainOperationResult<NegotiationSummary>.Failure(offerResult.Errors.ToArray());
        }

        var placeResult = negotiation.PlaceOffer(offerResult.Value, dayResult.Value);
        return placeResult.IsFailure
            ? DomainOperationResult<NegotiationSummary>.Failure(placeResult.Errors.ToArray())
            : Summarize(negotiation, dayResult.Value);
    }

    /// <summary>
    /// Records what the player would rather have from one team. A new offer in the history authored
    /// by the player — not a state transition, and not an acceptance: the market is still open and
    /// the team answers by placing its next offer.
    /// </summary>
    public DomainOperationResult<NegotiationSummary> Counteroffer(CounterofferRequest request, int day)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_snapshot is null)
        {
            return NotLoaded<NegotiationSummary>();
        }

        var dayResult = ToSeasonDay(day);
        if (dayResult.IsFailure)
        {
            return DomainOperationResult<NegotiationSummary>.Failure(dayResult.Errors.ToArray());
        }

        var negotiation = _negotiations.GetValueOrDefault(request.PlayerId);
        if (negotiation is null)
        {
            return NoNegotiation<NegotiationSummary>(request.PlayerId);
        }

        var offerRequest = new OfferRequest(request.TeamId, request.PlayerId, request.Seasons);
        var partiesResult = ResolveParties(offerRequest, _snapshot);
        if (partiesResult.IsFailure)
        {
            return DomainOperationResult<NegotiationSummary>.Failure(partiesResult.Errors.ToArray());
        }

        var (team, player) = partiesResult.Value;

        var counterResult = BuildOffer(offerRequest, team, player);
        if (counterResult.IsFailure)
        {
            return DomainOperationResult<NegotiationSummary>.Failure(counterResult.Errors.ToArray());
        }

        var recorded = negotiation.Counter(
            counterResult.Value,
            new OfferId(request.InResponseToOfferId),
            dayResult.Value);

        return recorded.IsFailure
            ? DomainOperationResult<NegotiationSummary>.Failure(recorded.Errors.ToArray())
            : Summarize(negotiation, dayResult.Value);
    }

    /// <summary>Takes a team's offer back off the table.</summary>
    public DomainOperationResult<NegotiationSummary> WithdrawOffer(string playerId, string offerId, int day)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(offerId);

        if (_snapshot is null)
        {
            return NotLoaded<NegotiationSummary>();
        }

        var dayResult = ToSeasonDay(day);
        if (dayResult.IsFailure)
        {
            return DomainOperationResult<NegotiationSummary>.Failure(dayResult.Errors.ToArray());
        }

        var negotiation = _negotiations.GetValueOrDefault(playerId);
        if (negotiation is null)
        {
            return NoNegotiation<NegotiationSummary>(playerId);
        }

        var withdrawn = negotiation.WithdrawOffer(new OfferId(offerId), dayResult.Value);
        return withdrawn.IsFailure
            ? DomainOperationResult<NegotiationSummary>.Failure(withdrawn.Errors.ToArray())
            : Summarize(negotiation, dayResult.Value);
    }

    /// <summary>
    /// Works out who would win this player's market on a given day, and changes nothing. The board's
    /// counterpart to <see cref="AssessOffer"/>, and safe to call as often as a GM likes.
    /// </summary>
    public DomainOperationResult<MarketAssessmentSummary> AssessMarket(string playerId, int day)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);

        if (_snapshot is null)
        {
            return NotLoaded<MarketAssessmentSummary>();
        }

        var dayResult = ToSeasonDay(day);
        if (dayResult.IsFailure)
        {
            return DomainOperationResult<MarketAssessmentSummary>.Failure(dayResult.Errors.ToArray());
        }

        var negotiation = _negotiations.GetValueOrDefault(playerId);
        if (negotiation is null)
        {
            return NoNegotiation<MarketAssessmentSummary>(playerId);
        }

        var assessmentResult = _freeAgencyMarket.Assess(negotiation, _snapshot, dayResult.Value, _marketRandom);
        return assessmentResult.IsFailure
            ? DomainOperationResult<MarketAssessmentSummary>.Failure(assessmentResult.Errors.ToArray())
            : DomainOperationResult<MarketAssessmentSummary>.Success(ToSummary(assessmentResult.Value, _snapshot));
    }

    /// <summary>
    /// Resolves the market for real. Every competing offer is re-checked against the league as it
    /// stands at this moment, the player decides once, and either a contract exists afterwards or the
    /// negotiation is closed with nobody signed — which is an outcome, not a failure.
    /// </summary>
    public DomainOperationResult<MarketResolutionSubmission> ResolveMarket(string playerId, int day)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);

        if (_snapshot is null)
        {
            return NotLoaded<MarketResolutionSubmission>();
        }

        var dayResult = ToSeasonDay(day);
        if (dayResult.IsFailure)
        {
            return DomainOperationResult<MarketResolutionSubmission>.Failure(dayResult.Errors.ToArray());
        }

        var negotiation = _negotiations.GetValueOrDefault(playerId);
        if (negotiation is null)
        {
            return NoNegotiation<MarketResolutionSubmission>(playerId);
        }

        var executionResult = _freeAgencyMarket.Resolve(negotiation, _snapshot, dayResult.Value, _marketRandom);
        if (executionResult.IsFailure)
        {
            return DomainOperationResult<MarketResolutionSubmission>.Failure(executionResult.Errors.ToArray());
        }

        var execution = executionResult.Value;

        if (execution.Contract is not null)
        {
            // Same reasoning as SubmitOffer: a signing is the one transaction that creates an
            // aggregate rather than moving one, so the held league is replaced with one that has it.
            _snapshot = _snapshot with { Contracts = [.. _snapshot.Contracts, execution.Contract] };
        }

        var overviewResult = _overviewQuery.Project(_snapshot);
        if (overviewResult.IsFailure)
        {
            return DomainOperationResult<MarketResolutionSubmission>.Failure(overviewResult.Errors.ToArray());
        }

        var summaryResult = Summarize(negotiation, dayResult.Value);
        if (summaryResult.IsFailure)
        {
            return DomainOperationResult<MarketResolutionSubmission>.Failure(summaryResult.Errors.ToArray());
        }

        return DomainOperationResult<MarketResolutionSubmission>.Success(new MarketResolutionSubmission(
            ToSummary(execution.Assessment, _snapshot),
            summaryResult.Value,
            execution.PlayerSigned,
            execution.Route is null ? null : DescribeRoute(execution.Route.Value),
            execution.LedgerEntryCount,
            overviewResult.Value));
    }

    /// <summary>
    /// The free-agency board for one team: every position as a column, what the team already has
    /// there, and the best unsigned players available for it.
    /// <para>
    /// Columned by position against the team's own depth rather than as one ranked list, because the
    /// question a GM is actually asking is "who fixes what I am short of", and a league-wide best
    /// available answers a different one.
    /// </para>
    /// </summary>
    public DomainOperationResult<FreeAgencyBoardSummary> FreeAgencyBoard(string teamId, int day)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);

        if (_snapshot is null)
        {
            return NotLoaded<FreeAgencyBoardSummary>();
        }

        var dayResult = ToSeasonDay(day);
        if (dayResult.IsFailure)
        {
            return DomainOperationResult<FreeAgencyBoardSummary>.Failure(dayResult.Errors.ToArray());
        }

        var seasonDay = dayResult.Value;

        var team = _snapshot.Teams.FirstOrDefault(candidate => candidate.Id.Value == teamId);
        if (team is null)
        {
            return DomainOperationResult<FreeAgencyBoardSummary>.Failure(new DomainError(
                BoardUnknownTeamCode,
                $"Team '{teamId}' is not a team in this league."));
        }

        var overviewResult = _overviewQuery.Project(_snapshot);
        if (overviewResult.IsFailure)
        {
            return DomainOperationResult<FreeAgencyBoardSummary>.Failure(overviewResult.Errors.ToArray());
        }

        var overview = overviewResult.Value;
        var teamSummary = overview.Teams.First(candidate => candidate.TeamId == teamId);
        var expiryDays = _snapshot.Configuration.Negotiation.OfferExpiryDays;

        var columns = new List<BoardPositionColumn>();

        foreach (var position in Enum.GetValues<Position>())
        {
            var name = GetLeagueOverviewQuery.DescribePosition(position);

            var ownPlayers = teamSummary.Roster
                .Where(spot => spot.Position == name)
                .OrderByDescending(spot => spot.Overall)
                .ThenBy(spot => spot.FullName, StringComparer.Ordinal)
                .Select(spot => new BoardDepthLine(spot.PlayerId, spot.FullName, spot.Overall, spot.ContractSeasonsRemaining))
                .ToList();

            var available = overview.FreeAgents.Players
                .Where(line => line.Position == name)
                .Select(line => ToCandidate(line, team.Id, seasonDay, expiryDays))
                .ToList();

            columns.Add(new BoardPositionColumn(name, ownPlayers.Count, ownPlayers, available));
        }

        var ourNegotiations = new List<NegotiationSummary>();
        foreach (var negotiation in _negotiations.Values)
        {
            if (!negotiation.AllTeamOffers().Any(offer => offer.TeamId == team.Id))
            {
                continue;
            }

            var summary = Summarize(negotiation, seasonDay);
            if (summary.IsSuccess)
            {
                ourNegotiations.Add(summary.Value);
            }
        }

        return DomainOperationResult<FreeAgencyBoardSummary>.Success(new FreeAgencyBoardSummary(
            team.Id.Value,
            team.Name,
            seasonDay.Index,
            _snapshot.Configuration.Negotiation.MarketResolution.ToString(),
            expiryDays,
            columns,
            ourNegotiations
                .OrderBy(summary => summary.PlayerName, StringComparer.Ordinal)
                .ToList()));
    }

    private BoardCandidateLine ToCandidate(
        FreeAgentLine line,
        TeamId teamId,
        SeasonDay day,
        int? expiryDays)
    {
        var negotiation = _negotiations.GetValueOrDefault(line.PlayerId);

        var live = negotiation?.LiveOffersOn(day, expiryDays) ?? [];
        var ours = live.FirstOrDefault(offer => offer.TeamId == teamId);
        var counter = negotiation?.LatestCounterTo(teamId);

        var askingPrice = _snapshot is null
            ? null
            : _freeAgencyMarket.AskingPrice(_snapshot, new PlayerId(line.PlayerId));

        return new BoardCandidateLine(
            line.PlayerId,
            line.FullName,
            line.Overall,
            line.Age,
            line.SeasonsOfService,
            line.MinimumSalary,
            line.MaximumSalary,
            askingPrice?.SmallestUnits,
            negotiation is null ? "None" : negotiation.State.ToString(),
            live.Count,
            ours is not null,
            ours?.Id.Value,
            ours?.FirstSeasonCompensation.SmallestUnits,
            ours?.SeasonCount,
            counter?.Offer?.FirstSeasonCompensation.SmallestUnits,
            counter?.Offer?.SeasonCount,
            counter?.Narrative);
    }

    private static bool IsFreeAgent(Player player, LeagueSnapshot snapshot) =>
        !snapshot.Teams.Any(team => team.PlayerIds.Contains(player.Id)) &&
        !snapshot.Contracts.Any(contract =>
            !contract.IsTerminated &&
            contract.PlayerId == player.Id &&
            contract.TermFor(snapshot.CurrentSeason) is not null);

    /// <summary>
    /// Turns a day index from a screen into the domain's own unit. Negative is a caller error rather
    /// than a rule outcome, but it arrives from a control a GM can type into, so it comes back as a
    /// message.
    /// </summary>
    private static DomainOperationResult<SeasonDay> ToSeasonDay(int day) =>
        day < 0
            ? DomainOperationResult<SeasonDay>.Failure(new DomainError(
                "negotiation_request.negative_day",
                $"Day {day} is before the market opened. Days are counted from nought."))
            : DomainOperationResult<SeasonDay>.Success(new SeasonDay(day));

    private static DomainOperationResult<T> NoNegotiation<T>(string playerId) =>
        DomainOperationResult<T>.Failure(new DomainError(
            NoNegotiationCode,
            $"No negotiation over player '{playerId}' is running in this session. Place an offer to start one."));

    private DomainOperationResult<NegotiationSummary> Summarize(Negotiation negotiation, SeasonDay day)
    {
        if (_snapshot is null)
        {
            return NotLoaded<NegotiationSummary>();
        }

        var teamNames = _snapshot.Teams.ToDictionary(team => team.Id, team => team.Name);
        var player = _snapshot.Players.FirstOrDefault(candidate => candidate.Id == negotiation.PlayerId);
        var expiryDays = _snapshot.Configuration.Negotiation.OfferExpiryDays;

        return DomainOperationResult<NegotiationSummary>.Success(new NegotiationSummary(
            negotiation.Id.Value,
            negotiation.PlayerId.Value,
            player?.FullName ?? negotiation.PlayerId.Value,
            negotiation.State.ToString(),
            negotiation.OpenedOn.Index,
            negotiation.LiveOffersOn(day, expiryDays).Count,
            negotiation.AllTeamOffers().Count,
            negotiation.Counteroffers().Count,
            negotiation.AcceptedOfferId?.Value,
            negotiation.SignedContractId?.Value,
            negotiation.History.Select(entry => ToLine(entry, teamNames)).ToList()));
    }

    private static NegotiationEntryLine ToLine(NegotiationEntry entry, IReadOnlyDictionary<TeamId, string> teamNames) =>
        new(
            entry.Sequence,
            entry.Kind.ToString(),
            entry.Day.Index,
            entry.Author.ToString(),
            entry.TeamId?.Value,
            entry.TeamId is null ? null : teamNames.GetValueOrDefault(entry.TeamId, entry.TeamId.Value),
            entry.Offer?.FirstSeasonCompensation.SmallestUnits,
            entry.Offer?.SeasonCount,
            entry.Narrative);

    private static MarketAssessmentSummary ToSummary(MarketAssessment assessment, LeagueSnapshot snapshot)
    {
        var teamNames = snapshot.Teams.ToDictionary(team => team.Id, team => team.Name);
        var player = snapshot.Players.FirstOrDefault(candidate => candidate.Id == assessment.PlayerId);
        var winner = assessment.Winner;

        return new MarketAssessmentSummary(
            assessment.NegotiationId.Value,
            assessment.PlayerId.Value,
            player?.FullName ?? assessment.PlayerId.Value,
            assessment.Day.Index,
            assessment.Mode.ToString(),
            assessment.WouldSign,
            winner?.Offer.TeamId.Value,
            winner is null ? null : teamNames.GetValueOrDefault(winner.Offer.TeamId, winner.Offer.TeamId.Value),
            assessment.TieBreakUsed,
            assessment.Narrative,
            assessment.Ordered.Select(standing => ToLine(standing, teamNames)).ToList(),
            assessment.Warnings.Select(finding => new SigningFindingLine(finding.RuleCode, finding.Explanation)).ToList(),
            assessment.Notes.Select(finding => new SigningFindingLine(finding.RuleCode, finding.Explanation)).ToList());
    }

    private static MarketStandingLine ToLine(MarketOfferStanding standing, IReadOnlyDictionary<TeamId, string> teamNames) =>
        new(
            standing.Offer.Id.Value,
            standing.Offer.TeamId.Value,
            teamNames.GetValueOrDefault(standing.Offer.TeamId, standing.Offer.TeamId.Value),
            standing.Rank,
            standing.IsSignable,
            standing.Preference.MeetsReservation,
            standing.Offer.FirstSeasonCompensation.SmallestUnits,
            standing.Offer.SeasonCount,
            standing.Narrative,
            standing.Preference.Contributions
                .Select(contribution => new PreferenceFactorLine(
                    contribution.Factor.ToString(),
                    contribution.Score,
                    contribution.MaterialityBand,
                    contribution.RuleCode,
                    contribution.Explanation))
                .ToList(),
            standing.Exclusions
                .Select(finding => new SigningFindingLine(finding.RuleCode, finding.Explanation))
                .ToList());
}
