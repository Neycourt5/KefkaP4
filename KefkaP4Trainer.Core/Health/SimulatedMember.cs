namespace KefkaP4Trainer.Core.Health;

/// <summary>
/// One of the eight simulated party slots.
/// </summary>
/// <remarks>
/// Entirely virtual. Nothing here reads or writes a real character: the plugin
/// never applies a status, never changes HP and never touches actor memory.
/// This exists so a healer can practise against the encounter's damage profile
/// without a party.
/// </remarks>
public sealed class SimulatedMember
{
    private readonly List<ActiveMitigation> mitigations = [];
    private readonly List<ActiveShield> shields = [];
    private readonly List<ActiveRegen> regens = [];

    public SimulatedMember(PartyRole slot, SimulatedJob job, int maximumHp)
    {
        Slot = slot;
        Job = job;
        MaximumHp = Math.Max(1, maximumHp);
        CurrentHp = MaximumHp;
        DisplayName = slot.Key().ToUpperInvariant();
    }

    public PartyRole Slot { get; }

    public string Id => Slot.Key();

    public SimulatedJob Job { get; set; }

    public string DisplayName { get; set; }

    public int MaximumHp { get; private set; }

    public int CurrentHp { get; private set; }

    public bool IsAlive => CurrentHp > 0;

    public string? DeathReason { get; private set; }

    public double? DiedAt { get; private set; }

    /// <summary>True when this slot is driven by the local player.</summary>
    public bool IsLocalPlayer { get; set; }

    /// <summary>
    /// Session id of a connected plugin client steering this slot, or null when
    /// the slot is a bot. Reserved for the multiplayer work; unused today.
    /// </summary>
    public string? ConnectedParticipantId { get; set; }

    public bool IsBot => !IsLocalPlayer && ConnectedParticipantId is null;

    public IReadOnlyList<ActiveMitigation> Mitigations => mitigations;

    public IReadOnlyList<ActiveShield> Shields => shields;

    public IReadOnlyList<ActiveRegen> Regens => regens;

    public float HpFraction => MaximumHp <= 0 ? 0 : (float)CurrentHp / MaximumHp;

    public void SetMaximumHp(int maximumHp)
    {
        MaximumHp = Math.Max(1, maximumHp);
        CurrentHp = Math.Min(CurrentHp, MaximumHp);
    }

    public int TotalShieldAt(double time)
    {
        var total = 0;
        foreach (var shield in shields)
        {
            if (shield.IsActiveAt(time))
            {
                total += shield.Remaining;
            }
        }

        return total;
    }

    /// <summary>
    /// Combined reduction from every running mitigation, stacked
    /// multiplicatively as the game does.
    /// </summary>
    public float MitigationFractionAt(double time)
    {
        var remaining = 1f;
        foreach (var mitigation in mitigations)
        {
            if (mitigation.IsActiveAt(time))
            {
                remaining *= 1f - Math.Clamp(mitigation.Fraction, 0f, 0.95f);
            }
        }

        return Math.Clamp(1f - remaining, 0f, 0.99f);
    }

    public DamageResolution TakeDamage(
        string abilityName,
        int referenceUnmitigated,
        int rawPerTarget,
        int scaledDamage,
        bool mitigationApplies,
        bool shieldsApply,
        double time)
    {
        var hpBefore = CurrentHp;
        if (!IsAlive)
        {
            return new DamageResolution(
                Slot, abilityName, referenceUnmitigated, rawPerTarget, scaledDamage,
                0, 0, 0, 0, 0, hpBefore, hpBefore, false, true);
        }

        var mitigationFraction = mitigationApplies ? MitigationFractionAt(time) : 0f;
        var postMitigation = (int)Math.Round(scaledDamage * (1f - mitigationFraction));
        postMitigation = Math.Max(0, postMitigation);

        var absorbed = shieldsApply ? ConsumeShields(postMitigation, time) : 0;
        var incoming = Math.Max(0, postMitigation - absorbed);

        // HpLost is what actually left the pool. Reporting the incoming figure
        // here would show a 300k hit against a 100k pool as 300k lost, which
        // makes the debug breakdown lie about the HP line.
        var hpLost = Math.Min(hpBefore, incoming);
        var overkill = incoming - hpLost;

        CurrentHp = hpBefore - hpLost;
        var killed = CurrentHp == 0 && hpBefore > 0;
        if (killed)
        {
            DeathReason = abilityName;
            DiedAt = time;
        }

        return new DamageResolution(
            Slot, abilityName, referenceUnmitigated, rawPerTarget, scaledDamage,
            mitigationFraction, postMitigation, absorbed, hpLost, overkill,
            hpBefore, CurrentHp, killed, false);
    }

    /// <summary>
    /// Spends shields against incoming damage, soonest-expiring first so a
    /// short shield is not left stranded behind a long one.
    /// </summary>
    private int ConsumeShields(int damage, double time)
    {
        if (damage <= 0)
        {
            return 0;
        }

        var absorbed = 0;
        foreach (var shield in shields.Where(s => s.IsActiveAt(time)).OrderBy(s => s.ExpiresAt))
        {
            if (absorbed >= damage)
            {
                break;
            }

            var take = Math.Min(shield.Remaining, damage - absorbed);
            shield.Remaining -= take;
            absorbed += take;
        }

        return absorbed;
    }

    public HealResolution ReceiveHeal(string actionName, string source, int amount, double time)
    {
        var hpBefore = CurrentHp;
        if (!IsAlive)
        {
            // The game does not heal the dead; a raise has to come first.
            return new HealResolution(
                Slot, actionName, source, amount, 0, 0, hpBefore, hpBefore, true, "target is dead");
        }

        var raw = Math.Max(0, amount);
        var effective = Math.Min(raw, MaximumHp - CurrentHp);
        CurrentHp += effective;
        return new HealResolution(
            Slot, actionName, source, raw, effective, raw - effective,
            hpBefore, CurrentHp, false, null);
    }

    public ShieldResolution ApplyShield(
        string actionName,
        string source,
        int amount,
        double appliedAt,
        double duration,
        double time)
    {
        if (!IsAlive)
        {
            return new ShieldResolution(
                Slot, actionName, source, 0, TotalShieldAt(time), true, "target is dead");
        }

        // Same id refreshes rather than stacks, matching how the game treats a
        // reapplied shield from the same action.
        var id = $"{source}:{actionName}";
        shields.RemoveAll(s => string.Equals(s.Id, id, StringComparison.Ordinal));
        shields.Add(new ActiveShield
        {
            Id = id,
            Name = actionName,
            InitialAmount = Math.Max(0, amount),
            Remaining = Math.Max(0, amount),
            AppliedAt = appliedAt,
            ExpiresAt = duration <= 0 ? double.PositiveInfinity : appliedAt + duration,
            Source = source,
        });

        return new ShieldResolution(
            Slot, actionName, source, Math.Max(0, amount), TotalShieldAt(time), false, null);
    }

    public void ApplyMitigation(
        string actionName,
        string source,
        float fraction,
        double appliedAt,
        double duration)
    {
        var id = $"{source}:{actionName}";
        mitigations.RemoveAll(m => string.Equals(m.Id, id, StringComparison.Ordinal));
        mitigations.Add(new ActiveMitigation
        {
            Id = id,
            Name = actionName,
            Fraction = Math.Clamp(fraction, 0f, 0.95f),
            AppliedAt = appliedAt,
            ExpiresAt = duration <= 0 ? double.PositiveInfinity : appliedAt + duration,
            Source = source,
        });
    }

    public void ApplyRegen(
        string actionName,
        string source,
        int amountPerTick,
        double intervalSeconds,
        double appliedAt,
        double duration)
    {
        var id = $"{source}:{actionName}";
        regens.RemoveAll(r => string.Equals(r.Id, id, StringComparison.Ordinal));
        var interval = intervalSeconds <= 0 ? 3 : intervalSeconds;
        regens.Add(new ActiveRegen
        {
            Id = id,
            Name = actionName,
            AmountPerTick = Math.Max(0, amountPerTick),
            IntervalSeconds = interval,
            AppliedAt = appliedAt,
            ExpiresAt = duration <= 0 ? double.PositiveInfinity : appliedAt + duration,
            Source = source,
            NextTickAt = appliedAt + interval,
        });
    }

    /// <summary>
    /// Fires every regen tick due at or before <paramref name="time"/>. Catches
    /// up rather than skipping, so a lag spike or a fast playback speed cannot
    /// silently drop healing.
    /// </summary>
    public IReadOnlyList<HealResolution> TickRegens(double time)
    {
        List<HealResolution>? results = null;
        foreach (var regen in regens)
        {
            // Guard against a pathological interval producing an unbounded loop.
            var fired = 0;
            while (regen.IsActiveAt(regen.NextTickAt)
                && regen.NextTickAt <= time
                && fired < 64)
            {
                var tickTime = regen.NextTickAt;
                regen.NextTickAt += regen.IntervalSeconds;
                fired++;
                if (!IsAlive)
                {
                    continue;
                }

                var result = ReceiveHeal(regen.Name, regen.Source, regen.AmountPerTick, tickTime);
                (results ??= []).Add(result);
            }
        }

        return (IReadOnlyList<HealResolution>?)results ?? [];
    }

    /// <summary>Drops effects whose window has closed, and spent shields.</summary>
    public void ExpireEffects(double time)
    {
        mitigations.RemoveAll(m => time >= m.ExpiresAt);
        shields.RemoveAll(s => time >= s.ExpiresAt || s.Remaining <= 0);
        regens.RemoveAll(r => time >= r.ExpiresAt);
    }

    public bool Raise(int hpPercent)
    {
        if (IsAlive)
        {
            return false;
        }

        CurrentHp = Math.Clamp(
            (int)Math.Round(MaximumHp * (Math.Clamp(hpPercent, 1, 100) / 100.0)),
            1,
            MaximumHp);
        DeathReason = null;
        DiedAt = null;
        return true;
    }

    public void Reset()
    {
        CurrentHp = MaximumHp;
        DeathReason = null;
        DiedAt = null;
        mitigations.Clear();
        shields.Clear();
        regens.Clear();
    }
}
