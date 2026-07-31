using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer;

/// <summary>
/// Works out which seat in the mitigation plan the local player occupies.
/// </summary>
/// <remarks>
/// The four healer seats are one job each, and the plan puts physical ranged in
/// D3 and casters in D4, so those all resolve from the job alone. Tanks and
/// melee share a pair of seats that the job cannot distinguish, so those two
/// fall back to a stored preference.
/// </remarks>
internal static class MitSlotResolver
{
    // ClassJob sheet row ids.
    private const uint Paladin = 19;
    private const uint Warrior = 21;
    private const uint DarkKnight = 32;
    private const uint Gunbreaker = 37;
    private const uint WhiteMage = 24;
    private const uint Scholar = 28;
    private const uint Astrologian = 33;
    private const uint Sage = 40;
    private const uint Bard = 23;
    private const uint Machinist = 31;
    private const uint Dancer = 38;
    private const uint BlackMage = 25;
    private const uint Summoner = 27;
    private const uint RedMage = 35;
    private const uint Pictomancer = 42;

    /// <summary>
    /// The player's seat, or null when no job is readable (loading, between
    /// zones) and the caller should keep whatever it last showed.
    /// </summary>
    public static MitSlot? Resolve(Configuration configuration)
    {
        var job = Services.ClientState.LocalPlayer?.ClassJob.RowId;
        if (job is not { } jobId)
        {
            return null;
        }

        return jobId switch
        {
            WhiteMage => MitSlot.WhiteMage,
            Astrologian => MitSlot.Astrologian,
            Scholar => MitSlot.Scholar,
            Sage => MitSlot.Sage,
            Bard or Machinist or Dancer => MitSlot.D3,
            BlackMage or Summoner or RedMage or Pictomancer => MitSlot.D4,
            Paladin or Warrior or DarkKnight or Gunbreaker =>
                configuration.PlayerIsMainTank ? MitSlot.MainTank : MitSlot.OffTank,
            _ => configuration.PlayerIsFirstMelee ? MitSlot.D1 : MitSlot.D2,
        };
    }

    /// <summary>Whether the job leaves the seat ambiguous, so the UI can say so.</summary>
    public static bool NeedsChoice(out bool tankPair)
    {
        tankPair = false;
        var job = Services.ClientState.LocalPlayer?.ClassJob.RowId;
        if (job is not { } jobId)
        {
            return false;
        }

        if (jobId is Paladin or Warrior or DarkKnight or Gunbreaker)
        {
            tankPair = true;
            return true;
        }

        return jobId is not (WhiteMage or Astrologian or Scholar or Sage
            or Bard or Machinist or Dancer
            or BlackMage or Summoner or RedMage or Pictomancer);
    }
}
