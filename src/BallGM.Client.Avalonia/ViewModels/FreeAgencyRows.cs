using BallGM.Application.Leagues;
using BallGM.Application.Negotiations;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// One unsigned player as the offer screen lists them, with what this league permits anyone to pay
/// them. Deliberately not what they are asking for: that is the preference model's answer, and a
/// figure invented here would be a figure a GM learned to trust.
/// </summary>
public sealed record FreeAgentRow(
    string PlayerId,
    string FullName,
    string Position,
    int Overall,
    string Detail,
    string PermittedRange,
    bool IsInjured,
    string? InjuryDescription)
{
    public static FreeAgentRow From(FreeAgentLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var service = line.SeasonsOfService == 1 ? "1 season" : $"{line.SeasonsOfService} seasons";

        // Both figures are nullable because both lines are optional in a ruleset, and the sentence
        // has to say which of the two is missing rather than printing a zero for either.
        var range = (line.MinimumSalary, line.MaximumSalary) switch
        {
            (null, null) => "This league sets no minimum or maximum salary.",
            (not null, null) => $"At least {MoneyDisplay.ToMillions(line.MinimumSalary.Value)}; no maximum in this league.",
            (null, not null) => $"Up to {MoneyDisplay.ToMillions(line.MaximumSalary.Value)}; no minimum in this league.",
            var (minimum, maximum) => $"{MoneyDisplay.ToMillions(minimum!.Value)} to {MoneyDisplay.ToMillions(maximum!.Value)} per season",
        };

        return new FreeAgentRow(
            line.PlayerId,
            line.FullName,
            line.Position,
            line.Overall,
            $"Age {line.Age} · {service} of service",
            range,
            line.IsInjured,
            line.InjuryDescription);
    }
}

/// <summary>
/// One route's verdict, formatted. The three states stay visibly different: a route that permits, a
/// route that refuses with a figure behind the refusal, and a route this league does not have at all.
/// Collapsing the third into the second would teach a GM the rules of a league they are not in.
/// </summary>
public sealed record SigningRouteRow(string RouteName, string Status, string Explanation, string RuleCode, bool Permits, bool Applicable)
{
    public static SigningRouteRow From(SigningRouteLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var status = !line.Applicable
            ? "Not in this league"
            : line.Permits
                ? line.MaximumFirstSeasonCompensation is { } permitted
                    ? $"Permits — up to {MoneyDisplay.ToMillions(permitted)}"
                    : "Permits — no limit"
                : line.MaximumFirstSeasonCompensation is { } refused
                    ? $"Refuses — {MoneyDisplay.ToMillions(refused)} available"
                    : "Refuses";

        return new SigningRouteRow(line.RouteName, status, line.Explanation, line.RuleCode, line.Permits, line.Applicable);
    }
}

/// <summary>
/// One thing the rules had to say about the offer, with its code kept alongside the sentence — the
/// same bargain the trade screen strikes, for the same reason: "which rule" is the first question a
/// GM asks about a refusal.
/// </summary>
public sealed record SigningFindingRow(string RuleCode, string Explanation)
{
    public static SigningFindingRow From(SigningFindingLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new SigningFindingRow(line.RuleCode, line.Explanation);
    }
}
