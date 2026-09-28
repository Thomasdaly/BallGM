using System.Text.Json;
using System.Text.Json.Serialization;
using BallGM.Application.Leagues;
using BallGM.Application.Players;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Rulesets;
using BallGM.Infrastructure.Time;
using BallGM.Rules.Configuration;
using BallGM.Rules.DraftAssets;

namespace BallGM.Infrastructure.LeaguePacks;

/// <summary>
/// Loads a league from a league pack file (<see cref="LeaguePackEnvelope"/>) plus the ruleset file
/// it names, instead of from the fixture's static tables. This is the content half of moddability:
/// the ruleset file already made the rules swappable, and a pack does the same for the franchises,
/// teams, players, and contracts the rules are applied to.
/// <para>
/// The pack is untrusted input. Every problem found is collected and returned as a structured
/// failure rather than thrown, and nothing reaches an aggregate factory until the whole pack has been
/// read — so a pack with twelve mistakes reports twelve, not the first one.
/// </para>
/// <para>
/// A pack describes a league at the start of a season: every franchise owns its own draft picks
/// across the ruleset's tradable horizon, with no pick history. A pick history is league state a save
/// carries, not content a pack authors.
/// </para>
/// </summary>
public sealed class LeaguePackDataSource : ILeagueDataSource
{
    public const int CurrentSchemaVersion = 1;

    private const string MissingFileCode = "league_pack.file_missing";
    private const string UnreadableFileCode = "league_pack.file_unreadable";
    private const string MalformedFileCode = "league_pack.malformed_file";
    private const string UnsupportedSchemaVersionCode = "league_pack.unsupported_schema_version";
    private const string InvalidFieldCode = "league_pack.invalid_field";
    private const string UnknownTeamCode = "league_pack.unknown_team";
    private const string DuplicateTeamCode = "league_pack.duplicate_team";
    private const string ContractMismatchCode = "league_pack.contract_mismatch";
    private const string InvalidImageCode = "league_pack.invalid_image_path";
    private const string InvalidPickTradeCode = "league_pack.invalid_pick_trade";

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // Same reasoning as the ruleset file: a field this build does not know is content this
        // build would silently drop.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Ledger timestamps: a fixed start and step, so the same pack loads the same ledger.</summary>
    private static readonly TimeSpan LedgerStep = TimeSpan.FromMinutes(1);

    private readonly string _packFilePath;
    private readonly LeagueRulesetSerializer _rulesetSerializer = new();

    public LeaguePackDataSource(string packFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packFilePath);
        _packFilePath = packFilePath;
    }

    public DomainOperationResult<LeagueSnapshot> Load()
    {
        var packText = ReadFile(_packFilePath, "league pack");
        if (packText.IsFailure)
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(packText.Errors.ToArray());
        }

        LeaguePackEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<LeaguePackEnvelope>(packText.Value, Options);
        }
        catch (JsonException exception)
        {
            return Fail(MalformedFileCode, $"The league pack at '{_packFilePath}' is not valid: {exception.Message}");
        }

        if (envelope is null)
        {
            return Fail(MalformedFileCode, $"The league pack at '{_packFilePath}' is empty.");
        }

        if (envelope.SchemaVersion != CurrentSchemaVersion)
        {
            return Fail(
                UnsupportedSchemaVersionCode,
                $"The league pack is schema version {envelope.SchemaVersion}; this build reads version {CurrentSchemaVersion}.");
        }

        var rulesetPath = envelope.RulesetFile is null
            ? FixtureLeagueDataSource.DefaultRulesetFilePath
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_packFilePath)) ?? string.Empty, envelope.RulesetFile);

        var rulesetText = ReadFile(rulesetPath, "league ruleset");
        if (rulesetText.IsFailure)
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(rulesetText.Errors.ToArray());
        }

        var rulesetResult = _rulesetSerializer.Deserialize(rulesetText.Value);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(rulesetResult.Errors.ToArray());
        }

        var packDirectory = Path.GetDirectoryName(Path.GetFullPath(_packFilePath)) ?? string.Empty;
        return Build(envelope, rulesetResult.Value, packDirectory);
    }

    private static DomainOperationResult<LeagueSnapshot> Build(LeaguePackEnvelope envelope, LeagueRuleset ruleset, string packDirectory)
    {
        var errors = new List<DomainError>();

        if (string.IsNullOrWhiteSpace(envelope.Name))
        {
            errors.Add(Invalid("name", "The league pack must state a name."));
        }

        if (envelope.Season is not int seasonYear || seasonYear < 1)
        {
            errors.Add(Invalid("season", "The league pack must state the season it starts in, as a positive year."));
            seasonYear = 1;
        }

        var season = new Season(seasonYear);
        var teamPlans = envelope.Teams ?? [];
        var playerPlans = envelope.Players ?? [];

        if (teamPlans.Count < 2)
        {
            errors.Add(Invalid("teams", "A league pack needs at least two teams."));
        }

        var teamKeys = new HashSet<string>(StringComparer.Ordinal);
        var logosByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (plan, index) in teamPlans.Select((plan, index) => (plan, index)))
        {
            if (string.IsNullOrWhiteSpace(plan.Key) || string.IsNullOrWhiteSpace(plan.Name) || string.IsNullOrWhiteSpace(plan.FranchiseName))
            {
                errors.Add(Invalid($"teams[{index}]", "Every team needs a key, a name, and a franchise name."));
                continue;
            }

            if (!teamKeys.Add(plan.Key))
            {
                errors.Add(new DomainError(DuplicateTeamCode, $"Team key '{plan.Key}' appears more than once."));
            }

            if (ResolveImage(plan.Logo, packDirectory, $"teams[{index}] ('{plan.Key}') logo", errors) is string logo)
            {
                logosByKey[plan.Key] = logo;
            }
        }

        var pickTrades = envelope.PickTrades ?? [];
        foreach (var (plan, index) in pickTrades.Select((plan, index) => (plan, index)))
        {
            ValidatePickTrade(plan, index, teamKeys, season, ruleset.DraftRules, errors);
        }

        var parsedPlayers = new List<ParsedPlayer>(playerPlans.Count);
        foreach (var (plan, index) in playerPlans.Select((plan, index) => (plan, index)))
        {
            var parsed = ParsePlayer(plan, index, teamKeys, season, packDirectory, errors);
            if (parsed is not null)
            {
                parsedPlayers.Add(parsed);
            }
        }

        if (errors.Count > 0)
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(errors.ToArray());
        }

        return BuildLeague(envelope, ruleset, season, teamPlans, parsedPlayers, logosByKey);
    }

    private static DomainOperationResult<LeagueSnapshot> BuildLeague(
        LeaguePackEnvelope envelope,
        LeagueRuleset ruleset,
        Season season,
        IReadOnlyList<LeaguePackTeamEnvelope> teamPlans,
        IReadOnlyList<ParsedPlayer> parsedPlayers,
        IReadOnlyDictionary<string, string> logosByKey)
    {
        var errors = new List<DomainError>();
        var ledger = new TransactionLedger(new SteppingClock(
            new DateTimeOffset(season.Year, 7, 1, 9, 0, 0, TimeSpan.Zero),
            LedgerStep));

        var players = new List<Player>(parsedPlayers.Count);
        var portraits = new Dictionary<string, string>(StringComparer.Ordinal);
        var careers = new Dictionary<string, IReadOnlyList<CareerSeasonLine>>(StringComparer.Ordinal);
        var logos = new Dictionary<string, string>(StringComparer.Ordinal);
        var repeaters = new HashSet<string>(StringComparer.Ordinal);
        var playersByTeam = teamPlans.ToDictionary(plan => plan.Key!, _ => new List<(Player Player, ParsedPlayer Plan)>(), StringComparer.Ordinal);

        foreach (var plan in parsedPlayers)
        {
            var playerResult = Player.Create(
                new PlayerId(SortableId.NewId()),
                plan.Name,
                plan.Position,
                plan.Rating,
                plan.BirthDate,
                plan.SeasonsOfService,
                plan.Injury is null ? null : new Injury(plan.Injury));

            if (playerResult.IsFailure)
            {
                errors.AddRange(playerResult.Errors);
                continue;
            }

            players.Add(playerResult.Value);
            if (plan.PortraitPath is not null)
            {
                portraits[playerResult.Value.Id.Value] = plan.PortraitPath;
            }

            if (plan.Career.Count > 0)
            {
                careers[playerResult.Value.Id.Value] = plan.Career;
            }

            if (plan.TeamKey is not null)
            {
                playersByTeam[plan.TeamKey].Add((playerResult.Value, plan));
            }
        }

        var franchises = new List<Franchise>(teamPlans.Count);
        var teams = new List<Team>(teamPlans.Count);
        var teamIdsByKey = new Dictionary<string, TeamId>(StringComparer.Ordinal);
        var franchisesByKey = new Dictionary<string, Franchise>(StringComparer.Ordinal);
        var contracts = new List<Contract>();

        foreach (var teamPlan in teamPlans)
        {
            var franchiseResult = Franchise.Create(new FranchiseId(SortableId.NewId()), teamPlan.FranchiseName!);
            if (franchiseResult.IsFailure)
            {
                errors.AddRange(franchiseResult.Errors);
                continue;
            }

            var roster = playersByTeam[teamPlan.Key!];
            var teamResult = Team.Create(
                new TeamId(SortableId.NewId()),
                franchiseResult.Value.Id,
                teamPlan.Name!,
                ruleset.RosterLimits,
                roster.Select(entry => entry.Player.Id));

            if (teamResult.IsFailure)
            {
                errors.AddRange(teamResult.Errors);
                continue;
            }

            franchises.Add(franchiseResult.Value);
            teams.Add(teamResult.Value);
            teamIdsByKey[teamPlan.Key!] = teamResult.Value.Id;
            franchisesByKey[teamPlan.Key!] = franchiseResult.Value;
            if (logosByKey.TryGetValue(teamPlan.Key!, out var logo))
            {
                logos[teamResult.Value.Id.Value] = logo;
            }

            if (teamPlan.TaxRepeater)
            {
                repeaters.Add(teamResult.Value.Id.Value);
            }

            foreach (var (player, plan) in roster)
            {
                var contractResult = CreateContract(teamResult.Value.Id, player.Id, season, plan.Contract!);
                if (contractResult.IsFailure)
                {
                    errors.AddRange(contractResult.Errors);
                    continue;
                }

                contracts.Add(contractResult.Value);
                ledger.Record(
                    TransactionKind.ContractSigned,
                    season,
                    teamResult.Value.Id,
                    $"{player.FullName} signed a {contractResult.Value.Terms.Count}-season contract.",
                    player.Id,
                    contractResult.Value.Id,
                    contractResult.Value.Terms[0].Compensation);
            }
        }

        if (errors.Count > 0)
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(errors.ToArray());
        }

        var conferencesResult = BuildConferences(envelope.Conferences ?? [], teamIdsByKey);
        if (conferencesResult.IsFailure)
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(conferencesResult.Errors.ToArray());
        }

        var alignmentResult = LeagueAlignment.Create(conferencesResult.Value);
        if (alignmentResult.IsFailure)
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(alignmentResult.Errors.ToArray());
        }

        var leagueResult = League.Create(
            new LeagueId(SortableId.NewId()),
            envelope.Name!,
            teams.Select(team => team.Id),
            alignmentResult.Value);

        if (leagueResult.IsFailure)
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(leagueResult.Errors.ToArray());
        }

        var book = new DraftAssetBook(leagueResult.Value.Id);
        if (ruleset.DraftRules.HasDraft)
        {
            var registerResult = FixtureDraftAssets.RegisterPicks(book, leagueResult.Value.Id, season, franchises, ruleset.DraftRules);
            if (registerResult.IsFailure)
            {
                return DomainOperationResult<LeagueSnapshot>.Failure(registerResult.Errors.ToArray());
            }

            var pickTradeResult = ApplyPickTrades(book, season, envelope.PickTrades ?? [], franchisesByKey, ruleset.DraftRules, ledger);
            if (pickTradeResult.IsFailure)
            {
                return DomainOperationResult<LeagueSnapshot>.Failure(pickTradeResult.Errors.ToArray());
            }
        }

        return DomainOperationResult<LeagueSnapshot>.Success(
            new LeagueSnapshot(
                leagueResult.Value,
                season,
                franchises,
                teams,
                players,
                contracts,
                book,
                ledger,
                ruleset.ToConfiguration())
            {
                Artwork = new LeagueArtwork(logos, portraits),
                CareerHistory = careers,
                TaxRepeaterTeams = repeaters,
            });
    }

    private static DomainOperationResult<IReadOnlyList<LeagueConference>> BuildConferences(
        IReadOnlyList<LeaguePackConferenceEnvelope> conferencePlans,
        IReadOnlyDictionary<string, TeamId> teamIdsByKey)
    {
        var errors = new List<DomainError>();
        var conferences = new List<LeagueConference>(conferencePlans.Count);

        foreach (var (conferencePlan, conferenceIndex) in conferencePlans.Select((plan, index) => (plan, index)))
        {
            if (string.IsNullOrWhiteSpace(conferencePlan.Name))
            {
                errors.Add(Invalid($"conferences[{conferenceIndex}]", "Every conference needs a name."));
                continue;
            }

            var divisions = new List<LeagueDivision>();
            foreach (var divisionPlan in conferencePlan.Divisions ?? [])
            {
                if (string.IsNullOrWhiteSpace(divisionPlan.Name))
                {
                    errors.Add(Invalid($"conferences[{conferenceIndex}]", $"A division in '{conferencePlan.Name}' has no name."));
                    continue;
                }

                var teamIds = new List<TeamId>();
                foreach (var key in divisionPlan.Teams ?? [])
                {
                    if (teamIdsByKey.TryGetValue(key, out var teamId))
                    {
                        teamIds.Add(teamId);
                    }
                    else
                    {
                        errors.Add(new DomainError(UnknownTeamCode, $"Division '{divisionPlan.Name}' names team '{key}', which the pack does not define."));
                    }
                }

                divisions.Add(new LeagueDivision(divisionPlan.Name, teamIds));
            }

            conferences.Add(new LeagueConference(conferencePlan.Name, divisions));
        }

        return errors.Count > 0
            ? DomainOperationResult<IReadOnlyList<LeagueConference>>.Failure(errors.ToArray())
            : DomainOperationResult<IReadOnlyList<LeagueConference>>.Success(conferences);
    }

    private static ParsedPlayer? ParsePlayer(
        LeaguePackPlayerEnvelope plan,
        int index,
        IReadOnlySet<string> teamKeys,
        Season season,
        string packDirectory,
        List<DomainError> errors)
    {
        var field = $"players[{index}]";
        var label = string.IsNullOrWhiteSpace(plan.Name) ? field : $"{field} ('{plan.Name}')";
        var errorCount = errors.Count;

        if (string.IsNullOrWhiteSpace(plan.Name))
        {
            errors.Add(Invalid(field, "Every player needs a name."));
        }

        if (!Enum.TryParse<Position>(plan.Position, ignoreCase: false, out var position) || !Enum.IsDefined(position))
        {
            errors.Add(Invalid(label, $"Position '{plan.Position}' is not one of {string.Join(", ", Enum.GetNames<Position>())}."));
        }

        if (plan.BirthDate is not DateOnly birthDate || birthDate.Year >= season.Year)
        {
            errors.Add(Invalid(label, "A birth date before the pack's season is required."));
            birthDate = default;
        }

        if (plan.SeasonsOfService is not int service || service < 0)
        {
            errors.Add(Invalid(label, "Seasons of service must be stated and cannot be negative."));
            service = 0;
        }

        var rating = ParseRating(plan.Ratings, label, errors);

        if (plan.Team is not null && !teamKeys.Contains(plan.Team))
        {
            errors.Add(new DomainError(UnknownTeamCode, $"{label} is on team '{plan.Team}', which the pack does not define."));
        }

        // A rostered player without a contract would be on a roster and on nobody's payroll; a free
        // agent with one would be under contract to nobody. Both are content mistakes, not states.
        if (plan.Team is not null && plan.Contract is null)
        {
            errors.Add(new DomainError(ContractMismatchCode, $"{label} is on a roster but has no contract."));
        }
        else if (plan.Team is null && plan.Contract is not null)
        {
            errors.Add(new DomainError(ContractMismatchCode, $"{label} is a free agent but carries a contract."));
        }

        if (plan.Contract is not null)
        {
            ValidateContract(plan.Contract, label, errors);
        }

        var portrait = ResolveImage(plan.Portrait, packDirectory, $"{label} portrait", errors);
        var career = ParseCareer(plan.Career, label, errors);

        if (errors.Count > errorCount || rating is null)
        {
            return null;
        }

        return new ParsedPlayer(plan.Name!, position, rating, birthDate, service, plan.Team, plan.Injury, plan.Contract, portrait, career);
    }

    private static IReadOnlyList<CareerSeasonLine> ParseCareer(
        IReadOnlyList<LeaguePackCareerSeasonEnvelope>? career,
        string label,
        List<DomainError> errors)
    {
        if (career is null)
        {
            return [];
        }

        var lines = new List<CareerSeasonLine>(career.Count);
        foreach (var (season, index) in career.Select((season, index) => (season, index)))
        {
            int?[] totals = [season.GamesPlayed, season.Minutes, season.Points, season.Rebounds, season.Assists];
            if (string.IsNullOrWhiteSpace(season.Season) || totals.Any(total => total is not int value || value < 0))
            {
                errors.Add(Invalid(
                    $"{label} career[{index}]",
                    "A career season needs a season label and non-negative gamesPlayed, minutes, points, rebounds, and assists."));
                continue;
            }

            if (season.Metrics is { } metrics && metrics.Any(metric => string.IsNullOrWhiteSpace(metric.Key) || !double.IsFinite(metric.Value)))
            {
                errors.Add(Invalid($"{label} career[{index}]", "Every career metric needs a stat code and a finite number."));
                continue;
            }

            lines.Add(new CareerSeasonLine(
                season.Season.Trim(),
                string.IsNullOrWhiteSpace(season.Team) ? null : season.Team.Trim(),
                season.GamesPlayed!.Value,
                season.Minutes!.Value,
                season.Points!.Value,
                season.Rebounds!.Value,
                season.Assists!.Value,
                Metrics: season.Metrics is { Count: > 0 } stated ? new Dictionary<string, double>(stated, StringComparer.Ordinal) : null));
        }

        return lines;
    }

    private static PlayerRating? ParseRating(LeaguePackRatingEnvelope? ratings, string label, List<DomainError> errors)
    {
        if (ratings is null)
        {
            errors.Add(Invalid(label, "Ratings are required."));
            return null;
        }

        int?[] values = [ratings.Height, ratings.Speed, ratings.Strength, ratings.Passing, ratings.LateralQuickness];
        if (values.Any(value => value is not int rating || rating < PlayerRating.MinimumOverall || rating > PlayerRating.MaximumOverall))
        {
            errors.Add(Invalid(
                label,
                $"All five ratings (height, speed, strength, passing, lateralQuickness) must be stated, each between {PlayerRating.MinimumOverall} and {PlayerRating.MaximumOverall}."));
            return null;
        }

        return new PlayerRating(values[0]!.Value, values[1]!.Value, values[2]!.Value, values[3]!.Value, values[4]!.Value);
    }

    private static void ValidateContract(LeaguePackContractEnvelope contract, string label, List<DomainError> errors)
    {
        if (contract.Salaries is null || contract.Salaries.Count == 0 || contract.Salaries.Any(salary => salary <= 0))
        {
            errors.Add(Invalid(label, "A contract needs at least one season, and every season's salary must be positive."));
        }

        if (contract.FinalSeasonOption is not null
            && (!Enum.TryParse<ContractOptionKind>(contract.FinalSeasonOption, ignoreCase: false, out var option) || !Enum.IsDefined(option)))
        {
            errors.Add(Invalid(label, $"Final-season option '{contract.FinalSeasonOption}' is not one of {string.Join(", ", Enum.GetNames<ContractOptionKind>())}."));
        }
    }

    private static DomainOperationResult<Contract> CreateContract(
        TeamId teamId,
        PlayerId playerId,
        Season season,
        LeaguePackContractEnvelope plan)
    {
        var option = plan.FinalSeasonOption is null ? ContractOptionKind.None : Enum.Parse<ContractOptionKind>(plan.FinalSeasonOption);
        var salaries = plan.Salaries!;
        var terms = new List<ContractSeasonTerm>(salaries.Count);

        for (var offset = 0; offset < salaries.Count; offset++)
        {
            var isOptionSeason = offset == salaries.Count - 1 && option != ContractOptionKind.None;
            var compensation = new Money(salaries[offset]);
            terms.Add(new ContractSeasonTerm(
                new Season(season.Year + offset),
                compensation,
                isOptionSeason ? Money.Zero : compensation,
                isOptionSeason ? option : ContractOptionKind.None));
        }

        return Contract.Create(new ContractId(SortableId.NewId()), teamId, playerId, terms);
    }

    private static void ValidatePickTrade(
        LeaguePackPickTradeEnvelope plan,
        int index,
        IReadOnlySet<string> teamKeys,
        Season season,
        DraftRules draftRules,
        List<DomainError> errors)
    {
        var label = $"pickTrades[{index}]";
        void Fail(string message) => errors.Add(new DomainError(InvalidPickTradeCode, $"{label}: {message}"));

        if (!draftRules.HasDraft)
        {
            Fail("this league's ruleset holds no draft, so it has no picks to trade.");
            return;
        }

        var lastYear = season.Year + draftRules.TradableFutureDraftHorizon;
        if (plan.Draft is not int draft || draft < season.Year || draft > lastYear)
        {
            Fail($"the draft must be stated and fall between {season.Year} and {lastYear}, this ruleset's tradable window.");
        }

        if (plan.Round is not int round || round < 1 || round > draftRules.RoundCount)
        {
            Fail($"the round must be stated and fall between 1 and {draftRules.RoundCount}.");
        }

        foreach (var (key, field) in new[] { (plan.Original, "original"), (plan.Owner, "owner"), (plan.OwedTo, "owedTo"), (plan.SwapHolder, "swapHolder") })
        {
            if (key is not null && !teamKeys.Contains(key))
            {
                errors.Add(new DomainError(UnknownTeamCode, $"{label}: {field} names team '{key}', which the pack does not define."));
            }
        }

        if (plan.Original is null)
        {
            Fail("the team the pick originally belonged to must be stated.");
        }

        var statements = new[] { plan.Owner, plan.OwedTo, plan.SwapHolder }.Count(value => value is not null);
        if (statements != 1)
        {
            Fail("state exactly one of owner (traded outright), owedTo (an obligation), or swapHolder (a swap right).");
        }

        if (plan.OwedTo is null && (plan.ProtectedTop is not null || plan.Fallback is not null || plan.FallbackRound is not null))
        {
            Fail("protectedTop, fallback, and fallbackRound only describe an owedTo obligation.");
        }

        if (plan.ProtectedTop is { } protectedTop && protectedTop.Any(level => level < 1))
        {
            Fail("every protectedTop level must be 1 or greater.");
        }

        if (plan.Fallback is not null
            && (!Enum.TryParse<PickProtectionFallbackKind>(plan.Fallback, ignoreCase: false, out var fallback) || !Enum.IsDefined(fallback)))
        {
            Fail($"fallback '{plan.Fallback}' is not one of {string.Join(", ", Enum.GetNames<PickProtectionFallbackKind>())}.");
        }
    }

    /// <summary>
    /// Writes a pack's traded picks onto a freshly registered board: outright transfers first, then
    /// obligations and swap rights, each recorded on the ledger. Transfers skip the trade engine's
    /// retention rule on purpose — a pack states ownership as it stands, and replaying real history
    /// one transfer at a time can pass through states the retention rule would refuse on the way to
    /// a final board it accepts. Encumbrances still go through <see cref="PickOwnershipRules"/>.
    /// </summary>
    private static DomainOperationResult ApplyPickTrades(
        DraftAssetBook book,
        Season season,
        IReadOnlyList<LeaguePackPickTradeEnvelope> plans,
        IReadOnlyDictionary<string, Franchise> franchisesByKey,
        DraftRules draftRules,
        TransactionLedger ledger)
    {
        var errors = new List<DomainError>();
        var rules = new PickOwnershipRules();
        var indexed = plans.Select((plan, index) => (plan, index)).ToList();

        foreach (var (plan, index) in indexed.Where(entry => entry.plan.Owner is not null))
        {
            var original = franchisesByKey[plan.Original!];
            var owner = franchisesByKey[plan.Owner!];
            var pick = book.Find(new Season(plan.Draft!.Value), plan.Round!.Value, original.Id);
            if (pick is null || book.Ownership(pick.Id)!.CurrentOwnerFranchiseId != original.Id)
            {
                errors.Add(new DomainError(InvalidPickTradeCode, $"pickTrades[{index}]: the {plan.Draft} round {plan.Round} pick of {plan.Original} is stated as traded more than once."));
                continue;
            }

            if (owner.Id == original.Id)
            {
                errors.Add(new DomainError(InvalidPickTradeCode, $"pickTrades[{index}]: the {plan.Draft} round {plan.Round} pick cannot be traded to the team it already belongs to."));
                continue;
            }

            var transfer = book.Transfer(pick.Id, owner.Id);
            if (transfer.IsFailure)
            {
                errors.AddRange(transfer.Errors);
                continue;
            }

            ledger.RecordPickEvent(
                TransactionKind.DraftPickTransferred,
                season,
                original.Id,
                pick.Id,
                $"The {plan.Draft} round {plan.Round} pick belongs to {owner.Name}{NoteSuffix(plan.Note)}",
                owner.Id);
        }

        foreach (var (plan, index) in indexed.Where(entry => entry.plan.Owner is null))
        {
            var original = franchisesByKey[plan.Original!];
            var draft = new Season(plan.Draft!.Value);
            var pick = book.Find(draft, plan.Round!.Value, original.Id);
            if (pick is null)
            {
                errors.Add(new DomainError(InvalidPickTradeCode, $"pickTrades[{index}]: no {plan.Draft} round {plan.Round} pick of {plan.Original} is registered."));
                continue;
            }

            var controller = book.Ownership(pick.Id)!.CurrentOwnerFranchiseId;
            PickEncumbrance encumbrance;
            string reason;
            FranchiseId counterparty;

            if (plan.OwedTo is not null)
            {
                var beneficiary = franchisesByKey[plan.OwedTo];
                var fallbackKind = plan.Fallback is null ? PickProtectionFallbackKind.ConveysUnprotected : Enum.Parse<PickProtectionFallbackKind>(plan.Fallback);
                var fallback = PickProtectionFallback.Rebuild(fallbackKind, plan.FallbackRound);
                if (fallback.IsFailure)
                {
                    errors.AddRange(fallback.Errors.Select(error => new DomainError(error.Code, $"pickTrades[{index}]: {error.Message}")));
                    continue;
                }

                var protection = plan.ProtectedTop is { Count: > 0 } levels
                    ? PickProtection.TopSelections(levels, fallback.Value)
                    : DomainOperationResult<PickProtection>.Success(PickProtection.Unprotected);
                if (protection.IsFailure)
                {
                    errors.AddRange(protection.Errors.Select(error => new DomainError(error.Code, $"pickTrades[{index}]: {error.Message}")));
                    continue;
                }

                encumbrance = new PickObligation(new PickEncumbranceId(SortableId.NewId()), beneficiary.Id, protection.Value);
                counterparty = beneficiary.Id;
                reason = $"The {plan.Draft} round {plan.Round} pick is owed to {beneficiary.Name}{NoteSuffix(plan.Note)}";
            }
            else
            {
                var holder = franchisesByKey[plan.SwapHolder!];
                var counterpart = book.Find(draft, plan.Round!.Value, holder.Id);
                if (counterpart is null)
                {
                    errors.Add(new DomainError(InvalidPickTradeCode, $"pickTrades[{index}]: no {plan.Draft} round {plan.Round} pick of {plan.SwapHolder} is registered to swap."));
                    continue;
                }

                encumbrance = new SwapRight(new PickEncumbranceId(SortableId.NewId()), holder.Id, counterpart.Id);
                counterparty = holder.Id;
                reason = $"{holder.Name} may swap their {plan.Draft} round {plan.Round} selection for this one{NoteSuffix(plan.Note)}";
            }

            var validation = rules.ValidateEncumbrance(book, pick.Id, controller, encumbrance, season, draftRules);
            if (validation.IsFailure)
            {
                errors.AddRange(validation.Errors.Select(error => new DomainError(error.Code, $"pickTrades[{index}]: {error.Message}")));
                continue;
            }

            var encumbered = book.Encumber(pick.Id, encumbrance);
            if (encumbered.IsFailure)
            {
                errors.AddRange(encumbered.Errors);
                continue;
            }

            ledger.RecordPickEvent(TransactionKind.DraftPickEncumbered, season, controller, pick.Id, reason, counterparty);
        }

        return errors.Count > 0 ? DomainOperationResult.Failure(errors.ToArray()) : DomainOperationResult.Success;
    }

    private static string NoteSuffix(string? note) => string.IsNullOrWhiteSpace(note) ? "." : $" ({note.Trim()}).";

    /// <summary>
    /// Resolves an image path a pack states. The path is untrusted: it must be relative, name a png
    /// or jpg, and stay inside the pack's own folder once resolved — a pack must not be able to point
    /// the client at an arbitrary file on disk. A well-formed path to a file that is not there is not
    /// an error: artwork is optional, and the screen falls back to drawing without it.
    /// </summary>
    private static string? ResolveImage(string? statedPath, string packDirectory, string label, List<DomainError> errors)
    {
        if (statedPath is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(statedPath) || Path.IsPathRooted(statedPath))
        {
            errors.Add(new DomainError(InvalidImageCode, $"{label}: '{statedPath}' must be a path relative to the pack file."));
            return null;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packDirectory)) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, statedPath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(root, comparison))
        {
            errors.Add(new DomainError(InvalidImageCode, $"{label}: '{statedPath}' points outside the pack's folder."));
            return null;
        }

        if (!ImageExtensions.Contains(Path.GetExtension(fullPath).ToLowerInvariant()))
        {
            errors.Add(new DomainError(InvalidImageCode, $"{label}: '{statedPath}' is not a png or jpg image."));
            return null;
        }

        return File.Exists(fullPath) ? fullPath : null;
    }

    private DomainOperationResult<string> ReadFile(string path, string description)
    {
        try
        {
            var text = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(text)
                ? DomainOperationResult<string>.Failure(new DomainError(UnreadableFileCode, $"The {description} file at '{path}' is empty."))
                : DomainOperationResult<string>.Success(text);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return DomainOperationResult<string>.Failure(new DomainError(MissingFileCode, $"No {description} file was found at '{path}'."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DomainOperationResult<string>.Failure(new DomainError(UnreadableFileCode, $"The {description} file at '{path}' could not be read: {exception.Message}"));
        }
    }

    private static DomainError Invalid(string field, string message) => new(InvalidFieldCode, $"{field}: {message}");

    private static DomainOperationResult<LeagueSnapshot> Fail(string code, string message) =>
        DomainOperationResult<LeagueSnapshot>.Failure(new DomainError(code, message));

    private sealed record ParsedPlayer(
        string Name,
        Position Position,
        PlayerRating Rating,
        DateOnly BirthDate,
        int SeasonsOfService,
        string? TeamKey,
        string? Injury,
        LeaguePackContractEnvelope? Contract,
        string? PortraitPath,
        IReadOnlyList<CareerSeasonLine> Career);
}
