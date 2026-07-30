using System.Numerics;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Core.Tests;

public sealed class MechanicSemanticsTests
{
    [Theory]
    [InlineData(AssignmentKind.Water, "Compressed Water")]
    [InlineData(AssignmentKind.Lightning, "Forked Lightning")]
    public void PlayerOwnedStackMarkerUsesRealPlayerPosition(
        AssignmentKind markerKind,
        string markerLabel)
    {
        const long seed = 8675309;
        var generated = KefkaP4Assignments.Generate(seed);
        var assignment = generated.ShortWaterInGrandCrossOne
            ? generated.GrandCrossOne
            : generated.GrandCrossTwo;
        var playerRole = assignment.Get(markerKind, true);
        var encounter = new KefkaP4Encounter(seed, playerRole);
        var actualPosition = new Vector2(13.25f, -11.5f);
        var player = new PlayerState(true, actualPosition, new Vector2(0, -1), 0);

        _ = encounter.ProcessEvent(
            Event(60.1, TimelineEventKind.MoveShortDebuffs),
            PlayerState.Unavailable,
            1,
            evaluate: false);
        _ = encounter.ProcessEvent(
            Event(63.8, TimelineEventKind.ResolveShortDebuffs),
            player,
            1,
            evaluate: false);

        var ownedMarker = encounter.AllShapes.Single(shape =>
            shape.Label.StartsWith(markerLabel, StringComparison.Ordinal)
            && shape.Origin == actualPosition);
        Assert.Equal(ShapeKind.Circle, ownedMarker.Kind);
        Assert.Equal(KefkaP4Constants.WaterRadius, ownedMarker.Radius);
    }

    [Fact]
    public void AccelerationMovementThresholdUsesStrictSourceBoundary()
    {
        var thresholdSpeed = MathF.Sqrt(0.1f);
        Assert.Equal(0.1f, thresholdSpeed * thresholdSpeed);
        Assert.False(new PlayerState(
            true,
            Vector2.Zero,
            new Vector2(0, -1),
            thresholdSpeed).IsMoving);
        Assert.False(new PlayerState(
            true,
            Vector2.Zero,
            new Vector2(0, -1),
            MathF.BitDecrement(thresholdSpeed)).IsMoving);
        Assert.True(new PlayerState(
            true,
            Vector2.Zero,
            new Vector2(0, -1),
            MathF.BitIncrement(thresholdSpeed)).IsMoving);
    }

    [Fact]
    public void AccelerationResolutionPassesAtBoundaryAndUsesStrictSides()
    {
        var thresholdSpeed = MathF.Sqrt(0.1f);
        var belowThreshold = MathF.BitDecrement(thresholdSpeed);
        var aboveThreshold = MathF.BitIncrement(thresholdSpeed);
        var fake = FindShortAccelerationCase(fake: true);
        var real = FindShortAccelerationCase(fake: false);

        Assert.True(ResolveShortAcceleration(fake, thresholdSpeed).Passed);
        Assert.False(ResolveShortAcceleration(fake, belowThreshold).Passed);
        Assert.True(ResolveShortAcceleration(fake, aboveThreshold).Passed);

        Assert.True(ResolveShortAcceleration(real, thresholdSpeed).Passed);
        Assert.True(ResolveShortAcceleration(real, belowThreshold).Passed);
        Assert.False(ResolveShortAcceleration(real, aboveThreshold).Passed);
    }

    private static (long Seed, PartyRole PlayerRole) FindShortAccelerationCase(bool fake)
    {
        for (long seed = 0; seed < 10_000; seed++)
        {
            var generated = KefkaP4Assignments.Generate(seed);
            foreach (var role in PartyRoles.All)
            {
                var assignment = generated.GrandCrossOne.Find(role)
                    == AssignmentKind.ShortAcceleration
                        ? generated.GrandCrossOne
                        : generated.GrandCrossTwo.Find(role)
                            == AssignmentKind.ShortAcceleration
                            ? generated.GrandCrossTwo
                            : null;
                if (assignment?.Fake == fake)
                {
                    return (seed, role);
                }
            }
        }

        throw new InvalidOperationException($"No {(fake ? "fake" : "real")} short acceleration case found.");
    }

    private static SimulationResult ResolveShortAcceleration(
        (long Seed, PartyRole PlayerRole) testCase,
        float speed)
    {
        var encounter = new KefkaP4Encounter(testCase.Seed, testCase.PlayerRole);
        _ = encounter.ProcessEvent(
            Event(60.1, TimelineEventKind.MoveShortDebuffs),
            PlayerState.Unavailable,
            1,
            evaluate: false);
        var player = new PlayerState(
            true,
            encounter.RequiredPosition!.Value,
            new Vector2(0, -1),
            speed);

        return encounter.ProcessEvent(
            Event(63.8, TimelineEventKind.ResolveShortDebuffs),
            player,
            1,
            evaluate: true)!;
    }

    private static TimelineEvent Event(double time, TimelineEventKind kind) =>
        new(time, 0, kind.ToString(), kind);
}
