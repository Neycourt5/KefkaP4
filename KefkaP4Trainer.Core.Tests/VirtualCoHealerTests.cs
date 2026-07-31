using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;
using KefkaP4Trainer.Core.Health;

namespace KefkaP4Trainer.Core.Tests;

public sealed class VirtualCoHealerTests
{
    private const int Hp = 100_000;

    private static VirtualParty NewParty() =>
        new(Hp) { Scaling = new DamageScaling(Hp, Hp) };

    private static IReadOnlyList<DamageEvent> Events => KefkaP4DamageTable.For(infernoFirst: true);

    // ---------------- classification ----------------

    [Theory]
    [InlineData(SimulatedJob.WhiteMage, HealerProfile.Barrier)]
    [InlineData(SimulatedJob.Astrologian, HealerProfile.Barrier)]
    [InlineData(SimulatedJob.Scholar, HealerProfile.Pure)]
    [InlineData(SimulatedJob.Sage, HealerProfile.Pure)]
    public void CoHealerComplementsTheRealHealersProfile(
        SimulatedJob playerJob,
        HealerProfile expected)
    {
        var coHealer = VirtualCoHealer.ComplementOf(
            playerJob, CoHealerSettings.For(CoHealerAssistance.Standard));

        Assert.NotNull(coHealer);
        Assert.Equal(expected, coHealer!.Profile);
    }

    [Theory]
    [InlineData(SimulatedJob.Samurai)]
    [InlineData(SimulatedJob.Paladin)]
    [InlineData(SimulatedJob.Unknown)]
    public void NonHealerJobsGetNoCoHealer(SimulatedJob job) =>
        Assert.Null(VirtualCoHealer.ComplementOf(
            job, CoHealerSettings.For(CoHealerAssistance.Standard)));

    // ---------------- assistance levels ----------------

    [Fact]
    public void DisabledDoesNothingAtAll()
    {
        var settings = CoHealerSettings.For(CoHealerAssistance.Disabled);
        Assert.False(settings.DoesAnything);

        var coHealer = new VirtualCoHealer(HealerProfile.Barrier, settings);
        Assert.Empty(coHealer.Update(0, 60, NewParty(), Events));
    }

    [Fact]
    public void MinimalMitigatesButNeverHeals()
    {
        var party = NewParty();
        var coHealer = new VirtualCoHealer(
            HealerProfile.Barrier, CoHealerSettings.For(CoHealerAssistance.Minimal));

        var actions = coHealer.Update(0, 125, party, Events);

        Assert.NotEmpty(actions);
        Assert.All(actions, action =>
        {
            var definition = HealerActionDatabase.Find(action.ActionId);
            Assert.NotNull(definition);
            Assert.Equal(HealerActionKind.PartyMitigation, definition!.Kind);
        });
    }

    [Fact]
    public void EscalatingTheLevelStrictlyAddsActions()
    {
        int Count(CoHealerAssistance level)
        {
            var coHealer = new VirtualCoHealer(
                HealerProfile.Barrier, CoHealerSettings.For(level));
            var party = NewParty();
            // Damage the party so the emergency branch is reachable at Strong.
            _ = party.ApplyRawDamage("Chip", 80_000, DamageTargetRule.Party, 0);
            return coHealer.Update(0, 125, party, Events).Count;
        }

        var disabled = Count(CoHealerAssistance.Disabled);
        var minimal = Count(CoHealerAssistance.Minimal);
        var standard = Count(CoHealerAssistance.Standard);
        var strong = Count(CoHealerAssistance.Strong);

        Assert.Equal(0, disabled);
        Assert.True(minimal > disabled);
        Assert.True(standard > minimal);
        Assert.True(strong > standard);
    }

    // ---------------- profile behaviour ----------------

    /// <summary>A barrier co-healer shields ahead of the hit.</summary>
    [Fact]
    public void BarrierCoHealerShieldsBeforeTheDamageLands()
    {
        var party = NewParty();
        var coHealer = new VirtualCoHealer(
            HealerProfile.Barrier, CoHealerSettings.For(CoHealerAssistance.Standard));
        var applier = new HealerActionApplier();
        var first = Events[0];

        foreach (var action in coHealer.Update(0, first.Time, party, Events))
        {
            _ = applier.Apply(party, action, action.SimulationTime);
        }

        Assert.All(party.Members, member => Assert.True(member.TotalShieldAt(first.Time) > 0));
        Assert.All(party.Members, member => Assert.True(member.MitigationFractionAt(first.Time) > 0));
    }

    /// <summary>A pure co-healer repairs the damage afterwards instead.</summary>
    [Fact]
    public void PureCoHealerHealsAfterTheDamageLands()
    {
        var party = NewParty();
        var coHealer = new VirtualCoHealer(
            HealerProfile.Pure, CoHealerSettings.For(CoHealerAssistance.Standard));
        var applier = new HealerActionApplier();
        var first = Events[0];

        // Before the hit: mitigation only, no shields.
        foreach (var action in coHealer.Update(0, first.Time, party, Events))
        {
            _ = applier.Apply(party, action, action.SimulationTime);
        }

        Assert.All(party.Members, member => Assert.Equal(0, member.TotalShieldAt(first.Time)));
        Assert.All(party.Members, member => Assert.True(member.MitigationFractionAt(first.Time) > 0));

        _ = party.ApplyRawDamage("Raidwide", 30_000, DamageTargetRule.Party, first.Time);
        var damaged = party[PartyRole.M1].CurrentHp;

        foreach (var action in coHealer.Update(
            first.Time, first.Time + 5, party, Events))
        {
            _ = applier.Apply(party, action, action.SimulationTime);
        }

        Assert.True(party[PartyRole.M1].CurrentHp > damaged);
    }

    // ---------------- determinism and replay safety ----------------

    [Fact]
    public void TheSamePullProducesTheSameActionsEveryTime()
    {
        string Run()
        {
            var party = NewParty();
            var coHealer = new VirtualCoHealer(
                HealerProfile.Barrier, CoHealerSettings.For(CoHealerAssistance.Strong));
            var applier = new HealerActionApplier();
            var log = new List<string>();
            var previous = 0d;

            for (var time = 0.5; time <= 125; time += 0.5)
            {
                foreach (var action in coHealer.Update(previous, time, party, Events))
                {
                    log.Add($"{action.SimulationTime:0.0}:{action.ActionId}");
                    _ = applier.Apply(party, action, time);
                }

                party.Advance(time);
                foreach (var damageEvent in Events.Where(e => e.Time > previous && e.Time <= time))
                {
                    _ = party.ApplyDamage(damageEvent, time);
                }

                previous = time;
            }

            return string.Join("|", log);
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void EachScheduledActionFiresOnlyOnce()
    {
        var coHealer = new VirtualCoHealer(
            HealerProfile.Barrier, CoHealerSettings.For(CoHealerAssistance.Standard));
        var party = NewParty();

        var first = coHealer.Update(0, 125, party, Events);
        var second = coHealer.Update(0, 125, party, Events);

        Assert.NotEmpty(first);
        Assert.Empty(second);
    }

    /// <summary>Scrubbing backwards must not replay the co-healer's whole pull.</summary>
    [Fact]
    public void MovingBackwardsProducesNothing()
    {
        var coHealer = new VirtualCoHealer(
            HealerProfile.Pure, CoHealerSettings.For(CoHealerAssistance.Strong));

        Assert.Empty(coHealer.Update(60, 20, NewParty(), Events));
        Assert.Empty(coHealer.Update(60, 60, NewParty(), Events));
    }

    [Fact]
    public void ResetAllowsTheNextPullToFireAgain()
    {
        var coHealer = new VirtualCoHealer(
            HealerProfile.Barrier, CoHealerSettings.For(CoHealerAssistance.Standard));
        var party = NewParty();

        var first = coHealer.Update(0, 125, party, Events);
        coHealer.Reset();
        var second = coHealer.Update(0, 125, party, Events);

        Assert.Equal(first.Count, second.Count);
    }

    /// <summary>A paused clock advances no time, so nothing should fire.</summary>
    [Fact]
    public void NoTimeElapsedProducesNothing() =>
        Assert.Empty(new VirtualCoHealer(
            HealerProfile.Barrier, CoHealerSettings.For(CoHealerAssistance.Strong))
            .Update(30, 30, NewParty(), Events));

    // ---------------- emergency healing ----------------

    [Fact]
    public void EmergencyHealingFiresOnlyWhenSomeoneIsCritical()
    {
        var settings = CoHealerSettings.For(CoHealerAssistance.Strong);
        var coHealer = new VirtualCoHealer(HealerProfile.Pure, settings);
        var healthy = NewParty();

        // Healthy party: no emergency among the scheduled actions.
        var actions = coHealer.Update(0, 0.1, healthy, []);
        Assert.Empty(actions);

        var critical = NewParty();
        _ = critical.ApplyRawDamage("Chip", 90_000, DamageTargetRule.Slot, 0, PartyRole.T1);

        var emergency = Assert.Single(coHealer.Update(0, 0.1, critical, []));
        Assert.Equal(PartyRole.T1, emergency.TargetSlot);
        Assert.Equal(HealerActionKind.DirectHeal, HealerActionDatabase.Find(emergency.ActionId)!.Kind);
    }

    [Fact]
    public void EmergencyHealingRespectsItsCooldown()
    {
        var coHealer = new VirtualCoHealer(
            HealerProfile.Pure, CoHealerSettings.For(CoHealerAssistance.Strong));
        var party = NewParty();
        _ = party.ApplyRawDamage("Chip", 90_000, DamageTargetRule.Party, 0);

        Assert.Single(coHealer.Update(0, 1, party, []));
        Assert.Empty(coHealer.Update(1, 2, party, []));
        Assert.Empty(coHealer.Update(2, 40, party, []));
        Assert.Single(coHealer.Update(40, 50, party, []));
    }

    [Fact]
    public void EmergencyHealingIgnoresTheDead()
    {
        var coHealer = new VirtualCoHealer(
            HealerProfile.Pure, CoHealerSettings.For(CoHealerAssistance.Strong));
        var party = NewParty();
        _ = party.ApplyRawDamage("Kill", Hp, DamageTargetRule.Slot, 0, PartyRole.T1);

        // Everyone else is at full, so nothing is critical and nothing fires.
        Assert.Empty(coHealer.Update(0, 1, party, []));
    }

    // ---------------- the point of the feature ----------------

    /// <summary>
    /// Each level must buy measurably more time, and none of them may carry the
    /// phase alone.
    /// </summary>
    /// <remarks>
    /// Time-to-first-death is the metric rather than survivor count, because the
    /// calibrated phase damage exceeds a single pool several times over: the
    /// scripted events total roughly 336k per player against a 148k reference,
    /// so no lone healer of either profile can complete it. That is correct.
    /// A real duo spends far more than one shield or one party heal per
    /// raidwide, and the whole point of the co-healer is that the remaining
    /// deficit is the real healer's job.
    /// </remarks>
    [Fact]
    public void EachAssistanceLevelBuysTimeWithoutSolvingThePull()
    {
        double FirstDeathAt(CoHealerAssistance level)
        {
            var party = new VirtualParty(DamageScaling.DefaultReferenceMaximumHp);
            var coHealer = new VirtualCoHealer(HealerProfile.Barrier, CoHealerSettings.For(level));
            var applier = new HealerActionApplier();
            var previous = 0d;

            for (var time = 0.5; time <= 125; time += 0.5)
            {
                foreach (var action in coHealer.Update(previous, time, party, Events))
                {
                    _ = applier.Apply(party, action, time);
                }

                party.Advance(time);
                foreach (var damageEvent in Events.Where(e => e.Time > previous && e.Time <= time))
                {
                    _ = party.ApplyDamage(damageEvent, time);
                }

                if (party.Members.Any(member => !member.IsAlive))
                {
                    return time;
                }

                previous = time;
            }

            return double.PositiveInfinity;
        }

        var disabled = FirstDeathAt(CoHealerAssistance.Disabled);
        var minimal = FirstDeathAt(CoHealerAssistance.Minimal);
        var standard = FirstDeathAt(CoHealerAssistance.Standard);
        var strong = FirstDeathAt(CoHealerAssistance.Strong);

        Assert.True(minimal > disabled, $"minimal {minimal} did not beat disabled {disabled}");
        Assert.True(standard > minimal, $"standard {standard} did not beat minimal {minimal}");
        Assert.True(strong >= standard, $"strong {strong} regressed against standard {standard}");

        // The load-bearing requirement: Standard has to leave real work behind.
        // Somebody still dies without the player's own healing.
        Assert.True(
            double.IsFinite(standard),
            "Standard assistance carried the whole phase alone, which defeats the exercise");

        // Strong is allowed to carry it - that is what the setting is for - and
        // as calibrated it does, which is why Standard is the default.
        Assert.True(double.IsPositiveInfinity(strong) || strong > standard);
    }

    /// <summary>
    /// The co-healer must never touch HP directly: everything it does has to
    /// arrive as an attributable action in the history.
    /// </summary>
    [Fact]
    public void EveryContributionIsAttributedInTheHistory()
    {
        var party = NewParty();
        var coHealer = new VirtualCoHealer(
            HealerProfile.Barrier, CoHealerSettings.For(CoHealerAssistance.Strong));
        var applier = new HealerActionApplier();

        foreach (var action in coHealer.Update(0, 20, party, Events))
        {
            Assert.Equal(ObservationMethod.VirtualCoHealer, action.Method);
            Assert.StartsWith("co-healer", action.SourceName, StringComparison.Ordinal);
            _ = applier.Apply(party, action, action.SimulationTime);
        }

        Assert.NotEmpty(party.History);
        Assert.All(
            party.History.Where(entry => entry.Kind != HealthLogKind.Reset),
            entry => Assert.StartsWith("co-healer", entry.Source, StringComparison.Ordinal));
    }

    /// <summary>Its actions must not collide with the player's own dedup keyspace.</summary>
    [Fact]
    public void CoHealerActionsDoNotSuppressThePlayersOwnPresses()
    {
        var dedup = new HealerActionDeduplicator();
        var coHealer = new VirtualCoHealer(
            HealerProfile.Barrier, CoHealerSettings.For(CoHealerAssistance.Minimal));

        var theirs = coHealer.Update(0, 125, NewParty(), Events)[0];
        Assert.True(dedup.TryAccept(theirs));

        var mine = theirs with
        {
            SourceName = "you",
            Method = ObservationMethod.CooldownTransition,
        };
        Assert.True(dedup.TryAccept(mine));
    }
}
