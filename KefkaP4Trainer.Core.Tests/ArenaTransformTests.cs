using System.Numerics;
using KefkaP4Trainer.Core;

namespace KefkaP4Trainer.Core.Tests;

public sealed class ArenaTransformTests
{
    [Fact]
    public void SourceDmuWaymarkFixtureConvertsExactly()
    {
        var transform = new ArenaTransform();
        transform.Set(new Vector3(100, 7, 100), 0);

        var world = transform.SimulatorToWorld(new Vector2(27.6f, -27.6f), 0);
        Assert.Equal(112f, world.X, 4);
        Assert.Equal(7f, world.Y, 4);
        Assert.Equal(88f, world.Z, 4);

        var simulator = transform.WorldToSimulator(world);
        Assert.Equal(27.6f, simulator.X, 4);
        Assert.Equal(-27.6f, simulator.Y, 4);
    }

    [Fact]
    public void PositiveNinetyDegreesRotatesNorthTowardEast()
    {
        var transform = new ArenaTransform();
        transform.Set(Vector3.Zero, MathF.PI / 2);

        var world = transform.SimulatorToWorld(new Vector2(0, -23), 0);
        Assert.Equal(10f, world.X, 4);
        Assert.Equal(0f, world.Z, 4);
    }

    [Fact]
    public void RotatedRoundTripPreservesPosition()
    {
        var transform = new ArenaTransform();
        transform.Set(new Vector3(-55.2f, 4.3f, 19.8f), 1.234f);
        var source = new Vector2(17.75f, -31.2f);

        var roundTrip = transform.WorldToSimulator(transform.SimulatorToWorld(source));
        Assert.Equal(source.X, roundTrip.X, 4);
        Assert.Equal(source.Y, roundTrip.Y, 4);
    }

    [Fact]
    public void FacingAnchorMapsGameSouthToSimulatorNorth()
    {
        var transform = new ArenaTransform();
        transform.Set(Vector3.Zero, ArenaTransform.RotationFromFfxivFacing(0));

        var worldNorth = transform.SimulatorToWorld(new Vector2(0, -2.3f), 0);
        Assert.Equal(0f, worldNorth.X, 4);
        Assert.Equal(1f, worldNorth.Z, 4);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1.5707963267948966f)]
    [InlineData(3.1415926535897932f)]
    [InlineData(-1.5707963267948966f)]
    public void SimulatorNorthAlignsWithFfxivForwardAtCardinalFacings(float gameRotation)
    {
        var transform = new ArenaTransform();
        transform.Set(Vector3.Zero, ArenaTransform.RotationFromFfxivFacing(gameRotation));

        var northWorld = transform.SimulatorToWorld(new Vector2(0, -ArenaTransform.DmuScale), 0);
        var actualDirection = Vector2.Normalize(new Vector2(northWorld.X, northWorld.Z));
        var expectedDirection = ArenaTransform.FfxivForward(gameRotation);
        Assert.True(Vector2.Distance(actualDirection, expectedDirection) < 0.00001f);

        var simulatorDirection = transform.WorldDirectionToSimulator(expectedDirection);
        Assert.True(Vector2.Distance(simulatorDirection, new Vector2(0, -1)) < 0.00001f);
    }

    [Fact]
    public void HugeFiniteRotationNormalizesInConstantTime()
    {
        var transform = new ArenaTransform();

        transform.Set(Vector3.Zero, float.MaxValue);

        Assert.True(float.IsFinite(transform.RotationRadians));
        Assert.InRange(transform.RotationRadians, -MathF.PI, MathF.PI);
    }

    [Fact]
    public void NonFiniteRotationIsRejected()
    {
        var transform = new ArenaTransform();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => transform.Set(Vector3.Zero, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => transform.Set(Vector3.Zero, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => transform.Set(Vector3.Zero, float.NegativeInfinity));
    }
}
