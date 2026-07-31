namespace KefkaP4Trainer.Core.Health;

/// <summary>
/// The full audit trail for one damage application against one member, in the
/// order the pipeline computed it. Every intermediate is kept so the debug
/// panel can show the derivation rather than just the final HP.
/// </summary>
public sealed record DamageResolution(
    PartyRole Slot,
    string AbilityName,
    int ReferenceUnmitigated,
    int RawPerTarget,
    int ScaledDamage,
    float MitigationFraction,
    int PostMitigation,
    int ShieldAbsorbed,
    int HpLost,
    int Overkill,
    int HpBefore,
    int HpAfter,
    bool Killed,
    bool WasAlreadyDead)
{
    public string Breakdown =>
        $"raw {RawPerTarget:N0} -> scaled {ScaledDamage:N0} -> mit {MitigationFraction * 100:0.#}% "
        + $"-> {PostMitigation:N0} -> shield {ShieldAbsorbed:N0} -> hp -{HpLost:N0} "
        + $"({HpBefore:N0} -> {HpAfter:N0})"
        + (Overkill > 0 ? $" [overkill {Overkill:N0}]" : string.Empty);
}

/// <summary>The audit trail for one healing application.</summary>
public sealed record HealResolution(
    PartyRole Slot,
    string ActionName,
    string Source,
    int RawHeal,
    int EffectiveHeal,
    int Overheal,
    int HpBefore,
    int HpAfter,
    bool Rejected,
    string? RejectedReason)
{
    public string Breakdown => Rejected
        ? $"rejected: {RejectedReason}"
        : $"heal {RawHeal:N0} -> effective {EffectiveHeal:N0} (overheal {Overheal:N0}) "
            + $"({HpBefore:N0} -> {HpAfter:N0})";
}

/// <summary>The audit trail for one shield application.</summary>
public sealed record ShieldResolution(
    PartyRole Slot,
    string ActionName,
    string Source,
    int Amount,
    int TotalShieldAfter,
    bool Rejected,
    string? RejectedReason);

public enum HealthLogKind
{
    Damage,
    Heal,
    Shield,
    Mitigation,
    Regen,
    RegenTick,
    Death,
    Raise,
    Reset,
}

/// <summary>One line in the rolling party-health history.</summary>
public sealed record HealthLogEntry(
    double Time,
    HealthLogKind Kind,
    PartyRole? Slot,
    string Summary,
    string Source);
