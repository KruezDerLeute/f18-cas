public enum Difficulty { Easy, Normal, Realistic }

/// Choices made on the title screen. Static so they survive
/// ReloadCurrentScene: pressing R restarts with the same difficulty and skips
/// the menu.
public static class Settings
{
    public static Difficulty Difficulty = Difficulty.Easy;
    /// False on the first launch (show the title screen), true once a difficulty is picked.
    public static bool SkipMenu;
    /// Set when a difficulty is picked, so the briefing plays once (R restarts skip it).
    public static bool ShowBriefing;

    public static string DifficultyName => Difficulty switch
    {
        Difficulty.Easy => "ХЯЛБАР",
        Difficulty.Normal => "ДУНД",
        _ => "БОДИТ",
    };

    /// Start flying towards the front instead of parked on the runway.
    public static bool StartInAir => Difficulty != Difficulty.Realistic;
    /// Fly-by-wire won't let the wing stall, and adds throttle when too slow.
    public static bool StallProtection => Difficulty != Difficulty.Realistic;
    /// Automatic ground-collision avoidance: rolls upright and pulls up before hitting the ground.
    public static bool AutoGcas => Difficulty == Difficulty.Easy;
    public static bool UnlimitedFuel => Difficulty == Difficulty.Easy;
    /// Rearm, refuel and repair in the air after each cleared wave (no landing needed).
    public static bool AirRearm => Difficulty == Difficulty.Easy;
    /// Multiplier on all damage the jet takes.
    public static float DamageTaken => Difficulty switch
    {
        Difficulty.Easy => 0.35f,
        Difficulty.Normal => 0.7f,
        _ => 1f,
    };
}
