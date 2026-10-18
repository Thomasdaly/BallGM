using System.Text.RegularExpressions;

namespace BallGM.Client.Avalonia.UiTests.Harness;

/// <summary>
/// Source scanning for the UI lint tests, with the shared allowlist (tests/ui-lint-allowlist.txt):
/// every entry names a rule, a file, the exact offending text, and a reason after '#'.
/// </summary>
internal static class UiLint
{
    public sealed record Violation(string Rule, string Path, int Line, string Text)
    {
        public override string ToString() => $"{Rule} {Path}:{Line}: {Text}";
    }

    public sealed record AllowEntry(string Rule, string Path, string Text, string Reason);

    public static string ClientRoot => System.IO.Path.Combine(RepoPaths.Root, "src", "BallGM.Client.Avalonia");

    public static IEnumerable<string> ClientFiles(string pattern, bool includeThemes = false) =>
        Directory.EnumerateFiles(ClientRoot, pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => includeThemes || !Relative(path).Contains("/Themes/", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal);

    public static string Relative(string path) =>
        System.IO.Path.GetRelativePath(RepoPaths.Root, path).Replace('\\', '/');

    public static IReadOnlyList<AllowEntry> Allowlist()
    {
        var entries = new List<AllowEntry>();
        foreach (var raw in File.ReadAllLines(System.IO.Path.Combine(RepoPaths.Root, "tests", "ui-lint-allowlist.txt")))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var hash = line.IndexOf(" #", StringComparison.Ordinal);
            var reason = hash < 0 ? string.Empty : line[(hash + 2)..].Trim();
            var body = hash < 0 ? line : line[..hash].Trim();
            var parts = body.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            entries.Add(new AllowEntry(parts[0], parts.Length > 1 ? parts[1] : string.Empty, parts.Length > 2 ? parts[2] : string.Empty, reason));
        }

        return entries;
    }

    /// <summary>Violations not covered by an allowlist entry for the same rule, file and text.</summary>
    public static IReadOnlyList<Violation> Unallowed(IEnumerable<Violation> violations)
    {
        var allow = Allowlist();
        return violations
            .Where(violation => !allow.Any(entry => entry.Rule == violation.Rule && entry.Path == violation.Path && violation.Text.Contains(entry.Text, StringComparison.Ordinal)))
            .ToList();
    }

    public static IEnumerable<Violation> Scan(string rule, IEnumerable<string> files, Regex pattern, Func<Match, bool>? accept = null)
    {
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            var inComment = false;
            for (var index = 0; index < lines.Length; index++)
            {
                var text = lines[index];
                text = StripComments(text, ref inComment);
                foreach (Match match in pattern.Matches(text))
                {
                    if (accept is null || accept(match))
                    {
                        yield return new Violation(rule, Relative(file), index + 1, match.Value);
                    }
                }
            }
        }
    }

    /// <summary>Removes XML (&lt;!-- --&gt;) comment text from a line, tracking comments that span lines.</summary>
    private static string StripComments(string line, ref bool inComment)
    {
        var result = new System.Text.StringBuilder();
        var position = 0;
        while (position < line.Length)
        {
            if (inComment)
            {
                var end = line.IndexOf("-->", position, StringComparison.Ordinal);
                if (end < 0)
                {
                    return result.ToString();
                }

                inComment = false;
                position = end + 3;
                continue;
            }

            var start = line.IndexOf("<!--", position, StringComparison.Ordinal);
            if (start < 0)
            {
                result.Append(line, position, line.Length - position);
                break;
            }

            result.Append(line, position, start - position);
            inComment = true;
            position = start + 4;
        }

        return result.ToString();
    }
}
