using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;
using KefkaP4Trainer.Core.Health;

namespace KefkaP4Trainer.Healing;

/// <summary>One entry in the observation diagnostic log.</summary>
public sealed record HealerLogEntry(
    ObservedHealerAction Action,
    bool Recognised,
    string Summary);

/// <summary>
/// Owns the virtual party and drives it from observed healer actions and the
/// scripted damage table.
/// </summary>
/// <remarks>
/// Nothing here reads or writes real health. The party is entirely simulated;
/// the only game data involved is which actions the local player pressed.
/// </remarks>
public sealed class HealerPracticeService
{
    private const int LogLimit = 128;

    private readonly HealerActionApplier applier = new();
    private readonly List<HealerLogEntry> log = [];
    private readonly HashSet<string> firedDamageEvents = [];

    private double previousTime = double.NegativeInfinity;
    private int previousPull = -1;

    public HealerPracticeService(IHealerActionObserver observer)
    {
        Observer = observer;
    }

    public IHealerActionObserver Observer { get; }

    public VirtualParty Party { get; } = new();

    public IReadOnlyList<HealerLogEntry> Log => log;

    /// <summary>Damage events that have already resolved this pull.</summary>
    public IReadOnlyCollection<string> FiredDamageEvents => firedDamageEvents;

    public void Update(SimulationEngine engine, Configuration configuration)
    {
        if (!configuration.HealerPracticeEnabled)
        {
            return;
        }

        var time = engine.Clock.Time;

        // A new pull, or any jump backwards, restarts the pull rather than
        // replaying every damage event between the two times.
        if (engine.PullNumber != previousPull || time < previousTime)
        {
            previousPull = engine.PullNumber;
            previousTime = time;
            ResetPull(time);
            return;
        }

        if (engine.Clock.State is not (SimulationState.Running or SimulationState.Countdown))
        {
            previousTime = time;
            return;
        }

        foreach (var action in Observer.Poll(time))
        {
            var applied = applier.Apply(Party, action, time);
            Append(new HealerLogEntry(action, applied.Recognised, applied.Summary));
        }

        Party.Advance(time);

        if (configuration.HealerDamageEnabled)
        {
            foreach (var damageEvent in KefkaP4DamageTable.For(engine.Encounter.Assignments.InfernoFirst))
            {
                if (damageEvent.Time > previousTime
                    && damageEvent.Time <= time
                    && firedDamageEvents.Add(damageEvent.Id))
                {
                    _ = Party.ApplyDamage(damageEvent, time);
                }
            }
        }

        previousTime = time;
    }

    public void ResetPull(double time = 0)
    {
        Party.Reset(time);
        Observer.Reset();
        firedDamageEvents.Clear();
        log.Clear();
    }

    /// <summary>
    /// Feeds an action in by hand. Used for the actions the observer cannot see
    /// and for testing the pipeline without a game.
    /// </summary>
    public void Inject(uint actionId, string actionName, double time, PartyRole? target)
    {
        var action = new ObservedHealerAction
        {
            ActionId = actionId,
            ActionName = actionName,
            SimulationTime = time,
            ObservedAtUtc = DateTime.UtcNow,
            SourceName = "manual",
            TargetSlot = target,
            WasCast = false,
            Method = ObservationMethod.ManualInjection,
            Confidence = ObservationConfidence.High,
        };

        var applied = applier.Apply(Party, action, time);
        Append(new HealerLogEntry(action, applied.Recognised, applied.Summary));
    }

    private void Append(HealerLogEntry entry)
    {
        log.Add(entry);
        if (log.Count > LogLimit)
        {
            log.RemoveRange(0, log.Count - LogLimit);
        }
    }
}
