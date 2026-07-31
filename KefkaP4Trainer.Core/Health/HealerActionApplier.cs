namespace KefkaP4Trainer.Core.Health;

/// <summary>The outcome of feeding one observed action into the virtual party.</summary>
public sealed record AppliedHealerAction(
    ObservedHealerAction Action,
    HealerActionDefinition? Definition,
    IReadOnlyList<HealResolution> Heals,
    IReadOnlyList<ShieldResolution> Shields,
    bool Recognised,
    string Summary);

/// <summary>
/// Turns observed actions into virtual-party effects.
/// </summary>
/// <remarks>
/// Kept apart from both the observer and the party so the mapping can be tested
/// without a game and without a rendering surface, and so a virtual co-healer
/// can drive the exact same path a real observed action does.
/// </remarks>
public sealed class HealerActionApplier
{
    /// <summary>
    /// Applies <paramref name="action"/>. An action with no database entry is
    /// recorded and reported unrecognised rather than guessed at.
    /// </summary>
    public AppliedHealerAction Apply(
        VirtualParty party,
        ObservedHealerAction action,
        double time)
    {
        var definition = HealerActionDatabase.Find(action.ActionId);
        if (definition is null)
        {
            return new AppliedHealerAction(
                action, null, [], [], false,
                $"{action.ActionName} (id {action.ActionId}) is not in the action database.");
        }

        // Party-wide actions ignore the resolved target. A single-target action
        // with no resolved target falls back to the lowest-HP living member,
        // which is what a healer pressing it would almost always have picked.
        var targets = definition.IsPartyWide
            ? party.Members.ToList()
            : [ResolveSingleTarget(party, action)];

        var heals = new List<HealResolution>();
        var shields = new List<ShieldResolution>();

        foreach (var member in targets)
        {
            if (definition.HealFraction > 0)
            {
                heals.Add(party.Heal(
                    member.Slot,
                    definition.Name,
                    action.SourceName,
                    Amount(member, definition.HealFraction),
                    time));
            }

            if (definition.ShieldFraction > 0)
            {
                shields.Add(party.Shield(
                    member.Slot,
                    definition.Name,
                    action.SourceName,
                    Amount(member, definition.ShieldFraction),
                    definition.DurationSeconds,
                    time));
            }

            if (definition.MitigationFraction > 0)
            {
                party.Mitigate(
                    member.Slot,
                    definition.Name,
                    action.SourceName,
                    definition.MitigationFraction,
                    definition.DurationSeconds,
                    time);
            }

            if (definition.RegenFractionPerTick > 0)
            {
                party.Regen(
                    member.Slot,
                    definition.Name,
                    action.SourceName,
                    Amount(member, definition.RegenFractionPerTick),
                    definition.RegenInterval,
                    definition.DurationSeconds,
                    time);
            }

            if (definition.Kind == HealerActionKind.Raise)
            {
                party.Raise(member.Slot, action.SourceName, time);
            }
        }

        return new AppliedHealerAction(
            action, definition, heals, shields, true, Describe(definition, targets, heals, shields));
    }

    private static SimulatedMember ResolveSingleTarget(VirtualParty party, ObservedHealerAction action)
    {
        if (action.TargetSlot is { } slot && party.Find(slot) is { } named)
        {
            return named;
        }

        var living = party.Members.Where(member => member.IsAlive).ToList();
        return living.Count == 0
            ? party.Members[0]
            : living.OrderBy(member => member.HpFraction).First();
    }

    private static int Amount(SimulatedMember member, float fraction) =>
        (int)Math.Round(member.MaximumHp * Math.Clamp(fraction, 0f, 1f));

    private static string Describe(
        HealerActionDefinition definition,
        IReadOnlyList<SimulatedMember> targets,
        IReadOnlyList<HealResolution> heals,
        IReadOnlyList<ShieldResolution> shields)
    {
        var scope = definition.IsPartyWide
            ? "party"
            : targets.Count > 0 ? targets[0].DisplayName : "nobody";
        var parts = new List<string>();

        if (heals.Count > 0)
        {
            parts.Add($"healed {heals.Sum(h => h.EffectiveHeal):N0} "
                + $"(overheal {heals.Sum(h => h.Overheal):N0})");
        }

        if (shields.Count > 0)
        {
            parts.Add($"shielded {shields.Sum(s => s.Amount):N0}");
        }

        if (definition.MitigationFraction > 0)
        {
            parts.Add($"mit {definition.MitigationFraction * 100:0.#}% "
                + $"for {definition.DurationSeconds:0.#}s");
        }

        if (definition.RegenFractionPerTick > 0)
        {
            parts.Add($"regen {definition.RegenFractionPerTick * 100:0.#}%/tick");
        }

        return parts.Count == 0
            ? $"{definition.Name} -> {scope}: no modelled effect"
            : $"{definition.Name} -> {scope}: {string.Join(", ", parts)}";
    }
}
