using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;
using KefkaP4Trainer.Core.Health;

namespace KefkaP4Trainer.Core.Tests;

public sealed class VirtualPartyTests
{
    private const int Hp = 100_000;

    private static VirtualParty NewParty()
    {
        var party = new VirtualParty(Hp);
        party.Scaling = new DamageScaling(Hp, Hp);
        return party;
    }

    [Fact]
    public void PartyStartsWithEightLivingMembersAtFullHp()
    {
        var party = NewParty();
        Assert.Equal(8, party.Members.Count);
        Assert.Equal(8, party.LivingCount);
        Assert.False(party.IsWiped);
        Assert.All(party.Members, m => Assert.Equal(Hp, m.CurrentHp));
        Assert.Equal(PartyRoles.All.Length, party.Members.Select(m => m.Slot).Distinct().Count());
    }

    [Fact]
    public void RaidwideDamageHitsEverySlot()
    {
        var party = NewParty();
        var results = party.ApplyRawDamage("Test Raidwide", 10_000, DamageTargetRule.Party, 0);

        Assert.Equal(8, results.Count);
        Assert.All(party.Members, m => Assert.Equal(Hp - 10_000, m.CurrentHp));
    }

    [Fact]
    public void TargetedDamageHitsOnlyTheNamedSlot()
    {
        var party = NewParty();
        var results = party.ApplyRawDamage(
            "Tankbuster", 20_000, DamageTargetRule.Slot, 0, PartyRole.T1);

        Assert.Single(results);
        Assert.Equal(Hp - 20_000, party[PartyRole.T1].CurrentHp);
        Assert.Equal(Hp, party[PartyRole.T2].CurrentHp);
    }

    [Fact]
    public void TankRuleHitsBothTanksOnly()
    {
        var party = NewParty();
        var results = party.ApplyRawDamage("Shared Tankbuster", 30_000, DamageTargetRule.Tanks, 0);

        Assert.Equal(2, results.Count);
        Assert.Equal(Hp - 30_000, party[PartyRole.T1].CurrentHp);
        Assert.Equal(Hp - 30_000, party[PartyRole.T2].CurrentHp);
        Assert.Equal(Hp, party[PartyRole.H1].CurrentHp);
    }

    /// <summary>Mitigation stacks multiplicatively, as in the game.</summary>
    [Fact]
    public void MultipleMitigationsStackMultiplicatively()
    {
        var party = NewParty();
        party.Mitigate(PartyRole.H1, "Reprisal", "T1", 0.10f, 15, 0);
        party.Mitigate(PartyRole.H1, "Feint", "M1", 0.10f, 15, 0);

        // 1 - (0.9 * 0.9) = 0.19, not 0.20.
        Assert.Equal(0.19f, party[PartyRole.H1].MitigationFractionAt(0), 4);

        var result = party.ApplyRawDamage(
            "Raidwide", 10_000, DamageTargetRule.Slot, 0, PartyRole.H1).Single();
        Assert.Equal(8_100, result.PostMitigation);
        Assert.Equal(Hp - 8_100, result.HpAfter);
    }

    [Fact]
    public void ExpiredMitigationStopsApplying()
    {
        var party = NewParty();
        party.Mitigate(PartyRole.H1, "Reprisal", "T1", 0.20f, 10, 0);

        Assert.Equal(0.20f, party[PartyRole.H1].MitigationFractionAt(5), 4);
        Assert.Equal(0f, party[PartyRole.H1].MitigationFractionAt(10), 4);

        var result = party.ApplyRawDamage(
            "Late", 10_000, DamageTargetRule.Slot, 11, PartyRole.H1).Single();
        Assert.Equal(10_000, result.PostMitigation);
    }

    [Fact]
    public void ShieldsAbsorbBeforeHpAndAreConsumed()
    {
        var party = NewParty();
        party.Shield(PartyRole.H1, "Adloquium", "H2", 8_000, 30, 0);

        var result = party.ApplyRawDamage(
            "Raidwide", 5_000, DamageTargetRule.Slot, 1, PartyRole.H1).Single();

        Assert.Equal(5_000, result.ShieldAbsorbed);
        Assert.Equal(0, result.HpLost);
        Assert.Equal(Hp, party[PartyRole.H1].CurrentHp);
        Assert.Equal(3_000, party[PartyRole.H1].TotalShieldAt(1));
    }

    [Fact]
    public void ShieldBreaksAndOverflowReachesHp()
    {
        var party = NewParty();
        party.Shield(PartyRole.H1, "Adloquium", "H2", 4_000, 30, 0);

        var result = party.ApplyRawDamage(
            "Raidwide", 10_000, DamageTargetRule.Slot, 1, PartyRole.H1).Single();

        Assert.Equal(4_000, result.ShieldAbsorbed);
        Assert.Equal(6_000, result.HpLost);
        Assert.Equal(Hp - 6_000, party[PartyRole.H1].CurrentHp);
        Assert.Equal(0, party[PartyRole.H1].TotalShieldAt(1));
    }

    /// <summary>Mitigation is applied before shields, so a shield lasts longer under it.</summary>
    [Fact]
    public void MitigationIsAppliedBeforeShieldAbsorption()
    {
        var party = NewParty();
        party.Mitigate(PartyRole.H1, "Party Mit", "T1", 0.20f, 30, 0);
        party.Shield(PartyRole.H1, "Adloquium", "H2", 10_000, 30, 0);

        var result = party.ApplyRawDamage(
            "Raidwide", 10_000, DamageTargetRule.Slot, 1, PartyRole.H1).Single();

        Assert.Equal(8_000, result.PostMitigation);
        Assert.Equal(8_000, result.ShieldAbsorbed);
        Assert.Equal(0, result.HpLost);
        Assert.Equal(2_000, party[PartyRole.H1].TotalShieldAt(1));
    }

    [Fact]
    public void ShieldsCanBeSkippedByAnEventThatIgnoresThem()
    {
        var party = NewParty();
        party.Shield(PartyRole.H1, "Adloquium", "H2", 50_000, 30, 0);

        var result = party.ApplyRawDamage(
            "Unshieldable", 10_000, DamageTargetRule.Slot, 1, PartyRole.H1,
            shieldsApply: false).Single();

        Assert.Equal(0, result.ShieldAbsorbed);
        Assert.Equal(10_000, result.HpLost);
    }

    [Fact]
    public void OverkillClampsAtZeroAndRecordsTheDeathReason()
    {
        var party = NewParty();
        var result = party.ApplyRawDamage(
            "Massive", Hp * 3, DamageTargetRule.Slot, 4, PartyRole.M1).Single();

        Assert.True(result.Killed);
        Assert.Equal(0, result.HpAfter);

        // HpLost is what actually left the pool; the excess is reported as
        // overkill rather than inflating the HP line.
        Assert.Equal(Hp, result.HpLost);
        Assert.Equal((Hp * 3) - Hp, result.Overkill);
        Assert.False(party[PartyRole.M1].IsAlive);
        Assert.Equal("Massive", party[PartyRole.M1].DeathReason);
        Assert.Equal(4, party[PartyRole.M1].DiedAt);
        Assert.Equal(7, party.LivingCount);
    }

    [Fact]
    public void DamageOnACorpseIsRecordedButChangesNothing()
    {
        var party = NewParty();
        _ = party.ApplyRawDamage("Kill", Hp, DamageTargetRule.Slot, 0, PartyRole.M1);
        var second = party.ApplyRawDamage("Again", 5_000, DamageTargetRule.Slot, 1, PartyRole.M1).Single();

        Assert.True(second.WasAlreadyDead);
        Assert.False(second.Killed);
        Assert.Equal(0, second.HpLost);
        Assert.Equal("Kill", party[PartyRole.M1].DeathReason);
    }

    [Fact]
    public void WipeIsDetectedWhenEveryoneDies()
    {
        var party = NewParty();
        _ = party.ApplyRawDamage("Wipe", Hp, DamageTargetRule.Party, 0);
        Assert.Equal(0, party.LivingCount);
        Assert.True(party.IsWiped);
    }

    [Fact]
    public void HealingIsCappedAtMaximumHpAndReportsOverheal()
    {
        var party = NewParty();
        _ = party.ApplyRawDamage("Chip", 3_000, DamageTargetRule.Slot, 0, PartyRole.H1);

        var result = party.Heal(PartyRole.H1, "Cure III", "H2", 10_000, 1);

        Assert.Equal(10_000, result.RawHeal);
        Assert.Equal(3_000, result.EffectiveHeal);
        Assert.Equal(7_000, result.Overheal);
        Assert.Equal(Hp, party[PartyRole.H1].CurrentHp);
    }

    [Fact]
    public void HealingTheDeadIsRejected()
    {
        var party = NewParty();
        _ = party.ApplyRawDamage("Kill", Hp, DamageTargetRule.Slot, 0, PartyRole.M1);

        var result = party.Heal(PartyRole.M1, "Cure III", "H1", 50_000, 1);

        Assert.True(result.Rejected);
        Assert.Equal(0, result.EffectiveHeal);
        Assert.Equal(0, party[PartyRole.M1].CurrentHp);
    }

    [Fact]
    public void RaiseRestoresTheDeadAtThePercentageGiven()
    {
        var party = NewParty();
        _ = party.ApplyRawDamage("Kill", Hp, DamageTargetRule.Slot, 0, PartyRole.M1);

        Assert.True(party.Raise(PartyRole.M1, "H1", 5, 50));
        Assert.True(party[PartyRole.M1].IsAlive);
        Assert.Equal(Hp / 2, party[PartyRole.M1].CurrentHp);
        Assert.Null(party[PartyRole.M1].DeathReason);

        // Raising the living is a no-op.
        Assert.False(party.Raise(PartyRole.M1, "H1", 6));
    }

    [Fact]
    public void RegenTicksOnItsIntervalAndStopsAtExpiry()
    {
        var party = NewParty();
        _ = party.ApplyRawDamage("Chip", 50_000, DamageTargetRule.Slot, 0, PartyRole.H1);
        party.Regen(PartyRole.H1, "Regen", "H1", 1_000, 3, 9, 0);

        party.Advance(2);
        Assert.Equal(Hp - 50_000, party[PartyRole.H1].CurrentHp);

        party.Advance(3);
        Assert.Equal(Hp - 49_000, party[PartyRole.H1].CurrentHp);

        // Catches up across a large jump rather than dropping ticks. Effect
        // windows are half-open everywhere in this codebase, so the 9s regen
        // ticks at 3 and 6 only; the tick due exactly at expiry does not fire.
        party.Advance(20);
        Assert.Equal(Hp - 48_000, party[PartyRole.H1].CurrentHp);

        // Expired: no further healing.
        party.Advance(40);
        Assert.Equal(Hp - 48_000, party[PartyRole.H1].CurrentHp);
    }

    [Fact]
    public void ReapplyingTheSameEffectRefreshesRatherThanStacks()
    {
        var party = NewParty();
        party.Shield(PartyRole.H1, "Adloquium", "H2", 5_000, 30, 0);
        party.Shield(PartyRole.H1, "Adloquium", "H2", 5_000, 30, 1);
        Assert.Equal(5_000, party[PartyRole.H1].TotalShieldAt(1));

        party.Mitigate(PartyRole.H1, "Reprisal", "T1", 0.10f, 15, 0);
        party.Mitigate(PartyRole.H1, "Reprisal", "T1", 0.10f, 15, 1);
        Assert.Equal(0.10f, party[PartyRole.H1].MitigationFractionAt(1), 4);
    }

    [Fact]
    public void ShieldsFromDifferentSourcesStack()
    {
        var party = NewParty();
        party.Shield(PartyRole.H1, "Adloquium", "H2", 5_000, 30, 0);
        party.Shield(PartyRole.H1, "Panhaima", "H1", 4_000, 30, 0);
        Assert.Equal(9_000, party[PartyRole.H1].TotalShieldAt(0));
    }

    [Fact]
    public void ResetRestoresEveryoneAndClearsEffects()
    {
        var party = NewParty();
        party.Mitigate(PartyRole.H1, "Reprisal", "T1", 0.2f, 30, 0);
        party.Shield(PartyRole.H1, "Adloquium", "H2", 5_000, 30, 0);
        _ = party.ApplyRawDamage("Wipe", Hp, DamageTargetRule.Party, 0);

        party.Reset();

        Assert.Equal(8, party.LivingCount);
        Assert.All(party.Members, m =>
        {
            Assert.Equal(Hp, m.CurrentHp);
            Assert.Null(m.DeathReason);
            Assert.Empty(m.Mitigations);
            Assert.Empty(m.Shields);
            Assert.Empty(m.Regens);
        });
    }

    /// <summary>
    /// The same ordered calls must produce byte-identical results, or a healer
    /// could not practise the same pull twice.
    /// </summary>
    [Fact]
    public void RepeatedIdenticalPullsProduceIdenticalResults()
    {
        static string RunPull()
        {
            var party = NewParty();
            party.MitigateParty("Party Mit", "T1", 0.10f, 20, 0);
            party.ShieldParty("Succor", "H2", 6_000, 30, 1);
            party.RegenParty("Medica III", "H1", 800, 3, 15, 2);
            foreach (var damageEvent in KefkaP4DamageTable.For(infernoFirst: true))
            {
                party.Advance(damageEvent.Time);
                _ = party.ApplyDamage(damageEvent, damageEvent.Time);
            }

            return string.Join(
                "|",
                party.Members.Select(m => $"{m.Slot}:{m.CurrentHp}:{m.DeathReason ?? "-"}"));
        }

        Assert.Equal(RunPull(), RunPull());
    }

    [Fact]
    public void HistoryRecordsDamageHealAndDeathAndIsBounded()
    {
        var party = NewParty();
        _ = party.ApplyRawDamage("Chip", 1_000, DamageTargetRule.Slot, 0, PartyRole.H1);
        party.Heal(PartyRole.H1, "Cure", "H2", 1_000, 1);
        _ = party.ApplyRawDamage("Kill", Hp, DamageTargetRule.Slot, 2, PartyRole.M1);

        Assert.Contains(party.History, e => e.Kind == HealthLogKind.Damage);
        Assert.Contains(party.History, e => e.Kind == HealthLogKind.Heal);
        Assert.Contains(party.History, e => e.Kind == HealthLogKind.Death && e.Slot == PartyRole.M1);

        for (var i = 0; i < VirtualParty.HistoryLimit + 50; i++)
        {
            party.Heal(PartyRole.H2, "Spam", "H1", 1, 3);
        }

        Assert.True(party.History.Count <= VirtualParty.HistoryLimit);
    }

    [Fact]
    public void ScalingRescalesDamageAgainstTheSimulatedHpPool()
    {
        var party = new VirtualParty(50_000)
        {
            // Reference pool twice the simulated one: damage should halve.
            Scaling = new DamageScaling(100_000, 50_000),
        };

        var result = party.ApplyRawDamage(
            "Raidwide", 10_000, DamageTargetRule.Slot, 0, PartyRole.H1).Single();

        Assert.Equal(5_000, result.ScaledDamage);
        Assert.Equal(45_000, result.HpAfter);
    }

    [Fact]
    public void DefaultScalingLeavesReferenceFiguresUntouched()
    {
        Assert.Equal(1, DamageScaling.Default.Factor, 6);
        Assert.Equal(298_000, DamageScaling.Default.Scale(298_000));
    }
}
