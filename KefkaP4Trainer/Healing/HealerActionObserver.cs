using FFXIVClientStructs.FFXIV.Client.Game;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Health;

namespace KefkaP4Trainer.Healing;

/// <summary>
/// Watches the local player for healer actions, read-only.
/// </summary>
/// <remarks>
/// <para>
/// Dalamud API 15 ships no action or combat-event service, so there is no
/// supported callback for "the player pressed something". Everything here is a
/// poll over state the game already publishes to plugins. Nothing hooks,
/// injects, reads packets or touches an actor.
/// </para>
/// <para>Two methods, with different reach:</para>
/// <list type="bullet">
/// <item><b>Cast bar.</b> <c>IBattleChara.IsCasting</c> plus
/// <c>CastActionId</c>. Unambiguous, and gives the target object id, but only
/// ever sees casts. Recorded when the bar reaches full rather than when it
/// starts, so an interrupted cast is not counted.</item>
/// <item><b>Recast transition.</b> <c>ActionManager.IsActionOffCooldown</c> and
/// <c>GetCurrentCharges</c> against a watch list. An action that stops being
/// ready, or loses a charge, has just been used. This is the only safe way to
/// see an instant or an oGCD.</item>
/// </list>
/// <para>
/// What this cannot do: an instant <i>GCD</i> cannot be identified, because
/// every GCD shares one recast group and the group tells you a GCD fired but
/// not which one. Actions used by other players are also out of reach; only the
/// local player is observed.
/// </para>
/// </remarks>
internal sealed unsafe class HealerActionObserver : IHealerActionObserver
{
    private readonly HealerActionDeduplicator deduplicator = new();
    private readonly Dictionary<uint, uint> previousSignal = [];
    private readonly List<ObservedHealerAction> pending = [];

    private uint castingActionId;
    private float castingProgress;
    private bool warned;

    public ObservationCapabilities Capabilities { get; } = new(
        CastedActions: true,
        InstantOgcds: true,
        StatusEffects: false,
        TargetResolution: true,
        Notes: "Cast bar plus recast-timer polling on the local player only. "
            + "Instant GCDs cannot be identified (all GCDs share one recast group), "
            + "other players' actions are not observed, and status-appearance "
            + "detection is not implemented yet.");

    /// <summary>Names resolved from the Action sheet, for the diagnostic log.</summary>
    public IReadOnlyDictionary<uint, string> ResolvedNames { get; private set; } =
        new Dictionary<uint, string>();

    /// <summary>Database rows whose name disagrees with the game's own sheet.</summary>
    public IReadOnlyList<string> NameMismatches { get; private set; } = [];

    /// <summary>
    /// Cross-checks every database id against the Action sheet. One call at
    /// startup is enough to surface every wrong id in the hand-written table.
    /// </summary>
    public void VerifyAgainstGameData()
    {
        var resolved = new Dictionary<uint, string>();
        var mismatches = new List<string>();

        try
        {
            var sheet = Services.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            if (sheet is null)
            {
                return;
            }

            foreach (var definition in HealerActionDatabase.All)
            {
                if (!sheet.TryGetRow(definition.ActionId, out var row))
                {
                    mismatches.Add($"{definition.Name} (id {definition.ActionId}): no such action row.");
                    continue;
                }

                var gameName = row.Name.ExtractText();
                resolved[definition.ActionId] = gameName;
                if (!string.Equals(gameName, definition.Name, StringComparison.OrdinalIgnoreCase))
                {
                    mismatches.Add(
                        $"id {definition.ActionId}: database says \"{definition.Name}\", "
                        + $"game says \"{gameName}\".");
                }
            }
        }
        catch (Exception exception)
        {
            Services.Log.Warning(exception, "KefkaP4Trainer could not verify healer action ids.");
            return;
        }

        ResolvedNames = resolved;
        NameMismatches = mismatches;

        if (mismatches.Count > 0)
        {
            Services.Log.Warning(
                "KefkaP4Trainer healer action database has {Count} id mismatch(es); "
                + "see the healer debug window.",
                mismatches.Count);
        }
    }

    public IReadOnlyList<ObservedHealerAction> Poll(double simulationTime)
    {
        pending.Clear();

        try
        {
            var player = Services.ObjectTable.LocalPlayer;
            if (player is null)
            {
                return pending;
            }

            ObserveCastBar(player, simulationTime);
            ObserveRecastTransitions(simulationTime);
        }
        catch (Exception exception)
        {
            WarnOnce(exception);
        }

        return pending;
    }

    public void Reset()
    {
        deduplicator.Reset();
        previousSignal.Clear();
        castingActionId = 0;
        castingProgress = 0;
    }

    /// <summary>
    /// Records a cast only once it has reached full duration, so a cast that is
    /// moved out of or interrupted never reaches the simulation.
    /// </summary>
    private void ObserveCastBar(
        Dalamud.Game.ClientState.Objects.Types.IBattleChara player,
        double simulationTime)
    {
        if (player.IsCasting)
        {
            castingActionId = player.CastActionId;
            castingProgress = player.TotalCastTime <= 0
                ? 0
                : player.CurrentCastTime / player.TotalCastTime;
            return;
        }

        if (castingActionId == 0)
        {
            return;
        }

        var finishedId = castingActionId;
        var completed = castingProgress >= 0.95f;
        castingActionId = 0;
        castingProgress = 0;

        if (completed)
        {
            Accept(finishedId, simulationTime, wasCast: true, ObservationMethod.CastBar);
        }
    }

    /// <summary>
    /// An action going from off-cooldown to on-cooldown is a press.
    /// </summary>
    /// <remarks>
    /// The transition is compared against the previous poll rather than against
    /// an absolute, so an action that was already on cooldown when observation
    /// started is never misreported as a fresh press. A charged action that
    /// still has a charge left stays "off cooldown", so a press that only spends
    /// one of several charges is missed; the charge count is tracked alongside
    /// to catch those.
    /// </remarks>
    private void ObserveRecastTransitions(double simulationTime)
    {
        var manager = ActionManager.Instance();
        if (manager is null)
        {
            return;
        }

        foreach (var actionId in HealerActionDatabase.CooldownWatchList)
        {
            var ready = manager->IsActionOffCooldown(ActionType.Action, actionId);
            var charges = manager->GetCurrentCharges(actionId);

            // Pack both signals into one tracked value: charges when the action
            // has them, otherwise a 0/1 ready flag.
            var signal = charges > 0 ? charges : (ready ? 1u : 0u);
            var hasPrevious = previousSignal.TryGetValue(actionId, out var previous);
            previousSignal[actionId] = signal;

            if (!hasPrevious)
            {
                continue;
            }

            if (signal < previous)
            {
                Accept(actionId, simulationTime, wasCast: false, ObservationMethod.CooldownTransition);
            }
        }
    }

    private void Accept(
        uint actionId,
        double simulationTime,
        bool wasCast,
        ObservationMethod method)
    {
        var definition = HealerActionDatabase.Find(actionId);
        if (definition is null)
        {
            return;
        }

        var action = new ObservedHealerAction
        {
            ActionId = actionId,
            ActionName = ResolvedNames.TryGetValue(actionId, out var name) ? name : definition.Name,
            SimulationTime = simulationTime,
            ObservedAtUtc = DateTime.UtcNow,
            SourceName = "you",
            TargetSlot = null,
            WasCast = wasCast,
            Method = method,
            Confidence = method == ObservationMethod.CastBar
                ? ObservationConfidence.High
                : ObservationConfidence.Medium,
        };

        if (deduplicator.TryAccept(action))
        {
            pending.Add(action);
        }
    }

    private void WarnOnce(Exception exception)
    {
        if (warned)
        {
            return;
        }

        warned = true;
        Services.Log.Warning(exception, "KefkaP4Trainer healer action observation failed.");
    }
}
