namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>What each attribute covers, in a line: the tooltip a sheet row carries. Keyed by the attribute's stable key.</summary>
public static class AttributeDescriptions
{
    private static readonly Dictionary<string, string> Lines = new(StringComparer.Ordinal)
    {
        ["Finishing"] = "Making layups and shots at the rim, through and around defenders.",
        ["Dunking"] = "Finishing above the rim when the lane opens.",
        ["PostScoring"] = "Scoring with his back to the basket: footwork, hooks, turnarounds.",
        ["CloseRange"] = "Floaters and short shots in the paint, short of the rim.",
        ["MidRange"] = "Pull-ups and spot-ups inside the arc, away from the paint.",
        ["ThreePoint"] = "Making threes, from the corners and above the break.",
        ["FreeThrow"] = "Making free throws.",
        ["ShotCreation"] = "Getting his own shot off the dribble against a set defence.",
        ["BallHandling"] = "Keeping the ball under pressure and getting where he wants with it.",
        ["Passing"] = "Delivering the ball accurately, on time, to the right hand.",
        ["Vision"] = "Seeing the open man before the defence does.",
        ["OffBallMovement"] = "Cutting, relocating and finding space without the ball.",
        ["ScreenSetting"] = "Setting screens that free a team-mate: angle, timing, contact.",
        ["PerimeterDefence"] = "Guarding the ball and shooters on the outside.",
        ["InteriorDefence"] = "Guarding the post and defending at the rim.",
        ["HelpDefence"] = "Rotating, tagging the roller, protecting a beaten team-mate.",
        ["Stealing"] = "Taking the ball: jumping lanes, poking it loose.",
        ["Blocking"] = "Blocking and altering shots.",
        ["DefensiveDiscipline"] = "Defending without fouling or biting on fakes.",
        ["OffensiveRebounding"] = "Getting back his team's misses.",
        ["DefensiveRebounding"] = "Finishing defensive possessions by taking the miss.",
        ["BoxingOut"] = "Sealing his man away from the ball on a shot.",
        ["BasketballIq"] = "Making the right decision with and without the ball.",
        ["Anticipation"] = "Reading a play a beat before it happens.",
        ["Composure"] = "Keeping his decisions sound under pressure and late in games.",
        ["Concentration"] = "Staying switched on possession after possession.",
        ["WorkRate"] = "How hard he plays on every possession.",
        ["Hustle"] = "Loose balls, charges, diving on the floor.",
        ["Aggression"] = "Attacking the rim and seeking contact.",
        ["Leadership"] = "Lifting and organising those around him.",
        ["Teamwork"] = "Playing within the team's system rather than for himself.",
        ["Speed"] = "Speed up the floor, with and without the ball.",
        ["FirstStep"] = "The burst that beats a defender off the dribble.",
        ["LateralQuickness"] = "Moving his feet to stay in front.",
        ["Strength"] = "Holding position and finishing through contact.",
        ["Vertical"] = "How high and how quickly he gets off the floor.",
        ["Stamina"] = "How long he plays before tiring, in a game and over a season.",
        ["NaturalFitness"] = "How well he keeps his condition and recovers.",
    };

    public static string For(string key) => Lines.GetValueOrDefault(key, string.Empty);

    /// <summary>The name a sheet prints for an attribute key: its words, with the one acronym kept whole.</summary>
    public static string NameOf(string key) => key == "BasketballIq" ? "Basketball IQ" : DisplayText.Words(key);
}
