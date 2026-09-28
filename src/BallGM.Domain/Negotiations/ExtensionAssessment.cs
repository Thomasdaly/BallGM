using BallGM.Domain.Common;

namespace BallGM.Domain.Negotiations;

/// <summary>
/// What an extension offer to a player already under contract comes to: whether it is a legal thing
/// to offer (<see cref="IsLegal"/>, with <see cref="Violations"/> when not), and — only when it is —
/// whether the player takes it (<see cref="Accepted"/>) and why, measured against
/// <see cref="AskingPrice"/>. A refusal is an answer, not a failure: the offer was valid and the
/// player said no.
/// </summary>
public sealed record ExtensionAssessment(
    bool IsLegal,
    bool Accepted,
    Money? AskingPrice,
    string DecisionCode,
    string DecisionExplanation,
    IReadOnlyList<RuleFinding> Violations,
    IReadOnlyList<RuleFinding> Notes);
