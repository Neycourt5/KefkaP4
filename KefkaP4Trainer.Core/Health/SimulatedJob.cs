namespace KefkaP4Trainer.Core.Health;

/// <summary>
/// Combat jobs, numbered by their ClassJob sheet row id so the plugin layer can
/// convert a live job with a cast rather than a lookup table.
/// </summary>
public enum SimulatedJob
{
    Unknown = 0,
    Paladin = 19,
    Monk = 20,
    Warrior = 21,
    Dragoon = 22,
    Bard = 23,
    WhiteMage = 24,
    BlackMage = 25,
    Summoner = 27,
    Scholar = 28,
    Ninja = 30,
    Machinist = 31,
    DarkKnight = 32,
    Astrologian = 33,
    Samurai = 34,
    RedMage = 35,
    Gunbreaker = 37,
    Dancer = 38,
    Reaper = 39,
    Sage = 40,
    Viper = 41,
    Pictomancer = 42,
}

public enum RoleKind
{
    Unknown,
    Tank,
    Healer,
    Dps,
}

/// <summary>
/// Which half of the healer pair a job covers. Drives the complementary
/// co-healer: a barrier healer is paired with a pure one and vice versa.
/// </summary>
public enum HealerProfile
{
    None,

    /// <summary>White Mage and Astrologian: regen and direct healing.</summary>
    Pure,

    /// <summary>Scholar and Sage: shields and damage prevention.</summary>
    Barrier,
}

public static class SimulatedJobs
{
    public static RoleKind Role(this SimulatedJob job) => job switch
    {
        SimulatedJob.Paladin or SimulatedJob.Warrior
            or SimulatedJob.DarkKnight or SimulatedJob.Gunbreaker => RoleKind.Tank,
        SimulatedJob.WhiteMage or SimulatedJob.Scholar
            or SimulatedJob.Astrologian or SimulatedJob.Sage => RoleKind.Healer,
        SimulatedJob.Unknown => RoleKind.Unknown,
        _ => RoleKind.Dps,
    };

    public static HealerProfile Profile(this SimulatedJob job) => job switch
    {
        SimulatedJob.WhiteMage or SimulatedJob.Astrologian => HealerProfile.Pure,
        SimulatedJob.Scholar or SimulatedJob.Sage => HealerProfile.Barrier,
        _ => HealerProfile.None,
    };

    public static bool IsHealer(this SimulatedJob job) => job.Role() == RoleKind.Healer;

    /// <summary>The profile that complements <paramref name="profile"/>.</summary>
    public static HealerProfile Complement(this HealerProfile profile) => profile switch
    {
        HealerProfile.Pure => HealerProfile.Barrier,
        HealerProfile.Barrier => HealerProfile.Pure,
        _ => HealerProfile.None,
    };

    /// <summary>A representative job for a profile, used to label the virtual co-healer.</summary>
    public static SimulatedJob RepresentativeJob(this HealerProfile profile) => profile switch
    {
        HealerProfile.Pure => SimulatedJob.WhiteMage,
        HealerProfile.Barrier => SimulatedJob.Scholar,
        _ => SimulatedJob.Unknown,
    };

    public static SimulatedJob FromRowId(uint rowId) =>
        Enum.IsDefined(typeof(SimulatedJob), (int)rowId)
            ? (SimulatedJob)rowId
            : SimulatedJob.Unknown;

    /// <summary>The job the eight-slot party defaults to when nothing else is known.</summary>
    public static SimulatedJob DefaultFor(PartyRole slot) => slot switch
    {
        PartyRole.T1 => SimulatedJob.Paladin,
        PartyRole.T2 => SimulatedJob.Warrior,
        PartyRole.H1 => SimulatedJob.WhiteMage,
        PartyRole.H2 => SimulatedJob.Scholar,
        PartyRole.M1 => SimulatedJob.Samurai,
        PartyRole.M2 => SimulatedJob.Dragoon,
        PartyRole.R1 => SimulatedJob.Bard,
        PartyRole.R2 => SimulatedJob.BlackMage,
        _ => SimulatedJob.Unknown,
    };
}
