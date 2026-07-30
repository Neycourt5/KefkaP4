using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Core.Tests;

public sealed class AssignmentTests
{
    [Fact]
    public void SameSeedProducesIdenticalAssignmentGraph()
    {
        var first = KefkaP4Assignments.Generate(8675309);
        var second = KefkaP4Assignments.Generate(8675309);

        Assert.Equal(first.Signature, second.Signature);
    }

    [Fact]
    public void DifferentSeedsCanProduceDifferentValidAssignments()
    {
        var signatures = Enumerable.Range(1, 32)
            .Select(seed => KefkaP4Assignments.Generate(seed).Signature)
            .Distinct()
            .ToArray();

        Assert.True(signatures.Length > 1);
        foreach (var seed in Enumerable.Range(1, 32))
        {
            AssertValid(KefkaP4Assignments.Generate(seed));
        }
    }

    [Fact]
    public void GrandCrossTwoPreservesSourceConstrainedRemap()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var assignment = KefkaP4Assignments.Generate(seed);
            foreach (var isDps in new[] { false, true })
            {
                var gcOneAcceleration = new HashSet<PartyRole>
                {
                    assignment.GrandCrossOne.Get(AssignmentKind.ShortAcceleration, isDps),
                    assignment.GrandCrossOne.Get(AssignmentKind.LongAcceleration, isDps),
                };
                var gcOneElements = new HashSet<PartyRole>
                {
                    assignment.GrandCrossOne.Get(AssignmentKind.Water, isDps),
                    assignment.GrandCrossOne.Get(AssignmentKind.Lightning, isDps),
                };
                var gcTwoAcceleration = new HashSet<PartyRole>
                {
                    assignment.GrandCrossTwo.Get(AssignmentKind.ShortAcceleration, isDps),
                    assignment.GrandCrossTwo.Get(AssignmentKind.LongAcceleration, isDps),
                };
                var gcTwoElements = new HashSet<PartyRole>
                {
                    assignment.GrandCrossTwo.Get(AssignmentKind.Water, isDps),
                    assignment.GrandCrossTwo.Get(AssignmentKind.Lightning, isDps),
                };

                Assert.True(gcOneElements.SetEquals(gcTwoAcceleration));
                Assert.True(gcOneAcceleration.SetEquals(gcTwoElements));
            }
        }
    }

    private static void AssertValid(KefkaP4Assignments assignment)
    {
        foreach (var grandCross in new[] { assignment.GrandCrossOne, assignment.GrandCrossTwo })
        {
            foreach (var kind in Enum.GetValues<AssignmentKind>())
            {
                Assert.True(grandCross.Get(kind, true).IsDps());
                Assert.False(grandCross.Get(kind, false).IsDps());
            }

            Assert.Equal(4, Enum.GetValues<AssignmentKind>()
                .Select(kind => grandCross.Get(kind, true))
                .Distinct()
                .Count());
            Assert.Equal(4, Enum.GetValues<AssignmentKind>()
                .Select(kind => grandCross.Get(kind, false))
                .Distinct()
                .Count());
        }

        Assert.Equal(4, assignment.FieldRoles.Count);
        Assert.Equal(4, assignment.DeathRoles.Count);
        Assert.Empty(assignment.FieldRoles.Intersect(assignment.DeathRoles));
        Assert.Equal(8, assignment.FieldRoles.Union(assignment.DeathRoles).Count());
        Assert.Empty(assignment.BlackWoundRoles.Intersect(assignment.WhiteWoundRoles));
        Assert.Equal(8, assignment.BlackWoundRoles.Union(assignment.WhiteWoundRoles).Count());
        Assert.Empty(assignment.BlackSafeRoles.Intersect(assignment.WhiteSafeRoles));
        Assert.Equal(8, assignment.BlackSafeRoles.Union(assignment.WhiteSafeRoles).Count());
        Assert.Empty(new[]
        {
            assignment.GrandCrossOne.ShriekDps,
            assignment.GrandCrossOne.ShriekSupport,
        }.Intersect(new[]
        {
            assignment.GrandCrossTwo.ShriekDps,
            assignment.GrandCrossTwo.ShriekSupport,
        }));
        Assert.True(assignment.GrandCrossOne.ShriekDps.IsDps());
        Assert.False(assignment.GrandCrossOne.ShriekSupport.IsDps());
        Assert.True(assignment.GrandCrossTwo.ShriekDps.IsDps());
        Assert.False(assignment.GrandCrossTwo.ShriekSupport.IsDps());
        var accelerationRoles = new HashSet<PartyRole>
        {
            assignment.GrandCrossOne.Get(AssignmentKind.ShortAcceleration, true),
            assignment.GrandCrossOne.Get(AssignmentKind.LongAcceleration, true),
            assignment.GrandCrossOne.Get(AssignmentKind.ShortAcceleration, false),
            assignment.GrandCrossOne.Get(AssignmentKind.LongAcceleration, false),
        };
        var shriekRoles = new HashSet<PartyRole>
        {
            assignment.GrandCrossOne.ShriekDps,
            assignment.GrandCrossOne.ShriekSupport,
            assignment.GrandCrossTwo.ShriekDps,
            assignment.GrandCrossTwo.ShriekSupport,
        };
        Assert.True(accelerationRoles.SetEquals(shriekRoles));
        foreach (var role in PartyRoles.All)
        {
            var expectedBlackSafe = assignment.BlackWoundRoles.Contains(role)
                ? assignment.DeathRoles.Contains(role) != assignment.FloodFake
                : assignment.DeathRoles.Contains(role) == assignment.FloodFake;
            Assert.Equal(expectedBlackSafe, assignment.BlackSafeRoles.Contains(role));
        }

        Assert.Contains(
            assignment.NeoRotationDegrees,
            new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f });
        Assert.Equal(4, assignment.MagicPatterns.Count);
        Assert.All(
            assignment.MagicPatterns,
            pattern => Assert.Contains(pattern.RotationDegrees, new[] { 45f, 135f, 225f, 315f }));
    }
}
