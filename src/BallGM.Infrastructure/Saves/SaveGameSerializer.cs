using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BallGM.Application.Leagues;
using BallGM.Application.Saves;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;
using BallGM.Infrastructure.Contracts;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Rulesets;
using BallGM.Infrastructure.Seasons;
using BallGM.Infrastructure.Time;

namespace BallGM.Infrastructure.Saves;

/// <summary>
/// Reads and writes a whole played league as one versioned save file, composing the envelopes each
/// concept already round-trips on its own. Like every other serializer here, it never throws on bad
/// content: a save is untrusted input the moment it is a file on disk, so a malformed or impossible
/// save produces a structured failure a loader can explain instead of a crash mid-load.
/// <para>
/// <b>Loading replays every concept through its own aggregate factory or serializer</b> — the
/// League's history through <see cref="League.RecordSeason"/>, the season through
/// <c>SeasonSerializer</c>'s own replay, a negotiation through <c>NegotiationSerializer</c>'s own
/// replay — so a save claiming a sequence that could not have happened fails exactly the way it would
/// have failed live. This type adds no new replay style; it only adds envelopes for the four
/// aggregates — <c>League</c>, <c>Franchise</c>, <c>Team</c>, <c>Player</c> — that never had one.
/// </para>
/// </summary>
public sealed class SaveGameSerializer : ISaveGameStore
{
    private const string MalformedFileCode = "save_game.malformed_file";
    private const string UnsupportedSchemaVersionCode = "save_game.unsupported_schema_version";
    private const string InvalidFieldCode = "save_game.invalid_field";
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public DomainOperationResult<string> Save(
        LeagueSnapshot snapshot,
        SeasonRun? season,
        IReadOnlyDictionary<string, Negotiation> negotiations)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(negotiations);

        var rulesetResult = snapshot.Configuration.ToRuleset(snapshot.League.Alignment.IsFlat);
        if (rulesetResult.IsFailure)
        {
            // The configuration on a loaded snapshot already produced a working session — every
            // engine that ran against it succeeded — so a ruleset that fails to rebuild here means
            // the snapshot itself is not save-worthy content, the same class of failure every other
            // save-time check here reports rather than throws.
            return DomainOperationResult<string>.Failure(rulesetResult.Errors.ToArray());
        }

        var envelope = new SaveGameEnvelope(
            SaveGameEnvelope.CurrentSchemaVersion,
            new LeagueRulesetSerializer().Serialize(rulesetResult.Value),
            snapshot.CurrentSeason.Year,
            ToEnvelope(snapshot.League),
            snapshot.Franchises.OrderBy(franchise => franchise.Id.Value, StringComparer.Ordinal).Select(ToEnvelope).ToList(),
            snapshot.Teams.OrderBy(team => team.Id.Value, StringComparer.Ordinal).Select(ToEnvelope).ToList(),
            snapshot.Players.OrderBy(player => player.Id.Value, StringComparer.Ordinal).Select(ToEnvelope).ToList(),
            snapshot.Contracts
                .OrderBy(contract => contract.Id.Value, StringComparer.Ordinal)
                .Select(contract => new ContractSerializer().Serialize(contract))
                .ToList(),
            new DraftAssetSerializer().Serialize(snapshot.DraftAssets),
            ToEnvelope(snapshot.Ledger),
            season is null ? null : new SeasonSerializer().Serialize(season),
            negotiations.Values
                .OrderBy(negotiation => negotiation.Id.Value, StringComparer.Ordinal)
                .Select(negotiation => new NegotiationSerializer().Serialize(negotiation))
                .ToList());

        return DomainOperationResult<string>.Success(JsonSerializer.Serialize(envelope, Options));
    }

    public DomainOperationResult<SaveGameContents> Load(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        SaveGameEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SaveGameEnvelope>(json, Options);
        }
        catch (JsonException exception)
        {
            return Fail<SaveGameContents>(MalformedFileCode, $"The save is not valid JSON: {exception.Message}");
        }

        if (envelope is null)
        {
            return Fail<SaveGameContents>(MalformedFileCode, "The save payload did not contain a save game.");
        }

        if (envelope.SchemaVersion != SaveGameEnvelope.CurrentSchemaVersion)
        {
            return Fail<SaveGameContents>(
                UnsupportedSchemaVersionCode,
                $"Save schema version {envelope.SchemaVersion} cannot be read by this build, which reads version {SaveGameEnvelope.CurrentSchemaVersion}.");
        }

        try
        {
            var rulesetResult = new LeagueRulesetSerializer().Deserialize(envelope.RulesetJson);
            if (rulesetResult.IsFailure)
            {
                return DomainOperationResult<SaveGameContents>.Failure(rulesetResult.Errors.ToArray());
            }

            var configuration = rulesetResult.Value.ToConfiguration();

            var franchisesResult = BuildFranchises(envelope.Franchises);
            if (franchisesResult.IsFailure)
            {
                return DomainOperationResult<SaveGameContents>.Failure(franchisesResult.Errors.ToArray());
            }

            var playersResult = BuildPlayers(envelope.Players);
            if (playersResult.IsFailure)
            {
                return DomainOperationResult<SaveGameContents>.Failure(playersResult.Errors.ToArray());
            }

            var teamsResult = BuildTeams(envelope.Teams, configuration.RosterLimits);
            if (teamsResult.IsFailure)
            {
                return DomainOperationResult<SaveGameContents>.Failure(teamsResult.Errors.ToArray());
            }

            var leagueResult = BuildLeague(envelope.League, teamsResult.Value);
            if (leagueResult.IsFailure)
            {
                return DomainOperationResult<SaveGameContents>.Failure(leagueResult.Errors.ToArray());
            }

            var contractsResult = BuildContracts(envelope.Contracts);
            if (contractsResult.IsFailure)
            {
                return DomainOperationResult<SaveGameContents>.Failure(contractsResult.Errors.ToArray());
            }

            var draftAssetsResult = new DraftAssetSerializer().Deserialize(envelope.DraftAssets);
            if (draftAssetsResult.IsFailure)
            {
                return DomainOperationResult<SaveGameContents>.Failure(draftAssetsResult.Errors.ToArray());
            }

            var ledgerResult = BuildLedger(envelope.Ledger);
            if (ledgerResult.IsFailure)
            {
                return DomainOperationResult<SaveGameContents>.Failure(ledgerResult.Errors.ToArray());
            }

            var snapshot = new LeagueSnapshot(
                leagueResult.Value,
                new Season(envelope.CurrentSeasonYear),
                franchisesResult.Value,
                teamsResult.Value,
                playersResult.Value,
                contractsResult.Value,
                draftAssetsResult.Value,
                ledgerResult.Value,
                configuration);

            SeasonRun? season = null;
            if (envelope.Season is not null)
            {
                var seasonResult = new SeasonSerializer().Deserialize(envelope.Season);
                if (seasonResult.IsFailure)
                {
                    return DomainOperationResult<SaveGameContents>.Failure(seasonResult.Errors.ToArray());
                }

                season = seasonResult.Value;
            }

            var negotiationsResult = BuildNegotiations(envelope.Negotiations);
            if (negotiationsResult.IsFailure)
            {
                return DomainOperationResult<SaveGameContents>.Failure(negotiationsResult.Errors.ToArray());
            }

            return DomainOperationResult<SaveGameContents>.Success(
                new SaveGameContents(snapshot, season, negotiationsResult.Value));
        }
        catch (ArgumentException exception)
        {
            return Fail<SaveGameContents>(InvalidFieldCode, exception.Message);
        }
    }

    private static DomainOperationResult<List<Franchise>> BuildFranchises(IReadOnlyList<FranchiseEnvelope> envelopes)
    {
        var franchises = new List<Franchise>(envelopes.Count);

        foreach (var envelope in envelopes)
        {
            var result = Franchise.Create(new FranchiseId(envelope.FranchiseId), envelope.Name);
            if (result.IsFailure)
            {
                return DomainOperationResult<List<Franchise>>.Failure(result.Errors.ToArray());
            }

            franchises.Add(result.Value);
        }

        return DomainOperationResult<List<Franchise>>.Success(franchises);
    }

    private static DomainOperationResult<List<Player>> BuildPlayers(IReadOnlyList<PlayerEnvelope> envelopes)
    {
        var players = new List<Player>(envelopes.Count);

        foreach (var envelope in envelopes)
        {
            if (!Enum.TryParse<Position>(envelope.Position, out var position) || !Enum.IsDefined(position))
            {
                return Fail<List<Player>>(
                    InvalidFieldCode,
                    $"Player '{envelope.PlayerId}' declares an unknown position '{envelope.Position}'.");
            }

            if (!DateOnly.TryParseExact(envelope.BirthDate, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var birthDate))
            {
                return Fail<List<Player>>(
                    InvalidFieldCode,
                    $"Player '{envelope.PlayerId}' declares '{envelope.BirthDate}' as a birth date. Expected the form {DateFormat}.");
            }

            var result = Player.Create(
                new PlayerId(envelope.PlayerId),
                envelope.FullName,
                position,
                new PlayerRating(envelope.Overall),
                birthDate,
                envelope.SeasonsOfService,
                envelope.InjuryDescription is null ? null : new Injury(envelope.InjuryDescription));

            if (result.IsFailure)
            {
                return DomainOperationResult<List<Player>>.Failure(result.Errors.ToArray());
            }

            players.Add(result.Value);
        }

        return DomainOperationResult<List<Player>>.Success(players);
    }

    private static DomainOperationResult<List<Team>> BuildTeams(IReadOnlyList<TeamEnvelope> envelopes, RosterSizeLimits rosterLimits)
    {
        var teams = new List<Team>(envelopes.Count);

        foreach (var envelope in envelopes)
        {
            var result = Team.Create(
                new TeamId(envelope.TeamId),
                new FranchiseId(envelope.FranchiseId),
                envelope.Name,
                rosterLimits,
                envelope.PlayerIds.Select(playerId => new PlayerId(playerId)));

            if (result.IsFailure)
            {
                return DomainOperationResult<List<Team>>.Failure(result.Errors.ToArray());
            }

            teams.Add(result.Value);
        }

        return DomainOperationResult<List<Team>>.Success(teams);
    }

    private static DomainOperationResult<League> BuildLeague(LeagueEnvelope envelope, IReadOnlyList<Team> teams)
    {
        var alignmentResult = BuildAlignment(envelope.Conferences);
        if (alignmentResult.IsFailure)
        {
            return DomainOperationResult<League>.Failure(alignmentResult.Errors.ToArray());
        }

        var leagueResult = League.Create(
            new LeagueId(envelope.LeagueId),
            envelope.Name,
            teams.Select(team => team.Id),
            alignmentResult.Value);

        if (leagueResult.IsFailure)
        {
            return leagueResult;
        }

        var league = leagueResult.Value;

        foreach (var historyEnvelope in envelope.History)
        {
            var entry = new SeasonHistoryEntry(
                new Season(historyEnvelope.SeasonYear),
                historyEnvelope.ChampionTeamId is null ? null : new TeamId(historyEnvelope.ChampionTeamId),
                historyEnvelope.FinalStandings
                    .Select(row => new SeasonHistoryTeamRecord(
                        new TeamId(row.TeamId),
                        row.Position,
                        new TeamRecord(row.Wins, row.Losses),
                        row.PointsFor,
                        row.PointsAgainst))
                    .ToList());

            var recordResult = league.RecordSeason(entry);
            if (recordResult.IsFailure)
            {
                return DomainOperationResult<League>.Failure(recordResult.Errors.ToArray());
            }
        }

        return DomainOperationResult<League>.Success(league);
    }

    private static DomainOperationResult<LeagueAlignment> BuildAlignment(IReadOnlyList<LeagueConferenceEnvelope> envelopes)
    {
        if (envelopes.Count == 0)
        {
            return DomainOperationResult<LeagueAlignment>.Success(LeagueAlignment.Flat);
        }

        var conferences = envelopes
            .Select(conference => new LeagueConference(
                conference.Name,
                conference.Divisions.Select(division => new LeagueDivision(
                    division.Name,
                    division.TeamIds.Select(teamId => new TeamId(teamId))))))
            .ToList();

        return LeagueAlignment.Create(conferences);
    }

    private static DomainOperationResult<List<Contract>> BuildContracts(IReadOnlyList<string> contractsJson)
    {
        var contracts = new List<Contract>(contractsJson.Count);
        var serializer = new ContractSerializer();

        foreach (var contractJson in contractsJson)
        {
            var result = serializer.Deserialize(contractJson);
            if (result.IsFailure)
            {
                return DomainOperationResult<List<Contract>>.Failure(result.Errors.ToArray());
            }

            contracts.Add(result.Value);
        }

        return DomainOperationResult<List<Contract>>.Success(contracts);
    }

    private static DomainOperationResult<TransactionLedger> BuildLedger(TransactionLedgerEnvelope envelope)
    {
        var entries = new List<TransactionEntry>(envelope.Entries.Count);

        foreach (var entryEnvelope in envelope.Entries)
        {
            if (!Enum.TryParse<TransactionKind>(entryEnvelope.Kind, out var kind) || !Enum.IsDefined(kind))
            {
                return Fail<TransactionLedger>(
                    InvalidFieldCode,
                    $"'{entryEnvelope.Kind}' is not a transaction kind this build knows.");
            }

            if (!DateTimeOffset.TryParse(
                    entryEnvelope.RecordedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var recordedAt))
            {
                return Fail<TransactionLedger>(
                    InvalidFieldCode,
                    $"'{entryEnvelope.RecordedAt}' is not a timestamp ledger entry '{entryEnvelope.TransactionId}' can be read back from.");
            }

            SigningRouteKind? signingRoute = null;
            if (entryEnvelope.SigningRoute is not null)
            {
                if (!Enum.TryParse<SigningRouteKind>(entryEnvelope.SigningRoute, out var parsedRoute))
                {
                    return Fail<TransactionLedger>(
                        InvalidFieldCode,
                        $"'{entryEnvelope.SigningRoute}' is not a signing route this build knows.");
                }

                signingRoute = parsedRoute;
            }

            try
            {
                entries.Add(new TransactionEntry(
                    new TransactionId(entryEnvelope.TransactionId),
                    entryEnvelope.Sequence,
                    recordedAt,
                    kind,
                    new Season(entryEnvelope.SeasonYear),
                    entryEnvelope.TeamId is null ? null : new TeamId(entryEnvelope.TeamId),
                    entryEnvelope.PlayerId is null ? null : new PlayerId(entryEnvelope.PlayerId),
                    entryEnvelope.ContractId is null ? null : new ContractId(entryEnvelope.ContractId),
                    entryEnvelope.Amount is null ? null : new Money(entryEnvelope.Amount.Value),
                    entryEnvelope.Reason,
                    entryEnvelope.FranchiseId is null ? null : new FranchiseId(entryEnvelope.FranchiseId),
                    entryEnvelope.CounterpartyFranchiseId is null ? null : new FranchiseId(entryEnvelope.CounterpartyFranchiseId),
                    entryEnvelope.DraftPickId is null ? null : new DraftPickId(entryEnvelope.DraftPickId),
                    signingRoute));
            }
            catch (ArgumentException exception)
            {
                return Fail<TransactionLedger>(InvalidFieldCode, exception.Message);
            }
        }

        return TransactionLedger.Rehydrate(new SystemClock(), entries);
    }

    private static DomainOperationResult<Dictionary<string, Negotiation>> BuildNegotiations(IReadOnlyList<string> negotiationsJson)
    {
        var negotiations = new Dictionary<string, Negotiation>();
        var serializer = new NegotiationSerializer();

        foreach (var negotiationJson in negotiationsJson)
        {
            var result = serializer.Deserialize(negotiationJson);
            if (result.IsFailure)
            {
                return DomainOperationResult<Dictionary<string, Negotiation>>.Failure(result.Errors.ToArray());
            }

            negotiations[result.Value.PlayerId.Value] = result.Value;
        }

        return DomainOperationResult<Dictionary<string, Negotiation>>.Success(negotiations);
    }

    private static LeagueEnvelope ToEnvelope(League league) =>
        new(
            league.Id.Value,
            league.Name,
            league.Alignment.IsFlat
                ? []
                : league.Alignment.Conferences
                    .Select(conference => new LeagueConferenceEnvelope(
                        conference.Name,
                        conference.Divisions
                            .Select(division => new LeagueDivisionEnvelope(
                                division.Name,
                                division.TeamIds.Select(teamId => teamId.Value).ToList()))
                            .ToList()))
                    .ToList(),
            league.History
                .Select(entry => new SeasonHistoryEntryEnvelope(
                    entry.Season.Year,
                    entry.ChampionTeamId?.Value,
                    entry.FinalStandings
                        .Select(row => new SeasonHistoryTeamRecordEnvelope(
                            row.TeamId.Value,
                            row.Position,
                            row.Record.Wins,
                            row.Record.Losses,
                            row.PointsFor,
                            row.PointsAgainst))
                        .ToList()))
                .ToList());

    private static FranchiseEnvelope ToEnvelope(Franchise franchise) => new(franchise.Id.Value, franchise.Name);

    private static TeamEnvelope ToEnvelope(Team team) =>
        new(
            team.Id.Value,
            team.FranchiseId.Value,
            team.Name,
            team.PlayerIds.Select(playerId => playerId.Value).OrderBy(value => value, StringComparer.Ordinal).ToList());

    private static PlayerEnvelope ToEnvelope(Player player) =>
        new(
            player.Id.Value,
            player.FullName,
            player.Position.ToString(),
            player.Rating.Overall,
            player.BirthDate.ToString(DateFormat, CultureInfo.InvariantCulture),
            player.SeasonsOfService,
            player.CurrentInjury?.Description);

    private static TransactionLedgerEnvelope ToEnvelope(TransactionLedger ledger) =>
        new(ledger.Entries.Select(ToEnvelope).ToList());

    private static TransactionEntryEnvelope ToEnvelope(TransactionEntry entry) =>
        new(
            entry.Id.Value,
            entry.Sequence,
            entry.RecordedAt.ToString("O", CultureInfo.InvariantCulture),
            entry.Kind.ToString(),
            entry.Season.Year,
            entry.TeamId?.Value,
            entry.PlayerId?.Value,
            entry.ContractId?.Value,
            entry.Amount?.SmallestUnits,
            entry.Reason,
            entry.FranchiseId?.Value,
            entry.CounterpartyFranchiseId?.Value,
            entry.DraftPickId?.Value,
            entry.SigningRoute?.ToString());

    private static DomainOperationResult<T> Fail<T>(string code, string message) =>
        DomainOperationResult<T>.Failure(new DomainError(code, message));
}
