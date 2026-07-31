using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Health;

namespace KefkaP4Trainer.Core.Tests;

public sealed class HealerActionTests
{
    private const int Hp = 100_000;

    private static VirtualParty NewParty() =>
        new(Hp) { Scaling = new DamageScaling(Hp, Hp) };

    private static ObservedHealerAction Action(
        uint id,
        string name,
        double time,
        PartyRole? target = null,
        ObservationMethod method = ObservationMethod.CooldownTransition) =>
        new()
        {
            ActionId = id,
            ActionName = name,
            SimulationTime = time,
            ObservedAtUtc = DateTime.UnixEpoch,
            SourceName = "H1",
            TargetSlot = target,
            WasCast = false,
            Method = method,
            Confidence = ObservationConfidence.High,
        };

    // ---------------- deduplication ----------------

    [Fact]
    public void RepeatSightingsInsideTheWindowAreSuppressed()
    {
        var dedup = new HealerActionDeduplicator(0.75);

        Assert.True(dedup.TryAccept(Action(188, "Sacred Soil", 10.00)));
        Assert.False(dedup.TryAccept(Action(188, "Sacred Soil", 10.05)));
        Assert.False(dedup.TryAccept(Action(188, "Sacred Soil", 10.70)));
        Assert.True(dedup.TryAccept(Action(188, "Sacred Soil", 10.80)));
    }

    [Fact]
    public void DifferentActionsAndCastersAreNotSuppressed()
    {
        var dedup = new HealerActionDeduplicator();

        Assert.True(dedup.TryAccept(Action(188, "Sacred Soil", 10)));
        Assert.True(dedup.TryAccept(Action(189, "Lustrate", 10)));

        var otherCaster = Action(188, "Sacred Soil", 10) with { SourceName = "H2" };
        Assert.True(dedup.TryAccept(otherCaster));
    }

    /// <summary>
    /// The cast-bar and cooldown methods can both see one GCD, so the key must
    /// not include the method or the same press would land twice.
    /// </summary>
    [Fact]
    public void TheSamePressSeenByTwoMethodsCollapsesToOne()
    {
        var dedup = new HealerActionDeduplicator();
        var viaCast = Action(185, "Adloquium", 10, method: ObservationMethod.CastBar);
        var viaCooldown = Action(185, "Adloquium", 10.1, method: ObservationMethod.CooldownTransition);

        Assert.True(dedup.TryAccept(viaCast));
        Assert.False(dedup.TryAccept(viaCooldown));
    }

    /// <summary>Manual injection is deliberately not suppressed by a game sighting.</summary>
    [Fact]
    public void ManualInjectionHasItsOwnKeyspace()
    {
        var dedup = new HealerActionDeduplicator();
        Assert.True(dedup.TryAccept(Action(188, "Sacred Soil", 10)));
        Assert.True(dedup.TryAccept(
            Action(188, "Sacred Soil", 10, method: ObservationMethod.ManualInjection)));
    }

    [Fact]
    public void ResetAllowsTheSameActionAgain()
    {
        var dedup = new HealerActionDeduplicator();
        Assert.True(dedup.TryAccept(Action(188, "Sacred Soil", 10)));

        dedup.Reset();

        Assert.True(dedup.TryAccept(Action(188, "Sacred Soil", 10)));
    }

    /// <summary>A scrub backwards must not wedge the filter.</summary>
    [Fact]
    public void TimeMovingBackwardsIsAccepted()
    {
        var dedup = new HealerActionDeduplicator();
        Assert.True(dedup.TryAccept(Action(188, "Sacred Soil", 50)));
        Assert.True(dedup.TryAccept(Action(188, "Sacred Soil", 10)));
    }

    // ---------------- database ----------------

    [Fact]
    public void ActionIdsAreUniqueAndEveryEntryIsUsable()
    {
        var all = HealerActionDatabase.All;
        Assert.Equal(all.Count, all.Select(d => d.ActionId).Distinct().Count());
        Assert.All(all, d =>
        {
            Assert.NotEqual(0u, d.ActionId);
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.NotEqual(HealerActionKind.Unknown, d.Kind);
            Assert.InRange(d.MitigationFraction, 0f, 0.95f);
        });
    }

    [Fact]
    public void EveryHealerJobHasAKitAndBothProfilesAreCovered()
    {
        foreach (var job in new[]
        {
            SimulatedJob.WhiteMage, SimulatedJob.Scholar,
            SimulatedJob.Astrologian, SimulatedJob.Sage,
        })
        {
            var kit = HealerActionDatabase.ForJob(job).ToList();
            Assert.NotEmpty(kit);
            Assert.Contains(kit, d => d.Kind == HealerActionKind.Raise);
            Assert.Contains(kit, d => d.Kind == HealerActionKind.PartyMitigation);
        }

        Assert.Equal(HealerProfile.Pure, SimulatedJob.WhiteMage.Profile());
        Assert.Equal(HealerProfile.Pure, SimulatedJob.Astrologian.Profile());
        Assert.Equal(HealerProfile.Barrier, SimulatedJob.Scholar.Profile());
        Assert.Equal(HealerProfile.Barrier, SimulatedJob.Sage.Profile());
    }

    [Fact]
    public void CooldownWatchListCoversInstantsOnly()
    {
        var watch = HealerActionDatabase.CooldownWatchList;
        Assert.NotEmpty(watch);
        Assert.All(watch, id =>
        {
            var definition = HealerActionDatabase.Find(id);
            Assert.NotNull(definition);
            Assert.False(definition!.IsCast);
        });
    }

    /// <summary>Every action the mitigation plan names should be recognisable.</summary>
    [Theory]
    [InlineData("Sacred Soil")]
    [InlineData("Kerachole")]
    [InlineData("Holos")]
    [InlineData("Panhaima")]
    [InlineData("Temperance")]
    [InlineData("Neutral Sect")]
    [InlineData("Collective Unconscious")]
    [InlineData("Plenary Indulgence")]
    [InlineData("Expedient")]
    [InlineData("Fey Illumination")]
    [InlineData("Sun Sign")]
    [InlineData("Divine Caress")]
    [InlineData("Liturgy of the Bell")]
    [InlineData("Macrocosmos")]
    [InlineData("Philosophia")]
    [InlineData("Reprisal")]
    [InlineData("Feint")]
    [InlineData("Addle")]
    public void MitigationPlanActionsExistInTheDatabase(string name) =>
        Assert.Contains(HealerActionDatabase.All, d =>
            string.Equals(d.Name, name, StringComparison.Ordinal));

    // ---------------- applier ----------------

    [Fact]
    public void UnknownActionIsRecordedButChangesNothing()
    {
        var party = NewParty();
        var result = new HealerActionApplier().Apply(party, Action(999_999, "Mystery", 5), 5);

        Assert.False(result.Recognised);
        Assert.Null(result.Definition);
        Assert.All(party.Members, m => Assert.Equal(Hp, m.CurrentHp));
    }

    [Fact]
    public void PartyMitigationAppliesToEverySlot()
    {
        var party = NewParty();
        _ = new HealerActionApplier().Apply(party, Action(188, "Sacred Soil", 5), 5);

        Assert.All(party.Members, m => Assert.True(m.MitigationFractionAt(5) > 0));
    }

    [Fact]
    public void PartyShieldAppliesToEverySlotAsAFractionOfMaximumHp()
    {
        var party = NewParty();
        _ = new HealerActionApplier().Apply(party, Action(24311, "Panhaima", 5), 5);

        // 10% of a 100,000 pool.
        Assert.All(party.Members, m => Assert.Equal(10_000, m.TotalShieldAt(5)));
    }

    [Fact]
    public void SingleTargetHealWithoutAResolvedTargetPicksTheLowestMember()
    {
        var party = NewParty();
        _ = party.ApplyRawDamage("Chip", 60_000, DamageTargetRule.Slot, 0, PartyRole.M2);

        var result = new HealerActionApplier().Apply(party, Action(189, "Lustrate", 5), 5);

        Assert.True(result.Recognised);
        var heal = Assert.Single(result.Heals);
        Assert.Equal(PartyRole.M2, heal.Slot);
    }

    [Fact]
    public void SingleTargetHealHonoursAResolvedTarget()
    {
        var party = NewParty();
        _ = party.ApplyRawDamage("Chip", 60_000, DamageTargetRule.Slot, 0, PartyRole.M2);
        _ = party.ApplyRawDamage("Chip", 30_000, DamageTargetRule.Slot, 0, PartyRole.T1);

        var result = new HealerActionApplier()
            .Apply(party, Action(189, "Lustrate", 5, PartyRole.T1), 5);

        var heal = Assert.Single(result.Heals);
        Assert.Equal(PartyRole.T1, heal.Slot);
    }

    [Fact]
    public void BenedictionFullyHealsItsTarget()
    {
        var party = NewParty();
        _ = party.ApplyRawDamage("Chip", 90_000, DamageTargetRule.Slot, 0, PartyRole.T1);

        _ = new HealerActionApplier().Apply(party, Action(140, "Benediction", 5, PartyRole.T1), 5);

        Assert.Equal(Hp, party[PartyRole.T1].CurrentHp);
    }

    [Fact]
    public void RaiseBringsBackADeadSlot()
    {
        var party = NewParty();
        _ = party.ApplyRawDamage("Kill", Hp, DamageTargetRule.Slot, 0, PartyRole.M1);

        _ = new HealerActionApplier().Apply(party, Action(125, "Raise", 5, PartyRole.M1), 5);

        Assert.True(party[PartyRole.M1].IsAlive);
    }

    /// <summary>
    /// The whole point of the feature: shields and mitigation pressed before a
    /// raidwide must measurably change the outcome.
    /// </summary>
    [Fact]
    public void MitigatingBeforeARaidwideChangesTheOutcome()
    {
        static int RunPull(bool mitigate)
        {
            var party = NewParty();
            if (mitigate)
            {
                var applier = new HealerActionApplier();
                _ = applier.Apply(party, Action(188, "Sacred Soil", 1), 1);
                _ = applier.Apply(party, Action(24311, "Panhaima", 1), 1);
            }

            _ = party.ApplyRawDamage("Raidwide", 40_000, DamageTargetRule.Party, 2);
            return party[PartyRole.M1].CurrentHp;
        }

        var unmitigated = RunPull(false);
        var mitigated = RunPull(true);

        Assert.Equal(Hp - 40_000, unmitigated);
        // 10% mitigation then a 10,000 shield: 40,000 -> 36,000 -> 26,000 to HP.
        Assert.Equal(Hp - 26_000, mitigated);
        Assert.True(mitigated > unmitigated);
    }

    [Fact]
    public void RegenActionsTickThroughTheParty()
    {
        var party = NewParty();
        _ = party.ApplyRawDamage("Chip", 50_000, DamageTargetRule.Party, 0);

        _ = new HealerActionApplier().Apply(party, Action(37010, "Medica III", 0), 0);
        party.Advance(3);

        Assert.All(party.Members, m => Assert.True(m.CurrentHp > Hp - 50_000));
    }
}
