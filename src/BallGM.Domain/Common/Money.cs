namespace BallGM.Domain.Common;

/// <summary>
/// A non-negative amount in the smallest whole unit of the game's fictional currency.
/// Never use <c>decimal</c>/<c>double</c> for cap or contract math — this type exists so every
/// financial value in the domain shares one representation and one comparison rule.
/// </summary>
public sealed record Money : IComparable<Money>
{
    public Money(long smallestUnits)
    {
        if (smallestUnits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(smallestUnits), smallestUnits, "Money cannot be negative.");
        }

        SmallestUnits = smallestUnits;
    }

    public long SmallestUnits { get; }

    public static Money Zero { get; } = new(0);

    public int CompareTo(Money? other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return SmallestUnits.CompareTo(other.SmallestUnits);
    }

    /// <summary>
    /// Adds two amounts in integer smallest units. Deliberately <c>checked</c>: a payroll that
    /// silently wrapped past <see cref="long.MaxValue"/> would produce a cap sheet that is wrong
    /// rather than one that fails.
    /// </summary>
    public static Money operator +(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new Money(checked(left.SmallestUnits + right.SmallestUnits));
    }

    public static Money Add(Money left, Money right) => left + right;

    public static Money Sum(IEnumerable<Money> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        return amounts.Aggregate(Zero, (total, amount) => total + amount);
    }

    /// <summary>
    /// This amount minus <paramref name="other"/>, as a signed count of smallest units. Returns a
    /// <see cref="long"/> rather than a <see cref="Money"/> because the answer is routinely
    /// negative — a payroll over a threshold — and <see cref="Money"/> is non-negative by design.
    /// </summary>
    public long SignedDifferenceFrom(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return checked(SmallestUnits - other.SmallestUnits);
    }

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;
}
