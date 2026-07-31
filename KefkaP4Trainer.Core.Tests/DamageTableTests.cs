using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;
using KefkaP4Trainer.Core.Health;

namespace KefkaP4Trainer.Core.Tests;

public sealed class DamageTableTests
{
    [Fact]
    public void EveryDamageEventLandsOnAMitigationPlanAnchor()
    {
        var anchors = KefkaP4Mitigation.All.Select(call => call.Time).ToHashSet();

        foreach (var damageEvent in KefkaP4DamageTable.Default)
        {
            Assert.True(
                anchors.Contains(damageEvent.Time),
                $"{damageEvent.AbilityName} at {damageEvent.Time} has no mitigation plan anchor.");
        }
    }

    [Fact]
    public void DamageEventsAreOrderedAndInsideTheEncounter()
    {
        var events = KefkaP4DamageTable.Default;
        for (var index = 1; index < events.Count; index++)
        {
            Assert.True(events[index - 1].Time <= events[index].Time);
        }

        Assert.All(events, e => Assert.InRange(e.Time, 0, KefkaP4Constants.EncounterLength));
    }

    [Fact]
    public void EventIdsAreUniqueAndCarryASourceNote()
    {
        var events = KefkaP4DamageTable.Default;
        Assert.Equal(events.Count, events.Select(e => e.Id).Distinct().Count());
        Assert.All(events, e => Assert.False(string.IsNullOrWhiteSpace(e.SourceNote)));
    }

    /// <summary>The nine supplied FF Logs figures must all survive into the table verbatim.</summary>
    [Fact]
    public void SuppliedReferenceFiguresArePreservedExactly()
    {
        var events = KefkaP4DamageTable.Default;
        int Reference(string id) => events.Single(e => e.Id == id).ReferenceUnmitigated;

        Assert.Equal(298_000, Reference("grand-cross-1"));
        Assert.Equal(234_000, Reference("inferno-1"));
        Assert.Equal(283_000, Reference("grand-cross-2"));
        Assert.Equal(226_000, Reference("tsunami-2"));
        Assert.Equal(292_000, Reference("grand-cross-3"));
        Assert.Equal(741_000, Reference("white-antilight"));
        Assert.Equal(231_000, Reference("death-bolt-1"));
        Assert.Equal(215_000, Reference("ultima-upsurge-1"));
        Assert.Equal(167_000, Reference("death-bolt-2"));
    }

    /// <summary>Inferno and Tsunami follow the pull's own ordering flag.</summary>
    [Theory]
    [InlineData(true, "Inferno", "Tsunami")]
    [InlineData(false, "Tsunami", "Inferno")]
    public void ChaosOrderFollowsTheAssignment(bool infernoFirst, string first, string second)
    {
        var events = KefkaP4DamageTable.For(infernoFirst);
        Assert.Equal(first, events.Single(e => e.Time == 18.4).AbilityName);
        Assert.Equal(second, events.Single(e => e.Time == 34.4).AbilityName);
    }

    /// <summary>
    /// A party-total figure divides across the party; a per-target one does not.
    /// This is the single knob that would need flipping if the source report
    /// turns out to be per-target.
    /// </summary>
    [Fact]
    public void PartyTotalScopeDividesAcrossTheEightTargets()
    {
        var raidwide = KefkaP4DamageTable.Default.Single(e => e.Id == "grand-cross-1");
        Assert.Equal(ReferenceScope.PartyTotal, raidwide.Scope);
        Assert.Equal(298_000 / 8, raidwide.RawPerTarget(8));

        var perTarget = raidwide with { Scope = ReferenceScope.PerTarget };
        Assert.Equal(298_000, perTarget.RawPerTarget(8));
    }

    [Fact]
    public void MultiHitEventsSplitTheReferenceFigureAcrossHits()
    {
        var twoHit = new DamageEvent
        {
            Id = "test",
            Time = 0,
            AbilityName = "Two Hit",
            Target = DamageTargetRule.Party,
            ReferenceUnmitigated = 160_000,
            Scope = ReferenceScope.PartyTotal,
            HitCount = 2,
            SourceNote = "test",
        };

        Assert.Equal(10_000, twoHit.RawPerTarget(8));

        var party = new VirtualParty(100_000) { Scaling = new DamageScaling(100_000, 100_000) };
        var results = party.ApplyDamage(twoHit, 0);
        Assert.Equal(16, results.Count);
        Assert.All(party.Members, m => Assert.Equal(80_000, m.CurrentHp));
    }

    /// <summary>
    /// With the recorded scope, an unmitigated raidwide has to be survivable at
    /// the reference HP pool. If this fails, the scope reading is wrong.
    /// </summary>
    [Fact]
    public void UnmitigatedRaidwidesAreSurvivableAtTheReferencePool()
    {
        var party = new VirtualParty(DamageScaling.DefaultReferenceMaximumHp);

        foreach (var damageEvent in KefkaP4DamageTable.Default.Where(e => !e.LethalByDesign))
        {
            var fresh = new VirtualParty(DamageScaling.DefaultReferenceMaximumHp);
            var results = fresh.ApplyDamage(damageEvent, damageEvent.Time);
            Assert.All(results, r => Assert.False(
                r.Killed,
                $"{damageEvent.AbilityName} killed a full-HP player unmitigated "
                + $"({r.Breakdown}); the reference scope is probably wrong."));
        }

        // ...and the whole sequence unmitigated should still be a wipe, or the
        // numbers would be too soft to practise against.
        foreach (var damageEvent in KefkaP4DamageTable.Default.Where(e => !e.LethalByDesign))
        {
            _ = party.ApplyDamage(damageEvent, damageEvent.Time);
        }

        Assert.True(party.IsWiped);
    }

    /// <summary>
    /// White Antilight is the heaviest raidwide in the phase but is survivable
    /// from full: the source log is a clear, so the figure is what the party
    /// took on its CORRECT side. Standing in the wrong Antilight is graded by
    /// the mechanic simulation, not by this table.
    /// </summary>
    [Fact]
    public void WhiteAntilightIsTheHeaviestRaidwideAndIsSurvivable()
    {
        var events = KefkaP4DamageTable.Default;
        var antilight = events.Single(e => e.Id == "white-antilight");

        Assert.False(antilight.LethalByDesign);
        Assert.Equal(
            events.Max(e => e.ReferenceUnmitigated),
            antilight.ReferenceUnmitigated);

        var party = new VirtualParty(DamageScaling.DefaultReferenceMaximumHp);
        var results = party.ApplyDamage(antilight, antilight.Time);
        Assert.All(results, r => Assert.False(r.Killed));

        // ...but it should hurt: over half the pool unmitigated.
        Assert.All(results, r => Assert.True(r.HpLost > DamageScaling.DefaultReferenceMaximumHp / 2));
    }

    [Fact]
    public void NoEventIsCurrentlyFlaggedLethalByDesign()
    {
        Assert.All(KefkaP4DamageTable.Default, e => Assert.False(e.LethalByDesign));
    }
}
