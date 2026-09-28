using System.Globalization;
using BallGM.Application.Players;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Teams;

namespace BallGM.Application.Leagues;

/// <summary>
/// The contracts half of <see cref="LeagueSession"/>: the payroll outlook across coming seasons, the
/// free-agent classes they produce, and re-signing a player before he gets there — with an alert for
/// every player who has refused.
/// </summary>
public sealed partial class LeagueSession
{
    /// <summary>How many seasons the outlook and the free-agent classes look ahead, the current one included.</summary>
    public const int OutlookSeasons = 5;

    private const string OutlookUnknownTeamCode = "cap_outlook.unknown_team";
    private const string ExtensionUnknownPlayerCode = "extension.unknown_player";

    /// <summary>
    /// Extension refusals this season, by player. Session state, like an in-flight negotiation: a
    /// refusal is cleared when the player re-signs, and every refusal is cleared when the season
    /// ends (he is then a free agent or still under contract, and the question is new).
    /// </summary>
    private readonly Dictionary<string, ExtensionRefusalLine> _extensionRefusals = new(StringComparer.Ordinal);

    public DomainOperationResult<CapOutlookSummary> CapOutlook(string teamId, int capGrowthPercent = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        if (_snapshot is null)
        {
            return NotLoaded<CapOutlookSummary>();
        }

        var team = _snapshot.Teams.FirstOrDefault(candidate => candidate.Id.Value == teamId);
        if (team is null)
        {
            return DomainOperationResult<CapOutlookSummary>.Failure(new DomainError(OutlookUnknownTeamCode, $"No team '{teamId}' is in this league."));
        }

        var start = _snapshot.CurrentSeason.Year;
        var years = Enumerable.Range(start, OutlookSeasons).ToList();
        var contracts = _snapshot.Contracts.Where(contract => contract.TeamId == team.Id && !contract.IsTerminated).ToList();
        var playersById = _snapshot.Players.ToDictionary(player => player.Id);

        var rows = contracts
            .GroupBy(contract => contract.PlayerId)
            .Where(group => playersById.ContainsKey(group.Key))
            .Select(group =>
            {
                var player = playersById[group.Key];
                var ordered = group.OrderBy(contract => contract.FirstSeason.Year).ToList();
                var cells = years.Select(year =>
                {
                    var owner = ordered.FirstOrDefault(contract => contract.TermFor(new Season(year)) is not null);
                    var term = owner?.TermFor(new Season(year));
                    return term is null
                        ? new CapOutlookCell(null, null, false)
                        : new CapOutlookCell(term.Compensation.SmallestUnits, OptionName(term), owner != ordered[0] && ordered[0].FirstSeason.Year <= start);
                }).ToList();
                return new CapOutlookPlayerRow(player.Id.Value, player.FullName, GetLeagueOverviewQuery.DescribePosition(player.Position), player.Rating.Overall, cells);
            })
            .OrderByDescending(row => row.Cells[0].Salary ?? 0)
            .ThenByDescending(row => row.Cells.Sum(cell => cell.Salary ?? 0))
            .ToList();

        var totals = new List<CapOutlookSeason>();
        foreach (var (year, offset) in years.Select((year, offset) => (year, offset)))
        {
            var season = new Season(year);
            var factor = Math.Pow(1 + (capGrowthPercent / 100.0), offset);
            Money? Grow(Money? line) => line is null ? null : new Money((long)Math.Round(line.SmallestUnits * factor / 1000) * 1000);
            var configuration = _snapshot.Configuration with
            {
                PayrollFloor = Grow(_snapshot.Configuration.PayrollFloor),
                SoftCap = Grow(_snapshot.Configuration.SoftCap),
                LuxuryTax = Grow(_snapshot.Configuration.LuxuryTax),
                FirstApron = Grow(_snapshot.Configuration.FirstApron),
                SecondApron = Grow(_snapshot.Configuration.SecondApron),
                HardCap = Grow(_snapshot.Configuration.HardCap),
            };

            var charges = CapChargeProjection.ForTeamSeason(contracts, team.Id, season);
            var underContract = contracts.Where(contract => contract.TermFor(season) is not null).Select(contract => contract.PlayerId).Distinct().Count();
            var sheet = _capLedger.Evaluate(team.Id, season, charges, underContract, configuration);
            if (sheet.IsFailure)
            {
                return DomainOperationResult<CapOutlookSummary>.Failure(sheet.Errors.ToArray());
            }

            var options = contracts.Select(contract => contract.TermFor(season)).OfType<ContractSeasonTerm>()
                .Where(term => term.IsPendingOption).Sum(term => term.Compensation.SmallestUnits);
            var payroll = sheet.Value.TotalPayroll.SmallestUnits;
            var cap = configuration.SoftCap?.SmallestUnits;
            var tax = configuration.LuxuryTax?.SmallestUnits;

            totals.Add(new CapOutlookSeason(
                SeasonLabel(year),
                year,
                underContract,
                sheet.Value.CommittedSalary.SmallestUnits + sheet.Value.DeadMoney.SmallestUnits,
                options,
                sheet.Value.RosterHolds.SmallestUnits,
                payroll,
                cap,
                cap - payroll,
                tax,
                tax - payroll,
                configuration.FirstApron?.SmallestUnits,
                configuration.SecondApron?.SmallestUnits,
                Standing(payroll, configuration)));
        }

        return DomainOperationResult<CapOutlookSummary>.Success(new CapOutlookSummary(
            team.Id.Value, team.Name, capGrowthPercent, years.Select(SeasonLabel).ToList(), rows, totals));
    }

    /// <summary>
    /// Every rostered player whose contract ends within the outlook, grouped by the summer he becomes
    /// a free agent. A player who has signed an extension is read by his latest contract, so he leaves
    /// his old class the moment he re-signs.
    /// </summary>
    public IReadOnlyList<FreeAgentClass> UpcomingFreeAgents(string? teamId = null)
    {
        if (_snapshot is null)
        {
            return [];
        }

        var start = _snapshot.CurrentSeason.Year;
        var asOf = _seasonRun is null ? new DateOnly(start, 7, 1) : _seasonRun.Calendar.DateOn(_seasonRun.CurrentDay);
        var teamNames = TeamNames(_snapshot);
        var playersById = _snapshot.Players.ToDictionary(player => player.Id);
        var lines = new List<UpcomingFreeAgentLine>();

        foreach (var team in _snapshot.Teams.Where(team => teamId is null || team.Id.Value == teamId))
        {
            foreach (var playerId in team.PlayerIds)
            {
                var live = _snapshot.Contracts
                    .Where(contract => contract.PlayerId == playerId && contract.TeamId == team.Id && !contract.IsTerminated)
                    .OrderBy(contract => contract.LastSeason.Year)
                    .ToList();
                if (live.Count == 0 || !playersById.TryGetValue(playerId, out var player))
                {
                    continue;
                }

                var latest = live[^1];
                var freeAgentYear = latest.LastSeason.Year + 1;
                if (freeAgentYear > start + OutlookSeasons)
                {
                    continue;
                }

                var current = live.FirstOrDefault(contract => contract.TermFor(_snapshot.CurrentSeason) is not null);
                lines.Add(new UpcomingFreeAgentLine(
                    player.Id.Value,
                    player.FullName,
                    team.Id.Value,
                    teamNames.GetValueOrDefault(team.Id, team.Name),
                    GetLeagueOverviewQuery.DescribePosition(player.Position),
                    player.Rating.Overall,
                    player.AgeOn(asOf),
                    current?.TermFor(_snapshot.CurrentSeason)?.Compensation.SmallestUnits ?? 0,
                    freeAgentYear,
                    OptionName(latest.Terms[^1]),
                    live.Count == 1 && latest.LastSeason.Year == start,
                    _extensionRefusals.ContainsKey(player.Id.Value),
                    _snapshot.Artwork.PortraitFor(player.Id)));
            }
        }

        return lines
            .GroupBy(line => line.FreeAgentYear)
            .OrderBy(group => group.Key)
            .Select(group => new FreeAgentClass(
                group.Key,
                string.Create(CultureInfo.InvariantCulture, $"Summer {group.Key} (after {SeasonLabel(group.Key - 1)})"),
                group.OrderByDescending(line => line.Overall).ThenBy(line => line.FullName, StringComparer.Ordinal).ToList()))
            .ToList();
    }

    /// <summary>What an extension to this player would need to look like, and what he is asking.</summary>
    public DomainOperationResult<ExtensionTermsSummary> ExtensionTerms(string playerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        if (_snapshot is null)
        {
            return NotLoaded<ExtensionTermsSummary>();
        }

        var player = _snapshot.Players.FirstOrDefault(candidate => candidate.Id.Value == playerId);
        var team = player is null ? null : _snapshot.Teams.FirstOrDefault(candidate => candidate.PlayerIds.Contains(player.Id));
        if (player is null || team is null)
        {
            return DomainOperationResult<ExtensionTermsSummary>.Failure(new DomainError(ExtensionUnknownPlayerCode, $"No rostered player '{playerId}' is in this league."));
        }

        var current = _snapshot.Contracts.FirstOrDefault(contract => contract.PlayerId == player.Id && contract.TeamId == team.Id
            && !contract.IsTerminated && contract.TermFor(_snapshot.CurrentSeason) is not null);
        var startSeason = (current?.LastSeason.Year ?? _snapshot.CurrentSeason.Year) + 1;
        var limits = _signingEngine.LimitsFor(_snapshot, player.SeasonsOfService);

        // Probe with a minimum-salary offer: the assessment reports eligibility and the asking price.
        var probeSalary = limits.Minimum ?? new Money(1);
        var probe = BuildExtensionOffer(team.Id, player.Id, startSeason, 1, probeSalary.SmallestUnits, 0);
        var assessment = probe.IsSuccess ? _signingEngine.AssessExtension(probe.Value, _snapshot) : null;
        var blocking = assessment?.Value?.Violations
            .Where(finding => finding.RuleCode.StartsWith("extension.", StringComparison.Ordinal))
            .ToList() ?? [];

        return DomainOperationResult<ExtensionTermsSummary>.Success(new ExtensionTermsSummary(
            player.Id.Value,
            player.FullName,
            blocking.Count == 0,
            blocking.Count == 0 ? "Eligible: in the final season of his contract." : blocking[0].Explanation,
            startSeason,
            _snapshot.Configuration.Negotiation.MaximumIncumbentContractSeasons ?? _snapshot.Configuration.Negotiation.MaximumContractSeasons ?? 5,
            limits.Minimum?.SmallestUnits,
            limits.Maximum?.SmallestUnits,
            assessment?.Value?.AskingPrice?.SmallestUnits,
            current?.TermFor(_snapshot.CurrentSeason)?.Compensation.SmallestUnits ?? 0));
    }

    /// <summary>
    /// Offers a player an extension. Accepted, it is signed at once; refused, the refusal becomes an
    /// alert; illegal, nothing happens and the violations say why.
    /// </summary>
    public DomainOperationResult<ExtensionOutcomeSummary> OfferExtension(string playerId, int seasons, long firstSeasonSalary, int annualRaisePercent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        if (_snapshot is null)
        {
            return NotLoaded<ExtensionOutcomeSummary>();
        }

        var terms = ExtensionTerms(playerId);
        if (terms.IsFailure)
        {
            return DomainOperationResult<ExtensionOutcomeSummary>.Failure(terms.Errors.ToArray());
        }

        var player = _snapshot.Players.First(candidate => candidate.Id.Value == playerId);
        var team = _snapshot.Teams.First(candidate => candidate.PlayerIds.Contains(player.Id));
        var offer = BuildExtensionOffer(team.Id, player.Id, terms.Value.StartSeason, seasons, firstSeasonSalary, annualRaisePercent);
        if (offer.IsFailure)
        {
            return DomainOperationResult<ExtensionOutcomeSummary>.Success(new ExtensionOutcomeSummary(
                false, false, offer.Errors[0].Code, offer.Errors[0].Message, terms.Value.AskingPrice, offer.Errors.Select(error => error.Message).ToList()));
        }

        var result = _signingEngine.ExecuteExtension(offer.Value, _snapshot);
        if (result.IsFailure)
        {
            return DomainOperationResult<ExtensionOutcomeSummary>.Failure(result.Errors.ToArray());
        }

        var assessment = result.Value.Assessment;
        if (result.Value.Contract is { } contract)
        {
            _snapshot = _snapshot with { Contracts = [.. _snapshot.Contracts, contract] };
            _extensionRefusals.Remove(player.Id.Value);
        }
        else if (assessment.IsLegal && !assessment.Accepted)
        {
            _extensionRefusals[player.Id.Value] = new ExtensionRefusalLine(
                player.Id.Value, player.FullName, team.Id.Value, team.Name, firstSeasonSalary,
                assessment.AskingPrice?.SmallestUnits, _snapshot.CurrentSeason.Year, assessment.DecisionExplanation);
        }

        return DomainOperationResult<ExtensionOutcomeSummary>.Success(new ExtensionOutcomeSummary(
            assessment.IsLegal,
            assessment.Accepted,
            assessment.DecisionCode,
            assessment.DecisionExplanation,
            assessment.AskingPrice?.SmallestUnits,
            assessment.Violations.Select(finding => finding.Explanation).ToList()));
    }

    /// <summary>Every player who has refused an extension this season, optionally for one team.</summary>
    public IReadOnlyList<ExtensionRefusalLine> ExtensionAlerts(string? teamId = null) =>
        _extensionRefusals.Values
            .Where(line => teamId is null || line.TeamId == teamId)
            .OrderBy(line => line.FullName, StringComparer.Ordinal)
            .ToList();

    /// <summary>Called when a season is concluded: refusals are about a season that is over.</summary>
    private void ClearExtensionRefusals() => _extensionRefusals.Clear();

    private static DomainOperationResult<Offer> BuildExtensionOffer(TeamId teamId, Domain.Players.PlayerId playerId, int startSeason, int seasons, long firstSeasonSalary, int annualRaisePercent)
    {
        var terms = Enumerable.Range(0, Math.Max(1, seasons)).Select(index =>
        {
            var salary = new Money(firstSeasonSalary + (firstSeasonSalary * annualRaisePercent * index / 100));
            return new ContractSeasonTerm(new Season(startSeason + index), salary, salary);
        });
        return Offer.Create(new OfferId(SortableId.NewId()), teamId, playerId, terms);
    }

    private static string? OptionName(ContractSeasonTerm term) => term.Option switch
    {
        ContractOptionKind.Player => "Player",
        ContractOptionKind.Team => "Team",
        _ => null,
    };

    private static string Standing(long payroll, LeagueConfiguration configuration)
    {
        bool Over(Money? line) => line is not null && payroll > line.SmallestUnits;
        return Over(configuration.SecondApron) ? "Over the second apron"
            : Over(configuration.FirstApron) ? "Over the first apron"
            : Over(configuration.LuxuryTax) ? "Over the tax"
            : Over(configuration.SoftCap) ? "Over the cap"
            : configuration.SoftCap is null ? "No cap"
            : "Under the cap";
    }
}
