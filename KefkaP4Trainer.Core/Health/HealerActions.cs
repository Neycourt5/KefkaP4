namespace KefkaP4Trainer.Core.Health;

/// <summary>What an observed action does to the virtual party.</summary>
public enum HealerActionKind
{
    Unknown,
    DirectHeal,
    AoeHeal,
    Regen,
    Shield,
    PartyMitigation,
    SingleTargetMitigation,
    GroundHeal,
    Raise,
}

/// <summary>
/// How an action was spotted. Dalamud API 15 exposes no action or combat-event
/// service, so every method here is a poll over state the game already
/// publishes; none of them hook, inject or read packets.
/// </summary>
public enum ObservationMethod
{
    /// <summary>
    /// The cast bar completed. Reliable, but only ever sees casted actions.
    /// </summary>
    CastBar,

    /// <summary>
    /// An action's recast timer restarted. This is the only safe way to see an
    /// instant or an oGCD, since neither touches the cast bar.
    /// </summary>
    CooldownTransition,

    /// <summary>
    /// A known status appeared on a party member. Catches the effect rather
    /// than the press, so the caster cannot always be attributed.
    /// </summary>
    StatusAppearance,

    /// <summary>Injected from the debug window. Never produced by the game.</summary>
    ManualInjection,

    /// <summary>
    /// Emitted by the simulated co-healer. Never produced by the game, and kept
    /// distinct so its contribution can be attributed in the log.
    /// </summary>
    VirtualCoHealer,
}

public enum ObservationConfidence
{
    /// <summary>The action is identified unambiguously.</summary>
    High,

    /// <summary>The action is identified, but the target or caster is inferred.</summary>
    Medium,

    /// <summary>Something fired, but which action is a guess.</summary>
    Low,
}

/// <summary>
/// One healer action the observer believes the local player used.
/// </summary>
/// <remarks>
/// Deliberately free of Dalamud types so the simulation, the tests and any
/// future network layer can all handle it without referencing the game.
/// </remarks>
public sealed record ObservedHealerAction
{
    public required uint ActionId { get; init; }

    public required string ActionName { get; init; }

    /// <summary>Encounter clock time, matching every other simulated timestamp.</summary>
    public required double SimulationTime { get; init; }

    /// <summary>Wall clock, for the diagnostic log only.</summary>
    public required DateTime ObservedAtUtc { get; init; }

    public required string SourceName { get; init; }

    /// <summary>Resolved virtual slot, when the target could be determined.</summary>
    public PartyRole? TargetSlot { get; init; }

    public required bool WasCast { get; init; }

    public required ObservationMethod Method { get; init; }

    public required ObservationConfidence Confidence { get; init; }

    /// <summary>
    /// Identity for duplicate suppression. The same press seen on several
    /// framework ticks, or by two methods at once, must collapse to one entry.
    /// </summary>
    public string DuplicateKey => $"{Method switch
    {
        ObservationMethod.ManualInjection => "manual",
        ObservationMethod.VirtualCoHealer => "cohealer",
        _ => "game",
    }}:{ActionId}:{SourceName}";
}

/// <summary>
/// Collapses repeat sightings of one press.
/// </summary>
/// <remarks>
/// Both live observation methods poll, so a single press is visible for as long
/// as the poll condition holds — several framework ticks at minimum, and the
/// cast-bar and cooldown methods can both see the same GCD. Suppression is
/// keyed on action plus caster within a window, which is safe because no healer
/// action in the game can legitimately be pressed twice inside it.
/// </remarks>
public sealed class HealerActionDeduplicator(double windowSeconds = 0.75)
{
    private readonly Dictionary<string, double> lastSeen = [];

    public double WindowSeconds { get; } = windowSeconds <= 0 ? 0.75 : windowSeconds;

    /// <summary>
    /// True when the action is new and should be handled. False when it is a
    /// repeat sighting inside the window.
    /// </summary>
    public bool TryAccept(ObservedHealerAction action)
    {
        var key = action.DuplicateKey;
        if (lastSeen.TryGetValue(key, out var previous)
            && action.SimulationTime >= previous
            && action.SimulationTime - previous < WindowSeconds)
        {
            return false;
        }

        lastSeen[key] = action.SimulationTime;
        return true;
    }

    /// <summary>Clears state, for a pull reset or a timeline scrub.</summary>
    public void Reset() => lastSeen.Clear();
}

/// <summary>
/// What a given observer implementation can actually see, so the UI can state
/// the limits rather than implying full coverage.
/// </summary>
public sealed record ObservationCapabilities(
    bool CastedActions,
    bool InstantOgcds,
    bool StatusEffects,
    bool TargetResolution,
    string Notes)
{
    public static ObservationCapabilities None { get; } =
        new(false, false, false, false, "No observer attached.");
}

/// <summary>
/// Source of healer actions. Implemented against the game in the plugin layer
/// and against a scripted list in tests.
/// </summary>
public interface IHealerActionObserver
{
    ObservationCapabilities Capabilities { get; }

    /// <summary>
    /// Actions newly observed since the last poll. Called once per framework
    /// tick; must already be de-duplicated.
    /// </summary>
    IReadOnlyList<ObservedHealerAction> Poll(double simulationTime);

    /// <summary>Drops carried state at a pull reset or a timeline scrub.</summary>
    void Reset();
}
