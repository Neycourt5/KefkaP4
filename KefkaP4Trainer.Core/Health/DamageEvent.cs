namespace KefkaP4Trainer.Core.Health;

public enum DamageTargetRule
{
    /// <summary>Every living member takes it.</summary>
    Party,

    /// <summary>Tank slots only.</summary>
    Tanks,

    /// <summary>One named slot.</summary>
    Slot,
}

public enum DamageKind
{
    Magical,
    Physical,
    Unaspected,
}

/// <summary>
/// How to read <see cref="DamageEvent.ReferenceUnmitigated"/>.
/// </summary>
/// <remarks>
/// An FF Logs damage-taken row shows the unmitigated figure for one target, but
/// the summary view a player copies numbers out of usually aggregates the whole
/// party for a raidwide. Which one a given figure is changes the per-player
/// result eightfold, so it is recorded explicitly per event rather than assumed.
/// </remarks>
public enum ReferenceScope
{
    /// <summary>The figure is what one player took.</summary>
    PerTarget,

    /// <summary>The figure is the whole party's total for one cast.</summary>
    PartyTotal,
}

/// <summary>
/// One scripted damage application in the encounter, kept separate from both
/// rendering and the timeline so the numbers can be corrected without touching
/// the simulation.
/// </summary>
public sealed record DamageEvent
{
    public required string Id { get; init; }

    /// <summary>Simulation time, on the same clock as the mechanic timeline.</summary>
    public required double Time { get; init; }

    public required string AbilityName { get; init; }

    /// <summary>Game action id, or 0 when it has not been confirmed.</summary>
    public uint AbilityId { get; init; }

    public required DamageTargetRule Target { get; init; }

    public PartyRole? TargetSlot { get; init; }

    public DamageKind Kind { get; init; } = DamageKind.Magical;

    /// <summary>The raw FF Logs "U:" figure, kept verbatim for debugging.</summary>
    public required int ReferenceUnmitigated { get; init; }

    public ReferenceScope Scope { get; init; } = ReferenceScope.PartyTotal;

    /// <summary>Number of separate hits the figure covers.</summary>
    public int HitCount { get; init; } = 1;

    public bool ShieldsApply { get; init; } = true;

    public bool MitigationApplies { get; init; } = true;

    /// <summary>
    /// True when surviving is not the point: the mechanic kills whoever takes
    /// it, so the trainer should not present it as a healing check.
    /// </summary>
    public bool LethalByDesign { get; init; }

    public required string SourceNote { get; init; }

    /// <summary>
    /// Per-target unmitigated damage for one hit, before profile scaling.
    /// </summary>
    public int RawPerTarget(int partySize)
    {
        var hits = Math.Max(1, HitCount);
        var total = Scope == ReferenceScope.PartyTotal
            ? ReferenceUnmitigated / (double)Math.Max(1, TargetCount(partySize))
            : ReferenceUnmitigated;
        return (int)Math.Round(total / hits);
    }

    private int TargetCount(int partySize) => Target switch
    {
        DamageTargetRule.Party => Math.Max(1, partySize),
        DamageTargetRule.Tanks => 2,
        DamageTargetRule.Slot => 1,
        _ => Math.Max(1, partySize),
    };
}

/// <summary>
/// Converts a reference figure onto the currently simulated party.
/// </summary>
/// <remarks>
/// Damage is normalised as a fraction of the reference player's maximum HP and
/// re-expressed against the simulated maximum, so changing the simulated HP
/// profile keeps the *severity* of every mechanic constant. With the defaults
/// the factor is exactly 1 and reference figures pass through untouched.
/// </remarks>
public sealed record DamageScaling(int ReferenceMaximumHp, int SimulatedMaximumHp)
{
    /// <summary>
    /// Reference maximum HP the calibration figures were taken at. Chosen as a
    /// plausible non-tank value for the tier; adjust alongside the dataset if a
    /// better-attested number turns up.
    /// </summary>
    public const int DefaultReferenceMaximumHp = 148_000;

    public static DamageScaling Default { get; } =
        new(DefaultReferenceMaximumHp, DefaultReferenceMaximumHp);

    public double Factor =>
        ReferenceMaximumHp <= 0 ? 1 : (double)SimulatedMaximumHp / ReferenceMaximumHp;

    public int Scale(int rawDamage) => (int)Math.Round(rawDamage * Factor);
}
