namespace BallGM.Integration.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void CoreProjectsDoNotReferenceAvalonia()
    {
        var root = FindRepositoryRoot();
        var coreProjects = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("BallGM.Client.Avalonia.csproj", StringComparison.Ordinal));

        foreach (var projectPath in coreProjects)
        {
            var projectXml = File.ReadAllText(projectPath);
            Assert.DoesNotContain("Avalonia", projectXml);
        }
    }

    [Fact]
    public void DomainProjectHasNoProjectReferences()
    {
        var root = FindRepositoryRoot();
        var domainProject = Path.Combine(root, "src", "BallGM.Domain", "BallGM.Domain.csproj");
        var projectXml = File.ReadAllText(domainProject);

        Assert.DoesNotContain("<ProjectReference", projectXml);
    }

    [Fact]
    public void AvaloniaClientDoesNotReferenceDomainDirectly()
    {
        var root = FindRepositoryRoot();
        var clientProject = Path.Combine(root, "src", "BallGM.Client.Avalonia", "BallGM.Client.Avalonia.csproj");
        var projectXml = File.ReadAllText(clientProject);

        Assert.Contains("BallGM.Application.csproj", projectXml);
        Assert.DoesNotContain("BallGM.Domain.csproj", projectXml);
    }

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
