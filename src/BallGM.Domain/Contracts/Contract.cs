using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Contracts;

/// <summary>
/// Aggregate root for one agreement between a team and a player: who the parties are, which seasons
/// it covers, what each season pays, how much of that survives a release, and who holds any option.
/// <para>
/// A contract states what is <em>owed</em>. What <em>counts</em> against a threshold in a given
/// season is a <see cref="CapCharge"/>, produced by <see cref="ChargeFor"/> — the two are separate
/// on purpose, because dead money is a charge with no live contract behind it.
/// </para>
/// </summary>
public sealed class Contract
{
    private const string SeasonNotInContractCode = "contract.season_not_in_contract";
    private const string SeasonHasNoOptionCode = "contract.season_has_no_option";
    private const string OptionAlreadyDecidedCode = "contract.option_already_decided";
    private const string AlreadyTerminatedCode = "contract.already_terminated";
    private const string AlreadyOnTeamCode = "contract.already_on_team";
    private const string InvalidRetainedPercentCode = "contract.invalid_buyout_share";

    private readonly List<ContractSeasonTerm> _terms;

    private Contract(ContractId id, TeamId teamId, PlayerId playerId, List<ContractSeasonTerm> terms)
    {
        Id = id;
        TeamId = teamId;
        PlayerId = playerId;
        _terms = terms;
    }

    /// <summary>
    /// Creates a contract. Structural problems (a null identifier) throw, because they are caller
    /// bugs; term problems a data pack can legitimately contain — no seasons, a gap in the season
    /// range, a guarantee larger than the salary it guarantees — come back as structured failures.
    /// </summary>
    public static DomainOperationResult<Contract> Create(
        ContractId id,
        TeamId teamId,
        PlayerId playerId,
        IEnumerable<ContractSeasonTerm> terms)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(terms);

        var normalized = ContractTerms.Normalize(terms, nameof(terms));
        if (normalized.IsFailure)
        {
            return DomainOperationResult<Contract>.Failure(normalized.Errors.ToArray());
        }

        var termList = normalized.Value.ToList();

        return DomainOperationResult<Contract>.Success(new Contract(id, teamId, playerId, termList));
    }

    public ContractId Id { get; }

    public TeamId TeamId { get; private set; }

    public PlayerId PlayerId { get; }

    public IReadOnlyList<ContractSeasonTerm> Terms => _terms;

    public Season FirstSeason => _terms[0].Season;

    /// <summary>
    /// The last season the contract actually runs: the last stated season, or the one before a declined
    /// option — declining an option ends the deal there, so nothing from that season on is owed or played.
    /// </summary>
    public Season LastSeason
    {
        get
        {
            var declined = _terms.FindIndex(term => term.IsDeclinedOption);
            return declined switch
            {
                < 0 => _terms[^1].Season,
                0 => _terms[0].Season,
                _ => _terms[declined - 1].Season,
            };
        }
    }

    /// <summary>The last season the contract states, declined options included — what a save writes and a cap sheet lists.</summary>
    public Season StatedLastSeason => _terms[^1].Season;

    /// <summary>The season the player was released in, if they were. Set by <see cref="Terminate"/>.</summary>
    public Season? TerminatedFromSeason { get; private set; }

    public bool IsTerminated => TerminatedFromSeason is not null;

    /// <summary>
    /// The share of each remaining season's guaranteed money still owed after a release, in whole per
    /// cent: 100 for an ordinary release (every guaranteed dollar becomes dead money), less for a buyout,
    /// where the player gave some back to be let go. Meaningless on a live contract.
    /// </summary>
    public int RetainedPercent { get; private set; } = 100;

    /// <summary>Whether this contract ended in a negotiated buyout rather than an ordinary release.</summary>
    public bool IsBoughtOut => IsTerminated && RetainedPercent < 100;

    /// <summary>
    /// Ends the contract from <paramref name="effectiveSeason"/> in a buyout: the player agreed to give
    /// back part of what he was owed, so only <paramref name="retainedPercent"/> per cent of each remaining
    /// season's guaranteed money stays on the team's books as dead money.
    /// </summary>
    public DomainOperationResult BuyOut(Season effectiveSeason, int retainedPercent)
    {
        ArgumentNullException.ThrowIfNull(effectiveSeason);
        if (retainedPercent is < 0 or > 100)
        {
            return DomainOperationResult.Failure(new DomainError(
                InvalidRetainedPercentCode,
                $"A buyout keeps 0 to 100 per cent of the guaranteed money, not {retainedPercent}."));
        }

        var terminated = Terminate(effectiveSeason);
        if (terminated.IsSuccess)
        {
            RetainedPercent = retainedPercent;
        }

        return terminated;
    }

    /// <summary>
    /// What the contract owes for <paramref name="season"/>, or null where it does not run then — before
    /// it starts, after it ends, or from a declined option on (the deal ended there).
    /// </summary>
    public ContractSeasonTerm? TermFor(Season season)
    {
        ArgumentNullException.ThrowIfNull(season);
        if (_terms.Any(term => term.IsDeclinedOption && term.Season.Year <= season.Year))
        {
            return null;
        }

        return _terms.FirstOrDefault(term => term.Season == season);
    }

    /// <summary>
    /// Takes up an option season. Only the holder's own decision is modelled — which party may call
    /// it, and by when, is negotiation logic deferred to Milestone 6.
    /// </summary>
    public DomainOperationResult ExerciseOption(Season season) =>
        DecideOption(season, ContractOptionStatus.Exercised);

    /// <summary>
    /// Turns down an option season. The contract ends there: that season and every later one stop
    /// producing cap charges, and no dead money is created, because an undecided option season was
    /// never a commitment.
    /// </summary>
    public DomainOperationResult DeclineOption(Season season) =>
        DecideOption(season, ContractOptionStatus.Declined);

    /// <summary>
    /// Releases the player from <paramref name="effectiveSeason"/> onward. The contract stops being
    /// live; what survives is the guaranteed money, which keeps counting as dead money.
    /// </summary>
    public DomainOperationResult Terminate(Season effectiveSeason)
    {
        ArgumentNullException.ThrowIfNull(effectiveSeason);

        if (IsTerminated)
        {
            return DomainOperationResult.Failure(new DomainError(
                AlreadyTerminatedCode,
                $"Contract '{Id.Value}' was already terminated from season {TerminatedFromSeason!.Year}."));
        }

        if (TermFor(effectiveSeason) is null)
        {
            return DomainOperationResult.Failure(new DomainError(
                SeasonNotInContractCode,
                $"Contract '{Id.Value}' does not cover season {effectiveSeason.Year}."));
        }

        TerminatedFromSeason = effectiveSeason;
        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Moves the contract to another team, as a trade does. The salary follows the player: a traded
    /// player whose contract stayed behind would leave both teams' cap sheets wrong, which is why
    /// this is an operation on the aggregate rather than a field the executor sets.
    /// <para>
    /// A terminated contract cannot move. Its remaining money is dead money on the team that
    /// released the player, and dead money is not a tradeable asset at this milestone.
    /// </para>
    /// </summary>
    public DomainOperationResult TransferTo(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);

        if (IsTerminated)
        {
            return DomainOperationResult.Failure(new DomainError(
                AlreadyTerminatedCode,
                $"Contract '{Id.Value}' was terminated from season {TerminatedFromSeason!.Year} and cannot be traded."));
        }

        if (teamId == TeamId)
        {
            return DomainOperationResult.Failure(new DomainError(
                AlreadyOnTeamCode,
                $"Contract '{Id.Value}' is already held by team '{teamId.Value}'."));
        }

        TeamId = teamId;
        return DomainOperationResult.Success;
    }

    /// <summary>
    /// What this contract puts on the team's books for <paramref name="season"/>, or <c>null</c>
    /// when it puts nothing there: a season outside the contract, a season after a declined option,
    /// an undecided option season, or a released player whose remaining money was not guaranteed.
    /// </summary>
    public CapCharge? ChargeFor(Season season)
    {
        ArgumentNullException.ThrowIfNull(season);

        var term = TermFor(season);
        if (term is null)
        {
            return null;
        }

        // A declined option ends the contract at that season, so nothing from there on counts.
        if (_terms.Any(other => other.IsDeclinedOption && other.Season.Year <= season.Year))
        {
            return null;
        }

        if (IsTerminated && season.Year >= TerminatedFromSeason!.Year)
        {
            var owed = term.GuaranteedAmount.SmallestUnits * RetainedPercent / 100;
            return owed == 0
                ? null
                : CapCharge.DeadMoney(TeamId, season, PlayerId, Id, new Money(owed));
        }

        if (term.IsPendingOption)
        {
            return null;
        }

        return CapCharge.ActiveContract(TeamId, season, PlayerId, Id, term.Compensation);
    }

    private DomainOperationResult DecideOption(Season season, ContractOptionStatus decision)
    {
        ArgumentNullException.ThrowIfNull(season);

        if (IsTerminated)
        {
            return DomainOperationResult.Failure(new DomainError(
                AlreadyTerminatedCode,
                $"Contract '{Id.Value}' was terminated from season {TerminatedFromSeason!.Year} and its options can no longer be decided."));
        }

        var index = _terms.FindIndex(term => term.Season == season);
        if (index < 0)
        {
            return DomainOperationResult.Failure(new DomainError(
                SeasonNotInContractCode,
                $"Contract '{Id.Value}' does not cover season {season.Year}."));
        }

        var term = _terms[index];
        if (term.Option == ContractOptionKind.None)
        {
            return DomainOperationResult.Failure(new DomainError(
                SeasonHasNoOptionCode,
                $"Season {season.Year} of contract '{Id.Value}' carries no option to decide."));
        }

        if (!term.IsPendingOption)
        {
            return DomainOperationResult.Failure(new DomainError(
                OptionAlreadyDecidedCode,
                $"The season {season.Year} option on contract '{Id.Value}' was already {term.OptionStatus.ToString().ToLowerInvariant()}."));
        }

        _terms[index] = term.WithOptionStatus(decision);
        return DomainOperationResult.Success;
    }
}
