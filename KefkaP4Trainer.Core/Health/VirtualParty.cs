namespace KefkaP4Trainer.Core.Health;

/// <summary>
/// The eight simulated party slots and the deterministic pipeline that resolves
/// damage and healing against them.
/// </summary>
/// <remarks>
/// Pure simulation: no Dalamud, no ImGui, no game reads. Nothing in this class
/// can affect a real character. Given the same starting state and the same
/// ordered calls it always produces the same result, so a whole pull can be
/// replayed in a test without FFXIV.
/// </remarks>
public sealed class VirtualParty
{
    /// <summary>Entries kept before the oldest are dropped.</summary>
    public const int HistoryLimit = 512;

    private readonly List<SimulatedMember> members = [];
    private readonly List<HealthLogEntry> history = [];

    public VirtualParty(int maximumHp = DamageScaling.DefaultReferenceMaximumHp)
    {
        foreach (var slot in PartyRoles.All)
        {
            members.Add(new SimulatedMember(slot, SimulatedJobs.DefaultFor(slot), maximumHp));
        }
    }

    public IReadOnlyList<SimulatedMember> Members => members;

    public IReadOnlyList<HealthLogEntry> History => history;

    public DamageScaling Scaling { get; set; } = DamageScaling.Default;

    public int LivingCount => members.Count(member => member.IsAlive);

    public bool IsWiped => LivingCount == 0;

    public SimulatedMember this[PartyRole slot] =>
        members.First(member => member.Slot == slot);

    public SimulatedMember? Find(PartyRole slot) =>
        members.FirstOrDefault(member => member.Slot == slot);

    /// <summary>Sets every slot's maximum HP and refills, as at a pull start.</summary>
    public void SetMaximumHp(int maximumHp)
    {
        foreach (var member in members)
        {
            member.SetMaximumHp(maximumHp);
        }

        Scaling = Scaling with { SimulatedMaximumHp = maximumHp };
    }

    public void Reset(double time = 0)
    {
        foreach (var member in members)
        {
            member.Reset();
        }

        history.Clear();
        Log(time, HealthLogKind.Reset, null, "Party reset to full HP.", "trainer");
    }

    /// <summary>
    /// Advances effects to <paramref name="time"/>: fires due regen ticks, then
    /// drops anything that has expired.
    /// </summary>
    public IReadOnlyList<HealResolution> Advance(double time)
    {
        List<HealResolution>? ticks = null;
        foreach (var member in members)
        {
            foreach (var tick in member.TickRegens(time))
            {
                (ticks ??= []).Add(tick);
                if (tick.EffectiveHeal > 0)
                {
                    Log(
                        time,
                        HealthLogKind.RegenTick,
                        member.Slot,
                        $"{tick.ActionName} ticked {tick.EffectiveHeal:N0}",
                        tick.Source);
                }
            }

            member.ExpireEffects(time);
        }

        return (IReadOnlyList<HealResolution>?)ticks ?? [];
    }

    /// <summary>
    /// Resolves one scripted damage event against every slot it targets.
    /// </summary>
    public IReadOnlyList<DamageResolution> ApplyDamage(DamageEvent damageEvent, double time)
    {
        var targets = SelectTargets(damageEvent).ToList();
        var rawPerTarget = damageEvent.RawPerTarget(members.Count);
        var scaled = Scaling.Scale(rawPerTarget);
        var hits = Math.Max(1, damageEvent.HitCount);
        var results = new List<DamageResolution>(targets.Count * hits);

        for (var hit = 0; hit < hits; hit++)
        {
            foreach (var member in targets)
            {
                var result = member.TakeDamage(
                    damageEvent.AbilityName,
                    damageEvent.ReferenceUnmitigated,
                    rawPerTarget,
                    scaled,
                    damageEvent.MitigationApplies,
                    damageEvent.ShieldsApply,
                    time);
                results.Add(result);

                if (result.WasAlreadyDead)
                {
                    continue;
                }

                Log(time, HealthLogKind.Damage, member.Slot, result.Breakdown, damageEvent.AbilityName);
                if (result.Killed)
                {
                    Log(
                        time,
                        HealthLogKind.Death,
                        member.Slot,
                        $"{member.DisplayName} died to {damageEvent.AbilityName}.",
                        damageEvent.AbilityName);
                }
            }
        }

        return results;
    }

    /// <summary>Damage outside the scripted table, for debug controls and tests.</summary>
    public IReadOnlyList<DamageResolution> ApplyRawDamage(
        string abilityName,
        int perTargetDamage,
        DamageTargetRule target,
        double time,
        PartyRole? slot = null,
        bool mitigationApplies = true,
        bool shieldsApply = true) =>
        ApplyDamage(
            new DamageEvent
            {
                Id = $"manual:{abilityName}",
                Time = time,
                AbilityName = abilityName,
                Target = target,
                TargetSlot = slot,
                ReferenceUnmitigated = perTargetDamage,
                Scope = ReferenceScope.PerTarget,
                MitigationApplies = mitigationApplies,
                ShieldsApply = shieldsApply,
                SourceNote = "manual debug injection",
            },
            time);

    public HealResolution Heal(PartyRole slot, string actionName, string source, int amount, double time)
    {
        var member = this[slot];
        var result = member.ReceiveHeal(actionName, source, amount, time);
        Log(time, HealthLogKind.Heal, slot, result.Breakdown, source);
        return result;
    }

    public IReadOnlyList<HealResolution> HealParty(string actionName, string source, int amount, double time)
    {
        var results = new List<HealResolution>(members.Count);
        foreach (var member in members)
        {
            var result = member.ReceiveHeal(actionName, source, amount, time);
            results.Add(result);
            if (!result.Rejected)
            {
                Log(time, HealthLogKind.Heal, member.Slot, result.Breakdown, source);
            }
        }

        return results;
    }

    public ShieldResolution Shield(
        PartyRole slot,
        string actionName,
        string source,
        int amount,
        double duration,
        double time)
    {
        var result = this[slot].ApplyShield(actionName, source, amount, time, duration, time);
        Log(
            time,
            HealthLogKind.Shield,
            slot,
            result.Rejected ? $"{actionName} rejected: {result.RejectedReason}"
                : $"{actionName} shielded {amount:N0} (total {result.TotalShieldAfter:N0})",
            source);
        return result;
    }

    public IReadOnlyList<ShieldResolution> ShieldParty(
        string actionName,
        string source,
        int amount,
        double duration,
        double time)
    {
        var results = new List<ShieldResolution>(members.Count);
        foreach (var member in members)
        {
            results.Add(member.ApplyShield(actionName, source, amount, time, duration, time));
        }

        Log(time, HealthLogKind.Shield, null, $"{actionName} shielded the party for {amount:N0}", source);
        return results;
    }

    public void Mitigate(
        PartyRole slot,
        string actionName,
        string source,
        float fraction,
        double duration,
        double time)
    {
        this[slot].ApplyMitigation(actionName, source, fraction, time, duration);
        Log(
            time,
            HealthLogKind.Mitigation,
            slot,
            $"{actionName} {fraction * 100:0.#}% for {duration:0.#}s",
            source);
    }

    public void MitigateParty(
        string actionName,
        string source,
        float fraction,
        double duration,
        double time)
    {
        foreach (var member in members)
        {
            member.ApplyMitigation(actionName, source, fraction, time, duration);
        }

        Log(
            time,
            HealthLogKind.Mitigation,
            null,
            $"{actionName} {fraction * 100:0.#}% party-wide for {duration:0.#}s",
            source);
    }

    public void Regen(
        PartyRole slot,
        string actionName,
        string source,
        int amountPerTick,
        double interval,
        double duration,
        double time)
    {
        this[slot].ApplyRegen(actionName, source, amountPerTick, interval, time, duration);
        Log(
            time,
            HealthLogKind.Regen,
            slot,
            $"{actionName} {amountPerTick:N0}/{interval:0.#}s for {duration:0.#}s",
            source);
    }

    public void RegenParty(
        string actionName,
        string source,
        int amountPerTick,
        double interval,
        double duration,
        double time)
    {
        foreach (var member in members)
        {
            member.ApplyRegen(actionName, source, amountPerTick, interval, time, duration);
        }

        Log(
            time,
            HealthLogKind.Regen,
            null,
            $"{actionName} {amountPerTick:N0}/{interval:0.#}s party-wide for {duration:0.#}s",
            source);
    }

    public bool Raise(PartyRole slot, string source, double time, int hpPercent = 50)
    {
        var member = this[slot];
        if (!member.Raise(hpPercent))
        {
            return false;
        }

        Log(time, HealthLogKind.Raise, slot, $"{member.DisplayName} raised at {hpPercent}%.", source);
        return true;
    }

    private IEnumerable<SimulatedMember> SelectTargets(DamageEvent damageEvent) => damageEvent.Target switch
    {
        DamageTargetRule.Party => members,
        DamageTargetRule.Tanks => members.Where(m => m.Job.Role() == RoleKind.Tank),
        DamageTargetRule.Slot => damageEvent.TargetSlot is { } slot
            ? members.Where(m => m.Slot == slot)
            : [],
        _ => members,
    };

    private void Log(double time, HealthLogKind kind, PartyRole? slot, string summary, string source)
    {
        history.Add(new HealthLogEntry(time, kind, slot, summary, source));
        if (history.Count > HistoryLimit)
        {
            history.RemoveRange(0, history.Count - HistoryLimit);
        }
    }
}
