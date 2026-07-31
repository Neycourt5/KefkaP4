namespace KefkaP4Trainer.Core.Health;

/// <summary>How much the simulated co-healer does.</summary>
public enum CoHealerAssistance
{
    /// <summary>No co-healer at all.</summary>
    Disabled,

    /// <summary>Party mitigation only. The healing is entirely yours.</summary>
    Minimal,

    /// <summary>
    /// Mitigation plus one pre-shield or post-damage heal per raidwide. Meant to
    /// leave the pull survivable but not solved.
    /// </summary>
    Standard,

    /// <summary>Adds regens and emergency healing on top of Standard.</summary>
    Strong,

    /// <summary>Whatever the individual switches say.</summary>
    Custom,
}

/// <summary>
/// What the co-healer is allowed to do.
/// </summary>
/// <remarks>
/// The presets deliberately escalate by <i>category</i> rather than by magnitude.
/// Scaling one healer's numbers up and down would make the practice dishonest —
/// the real healer would learn to expect a heal size that no real co-healer
/// produces. Adding or removing whole responsibilities is what actually changes
/// how much work is left over.
/// </remarks>
public sealed record CoHealerSettings
{
    /// <summary>Party mitigation before each raidwide.</summary>
    public bool Mitigation { get; init; }

    /// <summary>A shield (barrier) or a heal (pure) around each raidwide.</summary>
    public bool CoreHealing { get; init; }

    /// <summary>A rolling regen through the pull.</summary>
    public bool Regens { get; init; }

    /// <summary>A single big heal when someone is about to die.</summary>
    public bool EmergencyHealing { get; init; }

    /// <summary>Seconds before a damage event that shields and mitigation land.</summary>
    public double PreCastLead { get; init; } = 2.0;

    /// <summary>Seconds after a damage event that reactive healing lands.</summary>
    public double PostHealDelay { get; init; } = 1.5;

    /// <summary>HP fraction below which the emergency heal fires.</summary>
    public float EmergencyThreshold { get; init; } = 0.35f;

    /// <summary>Minimum seconds between emergency heals.</summary>
    public double EmergencyCooldown { get; init; } = 45;

    /// <summary>
    /// Take one raidwide in every N. Two means the co-healer covers alternate
    /// raidwides and the player is responsible for the ones in between, which is
    /// how a real pair actually splits cooldowns. One means it covers everything.
    /// </summary>
    public int CoverageStride { get; init; } = 2;

    public static CoHealerSettings For(CoHealerAssistance level) => level switch
    {
        CoHealerAssistance.Disabled => new CoHealerSettings(),
        CoHealerAssistance.Minimal => new CoHealerSettings
        {
            Mitigation = true,
            CoverageStride = 2,
        },
        CoHealerAssistance.Standard => new CoHealerSettings
        {
            Mitigation = true,
            CoreHealing = true,
            CoverageStride = 2,
        },
        CoHealerAssistance.Strong => new CoHealerSettings
        {
            Mitigation = true,
            CoreHealing = true,
            Regens = true,
            EmergencyHealing = true,
            CoverageStride = 1,
        },
        _ => new CoHealerSettings { Mitigation = true, CoreHealing = true },
    };

    public bool DoesAnything => Mitigation || CoreHealing || Regens || EmergencyHealing;
}

/// <summary>
/// A deterministic simulated second healer.
/// </summary>
/// <remarks>
/// <para>
/// Exists so one real healer can practise the phase without a partner. It is
/// complementary by design: a barrier healer is paired with a pure/regen
/// co-healer and vice versa, so the shape of the work left over resembles a real
/// duo rather than a mirror of the player.
/// </para>
/// <para>
/// It emits <see cref="ObservedHealerAction"/> through the same applier a real
/// observed press goes through. It never touches HP directly and never reduces
/// boss damage: everything it contributes is visible in the history as shields,
/// mitigation, regens and heals, attributed to "co-healer".
/// </para>
/// <para>
/// Fully deterministic — no randomness, and every decision is a function of
/// simulation time and party state, so the same pull replays identically.
/// </para>
/// </remarks>
public sealed class VirtualCoHealer
{
    /// <summary>Actions the barrier stand-in presses, all from the shared database.</summary>
    private const uint BarrierMitigation = 188;    // Sacred Soil
    private const uint BarrierPartyShield = 186;   // Succor
    private const uint BarrierRegen = 24298;       // Kerachole
    private const uint BarrierEmergency = 189;     // Lustrate

    /// <summary>Actions the pure stand-in presses.</summary>
    private const uint PureMitigation = 7433;      // Plenary Indulgence
    private const uint PurePartyHeal = 131;        // Cure III
    private const uint PureRegen = 37010;          // Medica III
    private const uint PureEmergency = 140;        // Benediction

    private readonly HashSet<string> fired = [];
    private double lastEmergencyAt = double.NegativeInfinity;

    public VirtualCoHealer(HealerProfile profile, CoHealerSettings settings)
    {
        Profile = profile;
        Settings = settings;
    }

    /// <summary>The profile this co-healer covers, i.e. the complement of the player's.</summary>
    public HealerProfile Profile { get; }

    public CoHealerSettings Settings { get; set; }

    /// <summary>Display name used as the action source, so its work is attributable.</summary>
    public string SourceName =>
        Profile == HealerProfile.Barrier ? "co-healer (barrier)" : "co-healer (pure)";

    /// <summary>
    /// The complementary co-healer for a real healer's job, or null when the job
    /// is not a healer.
    /// </summary>
    public static VirtualCoHealer? ComplementOf(SimulatedJob playerJob, CoHealerSettings settings)
    {
        var complement = playerJob.Profile().Complement();
        return complement == HealerProfile.None ? null : new VirtualCoHealer(complement, settings);
    }

    /// <summary>
    /// Actions due in <c>(previousTime, time]</c>.
    /// </summary>
    /// <remarks>
    /// The half-open window means a scrub backwards produces nothing, and the
    /// fired-key set stops a replayed span from double-casting. Both are needed:
    /// the window alone would refire everything after a reset.
    /// </remarks>
    public IReadOnlyList<ObservedHealerAction> Update(
        double previousTime,
        double time,
        VirtualParty party,
        IReadOnlyList<DamageEvent> damageEvents)
    {
        if (!Settings.DoesAnything || time <= previousTime)
        {
            return [];
        }

        List<ObservedHealerAction>? due = null;
        var stride = Math.Max(1, Settings.CoverageStride);

        for (var index = 0; index < damageEvents.Count; index++)
        {
            // Alternate coverage: the raidwides this skips are the player's.
            if (index % stride != 0)
            {
                continue;
            }

            var damageEvent = damageEvents[index];
            if (Settings.Mitigation)
            {
                Consider(
                    ref due,
                    $"mit:{damageEvent.Id}",
                    damageEvent.Time - Settings.PreCastLead,
                    previousTime,
                    time,
                    Profile == HealerProfile.Barrier ? BarrierMitigation : PureMitigation);
            }

            if (Settings.CoreHealing)
            {
                // A barrier healer front-loads absorption; a pure healer repairs
                // the damage afterwards. That difference is the whole point of
                // pairing the player with the opposite profile.
                if (Profile == HealerProfile.Barrier)
                {
                    Consider(
                        ref due,
                        $"core:{damageEvent.Id}",
                        damageEvent.Time - Settings.PreCastLead,
                        previousTime,
                        time,
                        BarrierPartyShield);
                }
                else
                {
                    Consider(
                        ref due,
                        $"core:{damageEvent.Id}",
                        damageEvent.Time + Settings.PostHealDelay,
                        previousTime,
                        time,
                        PurePartyHeal);
                }
            }

            if (Settings.Regens)
            {
                Consider(
                    ref due,
                    $"regen:{damageEvent.Id}",
                    damageEvent.Time - Settings.PreCastLead,
                    previousTime,
                    time,
                    Profile == HealerProfile.Barrier ? BarrierRegen : PureRegen);
            }
        }

        if (Settings.EmergencyHealing)
        {
            ConsiderEmergency(ref due, time, party);
        }

        return (IReadOnlyList<ObservedHealerAction>?)due ?? [];
    }

    public void Reset()
    {
        fired.Clear();
        lastEmergencyAt = double.NegativeInfinity;
    }

    private void Consider(
        ref List<ObservedHealerAction>? due,
        string key,
        double moment,
        double previousTime,
        double time,
        uint actionId)
    {
        if (moment <= previousTime || moment > time || !fired.Add(key))
        {
            return;
        }

        (due ??= []).Add(Build(actionId, time, null));
    }

    /// <summary>
    /// One big single-target heal when someone is close to dying, on its own
    /// cooldown so it cannot carry the pull.
    /// </summary>
    private void ConsiderEmergency(
        ref List<ObservedHealerAction>? due,
        double time,
        VirtualParty party)
    {
        if (time - lastEmergencyAt < Settings.EmergencyCooldown)
        {
            return;
        }

        var lowest = party.Members
            .Where(member => member.IsAlive)
            .OrderBy(member => member.HpFraction)
            .FirstOrDefault();

        if (lowest is null || lowest.HpFraction > Settings.EmergencyThreshold)
        {
            return;
        }

        lastEmergencyAt = time;
        var actionId = Profile == HealerProfile.Barrier ? BarrierEmergency : PureEmergency;
        (due ??= []).Add(Build(actionId, time, lowest.Slot));
    }

    private ObservedHealerAction Build(uint actionId, double time, PartyRole? target) =>
        new()
        {
            ActionId = actionId,
            ActionName = HealerActionDatabase.Find(actionId)?.Name ?? $"action {actionId}",
            SimulationTime = time,
            ObservedAtUtc = DateTime.UnixEpoch,
            SourceName = SourceName,
            TargetSlot = target,
            WasCast = false,
            Method = ObservationMethod.VirtualCoHealer,
            Confidence = ObservationConfidence.High,
        };
}
