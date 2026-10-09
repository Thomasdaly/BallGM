using BallGM.Application.Players;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The per game / per 36 / totals choice every stat table offers, as one component: the roster and
/// the profile hold one each and redraw when it changes. Three radio chips bind to the three flags;
/// a radio cannot be unchecked by clicking it, so only choosing a chip changes the basis.
/// </summary>
public sealed class StatBasisChoice : ViewModelBase
{
    private StatBasis _basis = StatBasis.PerGame;

    /// <summary>Raised after the basis changes, for the screen to redraw its tables.</summary>
    public event Action? Changed;

    public StatBasis Basis
    {
        get => _basis;
        set
        {
            if (_basis == value)
            {
                return;
            }

            _basis = value;
            RaisePropertyChanged(nameof(Basis));
            RaisePropertyChanged(nameof(IsPerGame));
            RaisePropertyChanged(nameof(IsPer36));
            RaisePropertyChanged(nameof(IsTotals));
            Changed?.Invoke();
        }
    }

    public bool IsPerGame
    {
        get => _basis == StatBasis.PerGame;
        set => Choose(StatBasis.PerGame, value);
    }

    public bool IsPer36
    {
        get => _basis == StatBasis.Per36;
        set => Choose(StatBasis.Per36, value);
    }

    public bool IsTotals
    {
        get => _basis == StatBasis.Totals;
        set => Choose(StatBasis.Totals, value);
    }

    private void Choose(StatBasis basis, bool chosen)
    {
        if (chosen)
        {
            Basis = basis;
        }
    }
}
