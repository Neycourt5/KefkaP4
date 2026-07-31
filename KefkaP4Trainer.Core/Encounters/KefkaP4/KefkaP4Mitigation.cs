namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

/// <summary>A seat in the mitigation plan.</summary>
/// <remarks>
/// Slots, not jobs: the plan assigns work by party seat, and several jobs can sit
/// in one seat. Healer seats resolve from the job alone, D3 is physical ranged
/// and D4 is caster, but the tank and melee seats need the player to say which
/// of the pair they are.
/// </remarks>
public enum MitSlot
{
    MainTank,
    OffTank,
    WhiteMage,
    Astrologian,
    Scholar,
    Sage,
    D1,
    D2,
    D3,
    D4,
}

/// <param name="CarryOver">
/// True when the plan marks this with an arrow: the effect is still running from
/// an earlier press and nothing needs pressing now. Alerts skip these.
/// </param>
public readonly record struct MitigationCall(
    double Time,
    string Mechanic,
    MitSlot Slot,
    string Action,
    bool CarryOver);

/// <summary>
/// The phase 4 mitigation plan.
/// </summary>
/// <remarks>
/// Anchored to the mechanics the encounter already models rather than to the
/// plan's own clock, which starts roughly sixteen seconds before this timeline
/// does. The plan lists its mechanics in exactly the order the timeline fires
/// them, so matching them up needs no clock alignment at all.
/// <para>
/// The two Death Bolt/Wave rows are the exception: the encounter does not model
/// that cast, so their times are interpolated between the neighbouring anchors
/// using the plan's own spacing. Every other entry sits on a real event.
/// </para>
/// </remarks>
public static class KefkaP4Mitigation
{
    private const double GrandCrossOne = 12.6;
    private const double ChaosOne = 18.4;
    private const double GrandCrossTwo = 27.6;
    private const double ChaosTwo = 34.4;
    private const double GrandCrossThree = 44.0;
    private const double Flood = 55.0;
    private const double DeathBoltOne = 64.5;
    private const double UltimaOne = 81.6;
    private const double DeathBoltTwo = 88.5;
    private const double UltimaTwo = 120.3;

    public static readonly IReadOnlyList<MitigationCall> All =
    [
        C(GrandCrossOne, "Grand Cross 1", MitSlot.WhiteMage, "Plenary Indulgence"),
        C(GrandCrossOne, "Grand Cross 1", MitSlot.Astrologian, "Collective Unconscious"),
        C(GrandCrossOne, "Grand Cross 1", MitSlot.Scholar, "Spreadlo + Sacred Soil"),
        C(GrandCrossOne, "Grand Cross 1", MitSlot.Sage, "Kerachole + Philosophia + Holos"),
        C(GrandCrossOne, "Grand Cross 1", MitSlot.D2, "Feint (open the phase with it, for autos)"),

        Carry(ChaosOne, "Inferno/Tsunami 1", MitSlot.WhiteMage, "Plenary Indulgence"),
        Carry(ChaosOne, "Inferno/Tsunami 1", MitSlot.Astrologian, "Collective Unconscious"),
        Carry(ChaosOne, "Inferno/Tsunami 1", MitSlot.Scholar, "Sacred Soil"),
        Carry(ChaosOne, "Inferno/Tsunami 1", MitSlot.Sage, "Kerachole + Holos"),
        C(ChaosOne, "Inferno/Tsunami 1", MitSlot.D3, "Party Mit"),

        C(GrandCrossTwo, "Grand Cross 2", MitSlot.WhiteMage, "Temperance"),
        C(GrandCrossTwo, "Grand Cross 2", MitSlot.Astrologian, "Neutral Sect"),
        C(GrandCrossTwo, "Grand Cross 2", MitSlot.Scholar, "Expedient + Fey Illumination"),
        Carry(GrandCrossTwo, "Grand Cross 2", MitSlot.Scholar, "Sacred Soil"),
        C(GrandCrossTwo, "Grand Cross 2", MitSlot.Sage, "Panhaima"),
        Carry(GrandCrossTwo, "Grand Cross 2", MitSlot.Sage, "Holos"),
        Carry(GrandCrossTwo, "Grand Cross 2", MitSlot.D3, "Party Mit"),

        C(ChaosTwo, "Inferno/Tsunami 2", MitSlot.OffTank, "Party Mit (GNB/DRK)"),
        Carry(ChaosTwo, "Inferno/Tsunami 2", MitSlot.WhiteMage, "Temperance"),
        C(ChaosTwo, "Inferno/Tsunami 2", MitSlot.Astrologian, "Sun Sign"),
        Carry(ChaosTwo, "Inferno/Tsunami 2", MitSlot.Astrologian, "Neutral Sect"),
        C(ChaosTwo, "Inferno/Tsunami 2", MitSlot.Scholar, "Seraph"),
        Carry(ChaosTwo, "Inferno/Tsunami 2", MitSlot.Scholar, "Expedient + Fey Illumination"),
        Carry(ChaosTwo, "Inferno/Tsunami 2", MitSlot.Sage, "Panhaima"),

        C(GrandCrossThree, "Grand Cross 3", MitSlot.OffTank, "Party Mit (WAR/PLD)"),
        Carry(GrandCrossThree, "Grand Cross 3", MitSlot.OffTank, "Party Mit (GNB/DRK)"),
        C(GrandCrossThree, "Grand Cross 3", MitSlot.WhiteMage, "Divine Caress"),
        Carry(GrandCrossThree, "Grand Cross 3", MitSlot.WhiteMage, "Temperance"),
        Carry(GrandCrossThree, "Grand Cross 3", MitSlot.Astrologian, "Neutral Sect + Sun Sign"),
        C(GrandCrossThree, "Grand Cross 3", MitSlot.Scholar, "Seraph"),
        Carry(GrandCrossThree, "Grand Cross 3", MitSlot.Scholar, "Expedient + Fey Illumination"),
        C(GrandCrossThree, "Grand Cross 3", MitSlot.Sage, "Zoe Shields"),

        C(Flood, "Flood of Naught", MitSlot.WhiteMage, "Liturgy of the Bell"),
        C(Flood, "Flood of Naught", MitSlot.Astrologian, "Macrocosmos"),
        C(Flood, "Flood of Naught", MitSlot.Scholar, "Sacred Soil"),
        C(Flood, "Flood of Naught", MitSlot.Sage, "Kerachole"),

        C(DeathBoltOne, "Death Bolt/Wave 1", MitSlot.MainTank, "Party Mit"),
        Carry(DeathBoltOne, "Death Bolt/Wave 1", MitSlot.Scholar, "Sacred Soil"),
        Carry(DeathBoltOne, "Death Bolt/Wave 1", MitSlot.Sage, "Kerachole"),

        C(UltimaOne, "Ultima Upsurge 1", MitSlot.MainTank, "Reprisal"),
        C(UltimaOne, "Ultima Upsurge 1", MitSlot.WhiteMage, "Plenary Indulgence"),
        C(UltimaOne, "Ultima Upsurge 1", MitSlot.Astrologian, "Collective Unconscious"),
        C(UltimaOne, "Ultima Upsurge 1", MitSlot.Scholar, "Sacred Soil"),
        C(UltimaOne, "Ultima Upsurge 1", MitSlot.Sage, "Kerachole"),
        C(UltimaOne, "Ultima Upsurge 1", MitSlot.D1, "Feint"),
        C(UltimaOne, "Ultima Upsurge 1", MitSlot.D4, "Addle"),

        Carry(DeathBoltTwo, "Death Bolt/Wave 2", MitSlot.WhiteMage, "Plenary Indulgence"),
        Carry(DeathBoltTwo, "Death Bolt/Wave 2", MitSlot.Astrologian, "Collective Unconscious"),
        Carry(DeathBoltTwo, "Death Bolt/Wave 2", MitSlot.Scholar, "Sacred Soil"),
        Carry(DeathBoltTwo, "Death Bolt/Wave 2", MitSlot.Sage, "Kerachole"),

        C(UltimaTwo, "Ultima Upsurge 2", MitSlot.OffTank, "Reprisal"),
        C(UltimaTwo, "Ultima Upsurge 2", MitSlot.Scholar, "Sacred Soil"),
        C(UltimaTwo, "Ultima Upsurge 2", MitSlot.Sage, "Kerachole"),
    ];

    /// <summary>
    /// The next call for <paramref name="slot"/> at or after <paramref name="time"/>,
    /// skipping carry-overs because they need no press.
    /// </summary>
    public static MitigationCall? Next(MitSlot slot, double time)
    {
        for (var index = 0; index < All.Count; index++)
        {
            var call = All[index];
            if (!call.CarryOver && call.Slot == slot && call.Time >= time)
            {
                return call;
            }
        }

        return null;
    }

    /// <summary>Everything assigned to <paramref name="slot"/>, carry-overs included.</summary>
    public static IReadOnlyList<MitigationCall> For(MitSlot slot)
    {
        var calls = new List<MitigationCall>();
        for (var index = 0; index < All.Count; index++)
        {
            if (All[index].Slot == slot)
            {
                calls.Add(All[index]);
            }
        }

        return calls;
    }

    private static MitigationCall C(double time, string mechanic, MitSlot slot, string action) =>
        new(time, mechanic, slot, action, CarryOver: false);

    private static MitigationCall Carry(
        double time, string mechanic, MitSlot slot, string action) =>
        new(time, mechanic, slot, action, CarryOver: true);
}
