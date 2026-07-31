namespace KefkaP4Trainer.Core.Health;

/// <summary>
/// What one healer action does to the virtual party.
/// </summary>
/// <remarks>
/// Effects are expressed as fractions of the target's maximum HP rather than as
/// potency, so the model stays coherent without simulating spell speed, weapon
/// damage, healing bonus or party buffs. That is a deliberate first cut: the
/// shape of this record leaves room for a potency/stat model later, and the
/// percentages are tuned to feel like the real action rather than to be exact.
/// </remarks>
public sealed record HealerActionDefinition
{
    public required uint ActionId { get; init; }

    public required string Name { get; init; }

    public required SimulatedJob Job { get; init; }

    public required HealerActionKind Kind { get; init; }

    /// <summary>True when the action is a cast rather than an instant.</summary>
    public bool IsCast { get; init; }

    /// <summary>True when the action affects the whole party.</summary>
    public bool IsPartyWide { get; init; }

    /// <summary>Immediate heal, as a fraction of the target's maximum HP.</summary>
    public float HealFraction { get; init; }

    /// <summary>Shield applied, as a fraction of the target's maximum HP.</summary>
    public float ShieldFraction { get; init; }

    /// <summary>Damage reduction in [0, 0.95].</summary>
    public float MitigationFraction { get; init; }

    /// <summary>Per-tick regen, as a fraction of the target's maximum HP.</summary>
    public float RegenFractionPerTick { get; init; }

    public double RegenInterval { get; init; } = 3;

    /// <summary>Effect duration in seconds. Zero means instantaneous.</summary>
    public double DurationSeconds { get; init; }

    /// <summary>Recast in seconds, used by cooldown-transition detection.</summary>
    public double RecastSeconds { get; init; }

    public string Notes { get; init; } = string.Empty;
}

/// <summary>
/// The healer actions the trainer understands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Action ids are UNVERIFIED.</b> They were written without a running game
/// to check them against. The plugin resolves each id's real name from the
/// Lumina Action sheet at startup and logs any row whose sheet name disagrees
/// with <see cref="HealerActionDefinition.Name"/>, so one launch is enough to
/// find and correct every wrong id. Nothing silently mis-fires: an id that
/// matches nothing simply never triggers.
/// </para>
/// <para>
/// Scope is deliberately the actions the mitigation plan in
/// <see cref="Encounters.KefkaP4.KefkaP4Mitigation"/> already names, plus the
/// core healing kit for each job. That keeps the table small enough to verify
/// by hand and covers what phase 4 actually asks a healer to press.
/// </para>
/// </remarks>
public static class HealerActionDatabase
{
    private static readonly IReadOnlyDictionary<uint, HealerActionDefinition> ById =
        BuildAll().ToDictionary(definition => definition.ActionId);

    public static IReadOnlyCollection<HealerActionDefinition> All => (IReadOnlyCollection<HealerActionDefinition>)ById.Values;

    public static HealerActionDefinition? Find(uint actionId) =>
        ById.TryGetValue(actionId, out var definition) ? definition : null;

    public static IEnumerable<HealerActionDefinition> ForJob(SimulatedJob job) =>
        ById.Values.Where(definition => definition.Job == job);

    /// <summary>Every id the observer needs to watch a recast timer for.</summary>
    public static IReadOnlyList<uint> CooldownWatchList =>
        ById.Values
            .Where(definition => !definition.IsCast && definition.RecastSeconds > 3)
            .Select(definition => definition.ActionId)
            .ToArray();

    private static IEnumerable<HealerActionDefinition> BuildAll()
    {
        // ---- White Mage (pure) ----
        yield return Heal(120, "Cure", SimulatedJob.WhiteMage, 0.10f, cast: true);
        yield return Heal(135, "Cure II", SimulatedJob.WhiteMage, 0.22f, cast: true);
        yield return Aoe(131, "Cure III", SimulatedJob.WhiteMage, 0.24f, cast: true);
        yield return Aoe(124, "Medica", SimulatedJob.WhiteMage, 0.14f, cast: true);
        yield return Aoe(37010, "Medica III", SimulatedJob.WhiteMage, 0.16f, cast: true,
            regenFraction: 0.03f, duration: 15);
        yield return RegenAction(137, "Regen", SimulatedJob.WhiteMage, 0.035f, 18);
        yield return Ogcd(140, "Benediction", SimulatedJob.WhiteMage, HealerActionKind.DirectHeal,
            180, healFraction: 1.0f, notes: "Full heal.");
        yield return Ogcd(3570, "Tetragrammaton", SimulatedJob.WhiteMage, HealerActionKind.DirectHeal,
            60, healFraction: 0.20f);
        yield return Ogcd(7432, "Divine Benison", SimulatedJob.WhiteMage, HealerActionKind.Shield,
            30, shieldFraction: 0.12f, duration: 15);
        yield return OgcdParty(7433, "Plenary Indulgence", SimulatedJob.WhiteMage,
            HealerActionKind.PartyMitigation, 60, mitigation: 0.10f, duration: 10);
        yield return OgcdParty(16536, "Temperance", SimulatedJob.WhiteMage,
            HealerActionKind.PartyMitigation, 120, mitigation: 0.10f, duration: 20);
        yield return OgcdParty(3569, "Asylum", SimulatedJob.WhiteMage,
            HealerActionKind.GroundHeal, 90, regenFraction: 0.03f, duration: 24);
        yield return OgcdParty(25862, "Liturgy of the Bell", SimulatedJob.WhiteMage,
            HealerActionKind.GroundHeal, 180, regenFraction: 0.02f, duration: 20);
        yield return Ogcd(25861, "Aquaveil", SimulatedJob.WhiteMage,
            HealerActionKind.SingleTargetMitigation, 60, mitigation: 0.15f, duration: 8);
        yield return OgcdParty(37011, "Divine Caress", SimulatedJob.WhiteMage,
            HealerActionKind.Shield, 1, shieldFraction: 0.10f, duration: 10);
        yield return Raise(125, "Raise", SimulatedJob.WhiteMage);

        // ---- Scholar (barrier) ----
        yield return Heal(190, "Physick", SimulatedJob.Scholar, 0.10f, cast: true);
        yield return Shield(185, "Adloquium", SimulatedJob.Scholar, 0.18f, 0.12f, 30, cast: true);
        yield return AoeShield(186, "Succor", SimulatedJob.Scholar, 0.10f, 0.10f, 30, cast: true);
        yield return AoeShield(37012, "Concitation", SimulatedJob.Scholar, 0.12f, 0.12f, 30, cast: true);
        yield return Ogcd(189, "Lustrate", SimulatedJob.Scholar, HealerActionKind.DirectHeal,
            1, healFraction: 0.22f);
        yield return Ogcd(7434, "Excogitation", SimulatedJob.Scholar, HealerActionKind.DirectHeal,
            45, healFraction: 0.22f, duration: 45);
        yield return OgcdParty(3583, "Indomitability", SimulatedJob.Scholar,
            HealerActionKind.AoeHeal, 30, healFraction: 0.16f);
        yield return OgcdParty(188, "Sacred Soil", SimulatedJob.Scholar,
            HealerActionKind.PartyMitigation, 30, mitigation: 0.10f, duration: 15,
            regenFraction: 0.01f);
        yield return OgcdParty(16537, "Fey Illumination", SimulatedJob.Scholar,
            HealerActionKind.PartyMitigation, 120, mitigation: 0.05f, duration: 20);
        yield return OgcdParty(25868, "Expedient", SimulatedJob.Scholar,
            HealerActionKind.PartyMitigation, 120, mitigation: 0.10f, duration: 20);
        yield return Ogcd(25867, "Protraction", SimulatedJob.Scholar,
            HealerActionKind.SingleTargetMitigation, 60, mitigation: 0.10f, duration: 10);
        yield return OgcdParty(37014, "Seraphism", SimulatedJob.Scholar,
            HealerActionKind.PartyMitigation, 180, mitigation: 0.10f, duration: 20);
        yield return Raise(173, "Resurrection", SimulatedJob.Scholar);

        // ---- Astrologian (pure) ----
        yield return Heal(3594, "Benefic", SimulatedJob.Astrologian, 0.10f, cast: true);
        yield return Heal(3610, "Benefic II", SimulatedJob.Astrologian, 0.22f, cast: true);
        yield return Aoe(3600, "Helios", SimulatedJob.Astrologian, 0.14f, cast: true);
        yield return Aoe(37030, "Helios Conjunction", SimulatedJob.Astrologian, 0.14f, cast: true,
            regenFraction: 0.03f, duration: 15);
        yield return Ogcd(3614, "Essential Dignity", SimulatedJob.Astrologian,
            HealerActionKind.DirectHeal, 40, healFraction: 0.25f);
        yield return Ogcd(16556, "Celestial Intersection", SimulatedJob.Astrologian,
            HealerActionKind.Shield, 30, healFraction: 0.10f, shieldFraction: 0.10f, duration: 30);
        yield return OgcdParty(16553, "Celestial Opposition", SimulatedJob.Astrologian,
            HealerActionKind.AoeHeal, 60, healFraction: 0.10f, regenFraction: 0.02f, duration: 15);
        yield return OgcdParty(3613, "Collective Unconscious", SimulatedJob.Astrologian,
            HealerActionKind.PartyMitigation, 60, mitigation: 0.10f, duration: 18,
            regenFraction: 0.01f);
        yield return OgcdParty(16559, "Neutral Sect", SimulatedJob.Astrologian,
            HealerActionKind.PartyMitigation, 120, mitigation: 0.10f, duration: 20);
        yield return OgcdParty(37031, "Sun Sign", SimulatedJob.Astrologian,
            HealerActionKind.PartyMitigation, 1, mitigation: 0.10f, duration: 15);
        yield return Ogcd(25873, "Exaltation", SimulatedJob.Astrologian,
            HealerActionKind.SingleTargetMitigation, 60, mitigation: 0.10f, duration: 8);
        yield return OgcdParty(25874, "Macrocosmos", SimulatedJob.Astrologian,
            HealerActionKind.AoeHeal, 180, healFraction: 0.15f, duration: 15);
        yield return Raise(3603, "Ascend", SimulatedJob.Astrologian);

        // ---- Sage (barrier) ----
        yield return Heal(24284, "Diagnosis", SimulatedJob.Sage, 0.10f, cast: true);
        yield return Shield(24291, "Eukrasian Diagnosis", SimulatedJob.Sage, 0.10f, 0.18f, 30, cast: true);
        yield return Aoe(24286, "Prognosis", SimulatedJob.Sage, 0.10f, cast: true);
        yield return AoeShield(24292, "Eukrasian Prognosis", SimulatedJob.Sage, 0f, 0.10f, 30, cast: true);
        yield return Ogcd(24296, "Druochole", SimulatedJob.Sage, HealerActionKind.DirectHeal,
            1, healFraction: 0.22f);
        yield return OgcdParty(24303, "Ixochole", SimulatedJob.Sage, HealerActionKind.AoeHeal,
            30, healFraction: 0.16f);
        yield return OgcdParty(24298, "Kerachole", SimulatedJob.Sage,
            HealerActionKind.PartyMitigation, 30, mitigation: 0.10f, duration: 15,
            regenFraction: 0.01f);
        yield return OgcdParty(24288, "Physis II", SimulatedJob.Sage,
            HealerActionKind.GroundHeal, 60, regenFraction: 0.03f, duration: 15);
        yield return OgcdParty(24310, "Holos", SimulatedJob.Sage,
            HealerActionKind.PartyMitigation, 120, mitigation: 0.10f, shieldFraction: 0.10f,
            duration: 20);
        yield return OgcdParty(24311, "Panhaima", SimulatedJob.Sage,
            HealerActionKind.Shield, 120, shieldFraction: 0.10f, duration: 15);
        yield return Ogcd(24305, "Haima", SimulatedJob.Sage, HealerActionKind.Shield,
            120, shieldFraction: 0.10f, duration: 15);
        yield return OgcdParty(37035, "Philosophia", SimulatedJob.Sage,
            HealerActionKind.PartyMitigation, 180, mitigation: 0.10f, duration: 20);
        yield return Ogcd(24317, "Krasis", SimulatedJob.Sage,
            HealerActionKind.SingleTargetMitigation, 60, mitigation: 0.0f, duration: 10,
            notes: "Increases healing received; not modelled as mitigation.");
        yield return Raise(24287, "Egeiro", SimulatedJob.Sage);

        // ---- Shared role mitigation the plan asks non-healers for ----
        yield return OgcdParty(7535, "Reprisal", SimulatedJob.Unknown,
            HealerActionKind.PartyMitigation, 60, mitigation: 0.10f, duration: 15,
            notes: "Tank role action.");
        yield return OgcdParty(7549, "Feint", SimulatedJob.Unknown,
            HealerActionKind.PartyMitigation, 90, mitigation: 0.06f, duration: 15,
            notes: "Melee role action; physical only in game, flattened here.");
        yield return OgcdParty(7560, "Addle", SimulatedJob.Unknown,
            HealerActionKind.PartyMitigation, 90, mitigation: 0.06f, duration: 15,
            notes: "Caster role action; magical only in game, flattened here.");
    }

    private static HealerActionDefinition Heal(
        uint id, string name, SimulatedJob job, float healFraction, bool cast) =>
        new()
        {
            ActionId = id, Name = name, Job = job, Kind = HealerActionKind.DirectHeal,
            IsCast = cast, HealFraction = healFraction, RecastSeconds = 2.5,
        };

    private static HealerActionDefinition Aoe(
        uint id, string name, SimulatedJob job, float healFraction, bool cast,
        float regenFraction = 0, double duration = 0) =>
        new()
        {
            ActionId = id, Name = name, Job = job, Kind = HealerActionKind.AoeHeal,
            IsCast = cast, IsPartyWide = true, HealFraction = healFraction,
            RegenFractionPerTick = regenFraction, DurationSeconds = duration,
            RecastSeconds = 2.5,
        };

    private static HealerActionDefinition Shield(
        uint id, string name, SimulatedJob job, float healFraction, float shieldFraction,
        double duration, bool cast) =>
        new()
        {
            ActionId = id, Name = name, Job = job, Kind = HealerActionKind.Shield,
            IsCast = cast, HealFraction = healFraction, ShieldFraction = shieldFraction,
            DurationSeconds = duration, RecastSeconds = 2.5,
        };

    private static HealerActionDefinition AoeShield(
        uint id, string name, SimulatedJob job, float healFraction, float shieldFraction,
        double duration, bool cast) =>
        new()
        {
            ActionId = id, Name = name, Job = job, Kind = HealerActionKind.Shield,
            IsCast = cast, IsPartyWide = true, HealFraction = healFraction,
            ShieldFraction = shieldFraction, DurationSeconds = duration, RecastSeconds = 2.5,
        };

    private static HealerActionDefinition RegenAction(
        uint id, string name, SimulatedJob job, float perTick, double duration) =>
        new()
        {
            ActionId = id, Name = name, Job = job, Kind = HealerActionKind.Regen,
            RegenFractionPerTick = perTick, DurationSeconds = duration, RecastSeconds = 2.5,
        };

    private static HealerActionDefinition Ogcd(
        uint id, string name, SimulatedJob job, HealerActionKind kind, double recast,
        float healFraction = 0, float shieldFraction = 0, float mitigation = 0,
        float regenFraction = 0, double duration = 0, string notes = "") =>
        new()
        {
            ActionId = id, Name = name, Job = job, Kind = kind,
            HealFraction = healFraction, ShieldFraction = shieldFraction,
            MitigationFraction = mitigation, RegenFractionPerTick = regenFraction,
            DurationSeconds = duration, RecastSeconds = recast, Notes = notes,
        };

    private static HealerActionDefinition OgcdParty(
        uint id, string name, SimulatedJob job, HealerActionKind kind, double recast,
        float healFraction = 0, float shieldFraction = 0, float mitigation = 0,
        float regenFraction = 0, double duration = 0, string notes = "") =>
        Ogcd(id, name, job, kind, recast, healFraction, shieldFraction, mitigation,
            regenFraction, duration, notes) with
        { IsPartyWide = true };

    private static HealerActionDefinition Raise(uint id, string name, SimulatedJob job) =>
        new()
        {
            ActionId = id, Name = name, Job = job, Kind = HealerActionKind.Raise,
            IsCast = true, RecastSeconds = 2.5,
        };
}
