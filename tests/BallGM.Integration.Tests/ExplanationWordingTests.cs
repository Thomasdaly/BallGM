using System.Text.RegularExpressions;

namespace BallGM.Integration.Tests;

/// <summary>
/// Rule findings and ledger entries are read by a GM, so their sentences take real plurals. A source
/// scan rather than one test per message: there are dozens of messages, and the regression is a
/// single habit ("day(s)") that comes back wherever a new one is written.
/// </summary>
public sealed partial class ExplanationWordingTests
{
    [Fact]
    public void ProductionMessages_NeverUseParentheticalPlurals()
    {
        var root = FindRepositoryRoot();
        var offenders = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => ParentheticalPluralInString().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(root, entry.path)}:{entry.index + 1}")
            .ToList();

        Assert.True(offenders.Count == 0, "Use ExplanationText.Count instead of \"(s)\":\n" + string.Join('\n', offenders));
    }

    // A word followed by "(s)" inside a string literal: "day(s)", "offer(s)".
    [GeneratedRegex("\"[^\"]*[a-z]\\(s\\)[^\"]*\"")]
    private static partial Regex ParentheticalPluralInString();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BallGM.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the BallGM repository root.");
    }
}
