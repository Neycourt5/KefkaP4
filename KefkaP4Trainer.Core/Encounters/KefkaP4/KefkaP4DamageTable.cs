using KefkaP4Trainer.Core.Health;

namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

/// <summary>
/// Scripted raidwide damage for phase 4, for healer practice only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Calibration status: UNVERIFIED.</b> Timing comes from the encounter the
/// trainer already models; the magnitudes are seeded from a single FF Logs
/// report supplied by the user and have not been checked against a second
/// source. Treat these as plausible practice values, not as authoritative
/// encounter data.
/// </para>
/// <para>
/// Timestamps reuse the anchors in <see cref="KefkaP4Mitigation"/> rather than
/// inventing a second clock, so a damage event always lands on the mechanic its
/// mitigation plan row is written for. The two Death Bolt anchors are the
/// interpolated ones documented on that class.
/// </para>
/// <para>
/// The supplied figures are recorded as <see cref="ReferenceScope.PartyTotal"/>.
/// A per-target reading cannot be right: 298,000 unmitigated on one player
/// against a reference pool of 148,000 would make every Grand Cross an
/// unhealable double-overkill, whereas dividing across eight gives ~37,000, a
/// normal raidwide. If the source report turns out to be per-target, flip
/// <see cref="DamageEvent.Scope"/> on the affected rows; nothing else changes.
/// </para>
/// <para>
/// Inferno and Tsunami swap places depending on
/// <c>KefkaP4Assignments.InfernoFirst</c>, so <see cref="For"/> takes that flag
/// rather than baking one ordering into the table.
/// </para>
/// </remarks>
public static class KefkaP4DamageTable
{
    // Anchors, matching KefkaP4Mitigation's own constants.
    private const double GrandCrossOne = 12.6;
    private const double ChaosOne = 18.4;
    private const double GrandCrossTwo = 27.6;
    private const double ChaosTwo = 34.4;
    private const double GrandCrossThree = 44.0;
    private const double Flood = 55.0;
    private const double DeathBoltOne = 64.5;
    private const double UltimaOne = 81.6;
    private const double DeathBoltTwo = 88.5;

    private const string LogNote =
        "FF Logs unmitigated (U:) figure from a single user-supplied report; unverified.";

    /// <summary>
    /// The damage schedule for a pull.
    /// </summary>
    /// <param name="infernoFirst">
    /// From the pull's assignments. Decides which Chaos slot is Inferno and
    /// which is Tsunami.
    /// </param>
    public static IReadOnlyList<DamageEvent> For(bool infernoFirst)
    {
        var firstChaos = infernoFirst ? Inferno(ChaosOne, 1) : Tsunami(ChaosOne, 1);
        var secondChaos = infernoFirst ? Tsunami(ChaosTwo, 2) : Inferno(ChaosTwo, 2);

        return
        [
            Raidwide("grand-cross-1", GrandCrossOne, "Grand Cross", 298_000),
            firstChaos,
            Raidwide("grand-cross-2", GrandCrossTwo, "Grand Cross", 283_000),
            secondChaos,
            Raidwide("grand-cross-3", GrandCrossThree, "Grand Cross", 292_000),

            // The heaviest raidwide in the phase, but survivable: a clear log
            // records the party taking its CORRECT Antilight, which is what this
            // figure is. Going to the wrong side is graded as a Flood failure by
            // the mechanic simulation and is deliberately not modelled as a
            // damage number here.
            Raidwide("white-antilight", Flood, "White Antilight", 741_000),

            Raidwide("death-bolt-1", DeathBoltOne, "Death Bolt", 231_000),
            Raidwide("ultima-upsurge-1", UltimaOne, "Ultima Upsurge", 215_000),
            Raidwide("death-bolt-2", DeathBoltTwo, "Death Bolt", 167_000),
        ];
    }

    /// <summary>Every event, using the default Inferno-first ordering.</summary>
    public static IReadOnlyList<DamageEvent> Default => For(infernoFirst: true);

    private static DamageEvent Raidwide(string id, double time, string name, int reference) =>
        new()
        {
            Id = id,
            Time = time,
            AbilityName = name,
            Target = DamageTargetRule.Party,
            ReferenceUnmitigated = reference,
            Scope = ReferenceScope.PartyTotal,
            SourceNote = LogNote,
        };

    private static DamageEvent Inferno(double time, int index) =>
        Raidwide($"inferno-{index}", time, "Inferno", 234_000);

    private static DamageEvent Tsunami(double time, int index) =>
        Raidwide($"tsunami-{index}", time, "Tsunami", 226_000);
}
