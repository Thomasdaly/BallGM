using BallGM.Application.Overview;

namespace BallGM.Client.Avalonia.ViewModels;

public sealed class MainWindowViewModel(BallGmOverview overview)
{
    public string ProductName { get; } = overview.ProductName;

    public string ArchitectureStage { get; } = overview.ArchitectureStage;

    public string ClientBoundary { get; } = overview.ClientBoundary;
}
