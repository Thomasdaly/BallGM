namespace BallGM.Domain.Leagues;

public sealed record Season
{
    public Season(int year)
    {
        if (year <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Season year must be positive.");
        }

        Year = year;
    }

    public int Year { get; }
}
