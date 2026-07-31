using System.Numerics;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Core.Tests;

/// <summary>
/// Parity fixtures for the mechanics that were previously traced but unpinned:
/// Chaos debuffs, Thrumming Thunder III, Entropy twisters, Blizzard Blowout III
/// and Mana Release.
///
/// Every expectation is transcribed from <c>p4_seq.gd</c>; where the source does
/// something surprising, the surprise is asserted rather than corrected.
/// </summary>
public sealed class RemainingMechanicParityTests
{
    private static TimelineEvent Event(double time, TimelineEventKind kind, int argument = 0) =>
        new(time, 0, kind.ToString(), kind, argument);

    private static KefkaP4Encounter Run(long seed, PartyRole role, params TimelineEvent[] events)
    {
        var encounter = new KefkaP4Encounter(seed, role);
        foreach (var timelineEvent in events)
        {
            _ = encounter.ProcessEvent(timelineEvent, PlayerState.Unavailable, 1, evaluate: false);
        }

        return encounter;
    }

    // ---------------- Chaos debuffs ----------------

    /// <summary>
    /// <c>chaos_debuffs</c>: Entropy when
    /// <c>(inferno_first and num == 1) or (!inferno_first and num == 2)</c>,
    /// otherwise Dynamic Fluid. Durations are
    /// <c>INFERNO_LONG 61 / INFERNO_SHORT 45</c> and
    /// <c>TSUNAMI_LONG 84 / TSUNAMI_SHORT 68</c>, chosen by call number.
    /// </summary>
    [Fact]
    public void ChaosAppliesTheRightDebuffToEveryoneWithTheSourceDurations()
    {
        for (long seed = 0; seed < 200; seed++)
        {
            var assignments = KefkaP4Assignments.Generate(seed);
            var encounter = Run(
                seed,
                PartyRole.M1,
                Event(18.4, TimelineEventKind.AssignChaos, 1),
                Event(34.4, TimelineEventKind.AssignChaos, 2));

            foreach (var (number, time) in new[] { (1, 18.4), (2, 34.4) })
            {
                var isInferno = (assignments.InfernoFirst && number == 1)
                    || (!assignments.InfernoFirst && number == 2);
                var expectedKind = isInferno ? DebuffKind.Entropy : DebuffKind.DynamicFluid;
                var expectedDuration = isInferno
                    ? (number == 1 ? 61d : 45d)
                    : (number == 1 ? 84d : 68d);

                var applied = encounter.AllDebuffs
                    .Where(debuff => debuff.Kind == expectedKind && debuff.AppliedAt == time)
                    .ToList();

                // Waju applies it to every party member, not a subset.
                Assert.Equal(PartyRoles.All.Length, applied.Count);
                Assert.All(applied, debuff =>
                    Assert.Equal(time + expectedDuration, debuff.ExpiresAt, 3));
            }
        }
    }

    // ---------------- Thrumming Thunder III ----------------

    /// <summary>
    /// <c>tt_hit</c> spawns two <b>lines only</b> — no cones — and flips them to
    /// the opposite side when the thunder element is fake.
    /// </summary>
    [Fact]
    public void ThrummingThunderResolvesLinesOnlyAndHonoursTheThunderFlip()
    {
        for (long seed = 0; seed < 120; seed++)
        {
            var pattern = KefkaP4Assignments.Generate(seed).MagicPatterns[3];
            var encounter = Run(seed, PartyRole.M1, Event(71.4, TimelineEventKind.ResolveThrummingThunder));

            var hazards = encounter.AllShapes
                .Where(shape => shape.Label.StartsWith("Thrumming Thunder", StringComparison.Ordinal)
                    && shape.Phase == ShapePhase.Dangerous)
                .ToList();

            Assert.Equal(2, hazards.Count);
            Assert.All(hazards, shape => Assert.Equal(ShapeKind.Rectangle, shape.Kind));
            Assert.DoesNotContain(hazards, shape => shape.Kind == ShapeKind.Cone);

            // Resolved side is the telegraphed side XOR the fake flag.
            var expectedWest = pattern.WestLines ^ pattern.ThunderFake;
            var expected = KefkaP4Mechanics.CreateMagicShapes(
                pattern with { WestLines = expectedWest, ThunderFake = false },
                resolved: true, includeLines: true, includeCones: false,
                0, 1, "Thrumming Thunder");

            Assert.Equal(
                expected.Select(shape => shape.Origin).OrderBy(o => o.X).ThenBy(o => o.Y),
                hazards.Select(shape => shape.Origin).OrderBy(o => o.X).ThenBy(o => o.Y));
        }
    }

    // ---------------- Blizzard Blowout III ----------------

    /// <summary>
    /// <c>bb_hit</c> spawns two <b>cones only</b>, using
    /// <c>Vector2.UP/DOWN</c> when north-south and <c>LEFT/RIGHT</c> otherwise,
    /// inverted when the ice element is fake.
    /// </summary>
    [Fact]
    public void BlizzardBlowoutResolvesConesOnlyAndHonoursTheIceFlip()
    {
        for (long seed = 0; seed < 120; seed++)
        {
            var pattern = KefkaP4Assignments.Generate(seed).MagicPatterns[3];
            var encounter = Run(seed, PartyRole.M1, Event(89.4, TimelineEventKind.ResolveBlizzardBlowout));

            var hazards = encounter.AllShapes
                .Where(shape => shape.Label.StartsWith("Blizzard Blowout", StringComparison.Ordinal)
                    && shape.Phase == ShapePhase.Dangerous)
                .ToList();

            Assert.Equal(2, hazards.Count);
            Assert.All(hazards, shape => Assert.Equal(ShapeKind.Cone, shape.Kind));
            Assert.All(hazards, shape => Assert.Equal(Vector2.Zero, shape.Origin));

            var northSouth = pattern.NorthSouthIce ^ pattern.IceFake;
            var expectedOne = Geometry.RotateDegrees(
                northSouth ? new Vector2(0, -1) : new Vector2(-1, 0), pattern.RotationDegrees);
            var expectedTwo = Geometry.RotateDegrees(
                northSouth ? new Vector2(0, 1) : new Vector2(1, 0), pattern.RotationDegrees);

            // The two cones are exactly opposed, whichever axis they use.
            Assert.Equal(2, hazards.Count);
            var directions = hazards.Select(shape => shape.Direction).ToList();
            Assert.Contains(directions, d => Vector2.Distance(d, Geometry.SafeNormalize(expectedOne)) < 0.001f);
            Assert.Contains(directions, d => Vector2.Distance(d, Geometry.SafeNormalize(expectedTwo)) < 0.001f);
        }
    }

    // ---------------- Entropy twisters ----------------

    /// <summary>
    /// <c>inferno_hit</c>: fake spawns a donut, real spawns a circle, one per
    /// snapshotted party position. <c>inferno_dodge</c> only moves the party
    /// when the mechanic is real.
    /// </summary>
    [Fact]
    public void EntropyTwistersUseDonutsWhenFakeAndCirclesWhenReal()
    {
        for (long seed = 0; seed < 120; seed++)
        {
            var fake = KefkaP4Assignments.Generate(seed).InfernoFake;
            var encounter = Run(
                seed,
                PartyRole.M1,
                Event(79.4, TimelineEventKind.SnapshotInferno),
                Event(83.5, TimelineEventKind.ResolveInferno));

            var twisters = encounter.AllShapes
                .Where(shape => shape.Label.StartsWith("Entropy", StringComparison.Ordinal))
                .ToList();

            Assert.Equal(PartyRoles.All.Length, twisters.Count);
            Assert.All(twisters, shape =>
            {
                Assert.Equal(fake ? ShapeKind.Donut : ShapeKind.Circle, shape.Kind);
                if (fake)
                {
                    Assert.Equal(KefkaP4Constants.TwisterInnerRadius, shape.InnerRadius);
                    Assert.Equal(KefkaP4Constants.TwisterOuterRadius, shape.Radius);
                }
                else
                {
                    Assert.Equal(KefkaP4Constants.TwisterInnerRadius, shape.Radius);
                }
            });
        }
    }

    [Fact]
    public void EntropyOnlyMovesThePartyWhenItIsReal()
    {
        for (long seed = 0; seed < 60; seed++)
        {
            var fake = KefkaP4Assignments.Generate(seed).InfernoFake;
            var before = Run(seed, PartyRole.M1, Event(76.9, TimelineEventKind.MoveCenter));
            var beforePositions = before.SimulatedPartyPositions.ToDictionary(p => p.Key, p => p.Value);

            var after = Run(
                seed,
                PartyRole.M1,
                Event(76.9, TimelineEventKind.MoveCenter),
                Event(81.4, TimelineEventKind.MoveInferno));

            var moved = after.SimulatedPartyPositions
                .Any(pair => Vector2.Distance(pair.Value, beforePositions[pair.Key]) > 0.001f);

            Assert.Equal(!fake, moved);
        }
    }

    // ---------------- Mana Release / Dynamic Fluid ----------------

    /// <summary>
    /// <c>mr_hit</c> re-flips lines only when <c>mr_thunder_fake != mm_thunder_fake</c>
    /// and cones only when <c>mr_ice_fake != mm_ice_fake</c>, because
    /// <c>tt_hit</c> and <c>bb_hit</c> deliberately wrote their flipped
    /// positions to locals so the telegraphed ones survive for Mana Release.
    /// </summary>
    [Fact]
    public void ManaReleaseFlipsRelativeToTheTelegraphNotTheEarlierResolution()
    {
        for (long seed = 0; seed < 120; seed++)
        {
            var assignments = KefkaP4Assignments.Generate(seed);
            var pattern = assignments.MagicPatterns[3];

            var expectedWest = pattern.WestLines
                ^ (pattern.ThunderFake != assignments.ManaReleaseThunderFake);
            var expectedNorthSouth = pattern.NorthSouthIce
                ^ (pattern.IceFake != assignments.ManaReleaseIceFake);

            var shapes = KefkaP4Mechanics.CreateManaReleaseShapes(
                pattern,
                assignments.ManaReleaseThunderFake,
                assignments.ManaReleaseIceFake,
                0,
                1);

            var reference = KefkaP4Mechanics.CreateMagicShapes(
                pattern with
                {
                    WestLines = expectedWest,
                    NorthSouthIce = expectedNorthSouth,
                    ThunderFake = false,
                    IceFake = false,
                },
                resolved: true, includeLines: true, includeCones: true, 0, 1, "Mana Release");

            Assert.Equal(4, shapes.Count);
            Assert.Equal(
                reference.Select(s => (s.Kind, s.Origin, s.Direction)),
                shapes.Select(s => (s.Kind, s.Origin, s.Direction)));
        }
    }

    /// <summary>
    /// A genuine source quirk, preserved rather than tidied: Entropy and Dynamic
    /// Fluid use <b>opposite</b> twister polarity. <c>inferno_hit</c> draws a
    /// donut when fake, but <c>tsunami_hit</c> draws a <i>circle</i> when fake.
    /// Waju even labels the Dynamic Fluid donut "(Fake)" although it is the
    /// branch that fires when the mechanic is real.
    /// </summary>
    [Fact]
    public void DynamicFluidTwisterPolarityIsInvertedRelativeToEntropy()
    {
        for (long seed = 0; seed < 120; seed++)
        {
            var tsunamiFake = KefkaP4Assignments.Generate(seed).TsunamiFake;
            var encounter = Run(
                seed,
                PartyRole.M1,
                Event(102.4, TimelineEventKind.SnapshotTsunami),
                Event(107.5, TimelineEventKind.ResolveManaRelease));

            var twisters = encounter.AllShapes
                .Where(shape => shape.Label.StartsWith("Dynamic Fluid", StringComparison.Ordinal))
                .ToList();

            Assert.Equal(PartyRoles.All.Length, twisters.Count);

            // Inverted against Entropy: fake is the CIRCLE here.
            Assert.All(twisters, shape =>
                Assert.Equal(tsunamiFake ? ShapeKind.Circle : ShapeKind.Donut, shape.Kind));

            // ...and the source's label is preserved verbatim, quirk included.
            Assert.All(twisters, shape => Assert.Equal(
                tsunamiFake
                    ? "Dynamic Fluid AoE (Twister)"
                    : "Dynamic Fluid Donut (Twister, Fake)",
                shape.Label));
        }
    }

    /// <summary>Mana Release resolves its own lines and cones plus the twisters.</summary>
    [Fact]
    public void ManaReleaseResolvesLinesConesAndTwistersTogether()
    {
        var encounter = Run(
            8675309,
            PartyRole.M1,
            Event(102.4, TimelineEventKind.SnapshotTsunami),
            Event(107.5, TimelineEventKind.ResolveManaRelease));

        var manaRelease = encounter.AllShapes
            .Where(shape => shape.Label.StartsWith("Mana Release", StringComparison.Ordinal)
                && shape.Phase == ShapePhase.Dangerous)
            .ToList();

        Assert.Equal(2, manaRelease.Count(shape => shape.Kind == ShapeKind.Rectangle));
        Assert.Equal(2, manaRelease.Count(shape => shape.Kind == ShapeKind.Cone));
        Assert.Equal(
            PartyRoles.All.Length,
            encounter.AllShapes.Count(shape =>
                shape.Label.StartsWith("Dynamic Fluid", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Twister geometry constants, straight from the source:
    /// <c>INFERNO_INNER 11.5</c>, <c>INFERNO_OUTTER 60.0</c>,
    /// <c>INFERNO_LIFETIME 0.8</c>. Both mechanics share them.
    /// </summary>
    [Fact]
    public void TwisterConstantsMatchTheSource()
    {
        Assert.Equal(11.5f, KefkaP4Constants.TwisterInnerRadius);
        Assert.Equal(60f, KefkaP4Constants.TwisterOuterRadius);
        Assert.Equal(0.8, KefkaP4Constants.TwisterLifetime);
    }
}
