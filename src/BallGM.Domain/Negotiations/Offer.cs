using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Negotiations;

/// <summary>
/// One team's proposed contract for one player: immutable, complete, and never edited. An offer is
/// superseded by another offer, not amended — the sequence of what was offered and refused is the
/// negotiation, and a mutable "current offer" field would throw away the history the AI front office
/// has to read back.
/// <para>
/// The seasons are <see cref="ContractSeasonTerm"/>, the same type the resulting
/// <see cref="Contract"/> carries, so an offer cannot pass a shape check the contract it becomes
/// would fail. What the offer does <em>not</em> assume is that compensation and term are the whole
/// of a deal: clauses a party holds — a movement veto, a buyout provision — attach to this type in a
/// later milestone without touching the season terms, which is why they are a collection on the
/// offer rather than the offer itself.
/// </para>
/// </summary>
public sealed record Offer
{
    private const string NonPositiveCompensationCode = "offer.non_positive_compensation";

    private Offer(
        OfferId id,
        TeamId teamId,
        PlayerId playerId,
        IReadOnlyList<ContractSeasonTerm> terms)
    {
        Id = id;
        TeamId = teamId;
        PlayerId = playerId;
        Terms = terms;
    }

    public OfferId Id { get; }

    public TeamId TeamId { get; }

    public PlayerId PlayerId { get; }

    /// <summary>The proposed seasons, ascending. Never empty.</summary>
    public IReadOnlyList<ContractSeasonTerm> Terms { get; }

    public Season FirstSeason => Terms[0].Season;

    public Season LastSeason => Terms[^1].Season;

    public int SeasonCount => Terms.Count;

    /// <summary>What the first season pays. Every cap and route check is against this figure.</summary>
    public Money FirstSeasonCompensation => Terms[0].Compensation;

    public Money TotalCompensation => Money.Sum(Terms.Select(term => term.Compensation));

    public Money TotalGuaranteed => Money.Sum(Terms.Select(term => term.GuaranteedAmount));

    public bool IsFullyGuaranteed => Terms.All(term => term.IsFullyGuaranteed);

    /// <summary>
    /// Builds an offer, or explains why the seasons are not a contract anyone could sign. Structural
    /// nulls throw; everything an offer screen can produce comes back as a structured failure,
    /// because an offer screen is untrusted input in exactly the way a data pack is.
    /// </summary>
    public static DomainOperationResult<Offer> Create(
        OfferId id,
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
            return DomainOperationResult<Offer>.Failure(normalized.Errors.ToArray());
        }

        var termList = normalized.Value;
        var errors = new List<DomainError>();

        foreach (var term in termList.Where(term => term.Compensation.SmallestUnits <= 0))
        {
            errors.Add(new DomainError(
                NonPositiveCompensationCode,
                $"Season {term.Season.Year} of this offer pays {term.Compensation.SmallestUnits}. An offer has to pay something in every season it covers."));
        }

        return errors.Count > 0
            ? DomainOperationResult<Offer>.Failure(errors.ToArray())
            : DomainOperationResult<Offer>.Success(new Offer(id, teamId, playerId, termList));
    }

    /// <summary>
    /// The season terms as a contract would carry them. Acceptance uses this rather than rebuilding
    /// the run from parts: what the player agreed to and what the contract says have to be the same
    /// list, not two lists that happen to match today.
    /// </summary>
    public IReadOnlyList<ContractSeasonTerm> ToContractTerms() => Terms;
}
