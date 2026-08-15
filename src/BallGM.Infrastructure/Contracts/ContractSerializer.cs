using System.Text.Json;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Infrastructure.Contracts;

/// <summary>
/// Reads and writes contracts as versioned JSON. Like <c>LeagueRulesetSerializer</c>, this never
/// throws on bad content: a save or data pack is untrusted input, so a malformed contract produces
/// a structured failure the loader can explain instead of a crash mid-load.
/// </summary>
public sealed class ContractSerializer
{
    private const string MalformedFileCode = "contract.malformed_file";
    private const string UnsupportedSchemaVersionCode = "contract.unsupported_schema_version";
    private const string InvalidFieldCode = "contract.invalid_field";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string Serialize(Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var envelope = new ContractEnvelope(
            ContractEnvelope.CurrentSchemaVersion,
            contract.Id.Value,
            contract.TeamId.Value,
            contract.PlayerId.Value,
            contract.TerminatedFromSeason?.Year,
            contract.Terms
                .Select(term => new ContractSeasonTermEnvelope(
                    term.Season.Year,
                    term.Compensation.SmallestUnits,
                    term.GuaranteedAmount.SmallestUnits,
                    term.Option.ToString(),
                    term.OptionStatus.ToString()))
                .ToList());

        return JsonSerializer.Serialize(envelope, Options);
    }

    public DomainOperationResult<Contract> Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        ContractEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ContractEnvelope>(json, Options);
        }
        catch (JsonException exception)
        {
            return Fail(MalformedFileCode, $"The contract is not valid JSON: {exception.Message}");
        }

        if (envelope is null)
        {
            return Fail(MalformedFileCode, "The contract payload did not contain a contract.");
        }

        if (envelope.SchemaVersion != ContractEnvelope.CurrentSchemaVersion)
        {
            return Fail(
                UnsupportedSchemaVersionCode,
                $"Contract schema version {envelope.SchemaVersion} cannot be read by this build, which reads version {ContractEnvelope.CurrentSchemaVersion}.");
        }

        if (envelope.Seasons is null)
        {
            return Fail(MalformedFileCode, "The contract declares no seasons.");
        }

        try
        {
            var terms = new List<ContractSeasonTerm>(envelope.Seasons.Count);
            foreach (var season in envelope.Seasons)
            {
                if (!Enum.TryParse<ContractOptionKind>(season.Option, out var option))
                {
                    return Fail(
                        InvalidFieldCode,
                        $"Season {season.SeasonYear} declares an unknown contract option '{season.Option}'.");
                }

                if (!Enum.TryParse<ContractOptionStatus>(season.OptionStatus, out var status))
                {
                    return Fail(
                        InvalidFieldCode,
                        $"Season {season.SeasonYear} declares an unknown contract option status '{season.OptionStatus}'.");
                }

                terms.Add(new ContractSeasonTerm(
                    new Season(season.SeasonYear),
                    new Money(season.Compensation),
                    new Money(season.GuaranteedAmount),
                    option,
                    status));
            }

            var contractResult = Contract.Create(
                new ContractId(envelope.ContractId),
                new TeamId(envelope.TeamId),
                new PlayerId(envelope.PlayerId),
                terms);

            if (contractResult.IsFailure || envelope.TerminatedFromSeasonYear is null)
            {
                return contractResult;
            }

            // Termination is state the contract reaches through its own method, not a field the
            // file sets directly, so a save that claims an impossible release fails the same way a
            // live release would.
            var terminateResult = contractResult.Value.Terminate(new Season(envelope.TerminatedFromSeasonYear.Value));
            return terminateResult.IsFailure
                ? DomainOperationResult<Contract>.Failure(terminateResult.Errors.ToArray())
                : contractResult;
        }
        catch (ArgumentException exception)
        {
            return Fail(InvalidFieldCode, exception.Message);
        }
    }

    private static DomainOperationResult<Contract> Fail(string code, string message) =>
        DomainOperationResult<Contract>.Failure(new DomainError(code, message));
}
