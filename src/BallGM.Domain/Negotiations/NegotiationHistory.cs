using BallGM.Domain.Teams;

namespace BallGM.Domain.Negotiations;

/// <summary>
/// Where a negotiation has got to. Four states, and the two that look alike are deliberately not one:
/// <see cref="Resolved"/> means the player has chosen and nothing has been signed yet, while
/// <see cref="Signed"/> means a contract exists. Collapsing them would leave no state for a market
/// that resolved and then had the signing refused — which is exactly the state a rollback restores.
/// </summary>
public enum NegotiationState
{
    /// <summary>Receiving offers. The only state in which anything may be offered, countered, or withdrawn.</summary>
    Open = 0,

    /// <summary>The market resolved and the player accepted an offer. No contract exists yet.</summary>
    Resolved = 1,

    /// <summary>The accepted offer became a contract.</summary>
    Signed = 2,

    /// <summary>Over with nobody signed: no offer cleared the player's reservation, or it was abandoned.</summary>
    Closed = 3,
}

/// <summary>Who authored an entry. A counteroffer is an offer the <em>player</em> wrote.</summary>
public enum NegotiationParty
{
    Team = 0,
    Player = 1,
}

/// <summary>
/// What happened, in the order it happened. The history is the negotiation: a mutable "current offer"
/// field would throw away the sequence of what was asked and refused, which is the only thing an AI
/// front office — or a GM reading back why they lost a player — has to reason from.
/// </summary>
public enum NegotiationEventKind
{
    Opened = 0,

    /// <summary>A team put terms on the table. Supersedes that team's previous offer, never amends it.</summary>
    OfferPlaced = 1,

    /// <summary>
    /// The player asked for different terms from one team. A new <see cref="Offer"/> in the history
    /// rather than a state transition: the negotiation is still open, and a team that likes the
    /// counter answers it with its own next offer.
    /// </summary>
    Counteroffer = 2,

    /// <summary>A team took its offer back.</summary>
    OfferWithdrawn = 3,

    /// <summary>An offer stood longer than this league lets one stand.</summary>
    OfferExpired = 4,

    /// <summary>The market resolved. Carries the accepted offer, or nothing when none was acceptable.</summary>
    MarketResolved = 5,

    /// <summary>The accepted offer was executed into a contract.</summary>
    ContractSigned = 6,

    /// <summary>The negotiation ended without a signing.</summary>
    Closed = 7,
}

/// <summary>
/// One line of the history. <paramref name="Offer"/> is present only for the two kinds that carry
/// terms, and <paramref name="InResponseTo"/> only for a counteroffer — a counter that does not name
/// what it is countering is a demand, not a reply.
/// </summary>
public sealed record NegotiationEntry(
    int Sequence,
    NegotiationEventKind Kind,
    SeasonDay Day,
    NegotiationParty Author,
    TeamId? TeamId,
    Offer? Offer,
    OfferId? InResponseTo,
    string Narrative);
