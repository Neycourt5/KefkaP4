using System.Numerics;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Core.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void CircleIncludesBoundary()
    {
        Assert.True(Geometry.InCircle(new Vector2(5, 0), Vector2.Zero, 5));
        Assert.True(Geometry.InCircle(new Vector2(3, 4), Vector2.Zero, 5));
        Assert.False(Geometry.InCircle(new Vector2(5.01f, 0), Vector2.Zero, 5));
    }

    [Fact]
    public void DonutInnerBoundaryIsSafeAndOuterBoundaryIsDangerous()
    {
        Assert.False(Geometry.InDonut(new Vector2(5, 0), Vector2.Zero, 5, 10));
        Assert.True(Geometry.InDonut(new Vector2(5.01f, 0), Vector2.Zero, 5, 10));
        Assert.True(Geometry.InDonut(new Vector2(10, 0), Vector2.Zero, 5, 10));
        Assert.False(Geometry.InDonut(new Vector2(10.01f, 0), Vector2.Zero, 5, 10));
    }

    [Fact]
    public void ForwardRectangleDoesNotExtendBehindOrigin()
    {
        var north = new Vector2(0, -1);
        Assert.True(Geometry.InForwardRectangle(new Vector2(2, -50), Vector2.Zero, north, 4, 100));
        Assert.True(Geometry.InForwardRectangle(new Vector2(-2, -100), Vector2.Zero, north, 4, 100));
        Assert.False(Geometry.InForwardRectangle(new Vector2(0, 0.1f), Vector2.Zero, north, 4, 100));
        Assert.False(Geometry.InForwardRectangle(new Vector2(2.1f, -50), Vector2.Zero, north, 4, 100));
    }

    [Fact]
    public void ConeUsesAngleAndLengthBoundaries()
    {
        var north = new Vector2(0, -1);
        // The source PrismMesh uses tan(configuredHalfAngle) * length as
        // its full base width. A configured 90 degrees therefore produces
        // an effective half-angle of atan(0.5), about 26.565 degrees.
        var inside = Geometry.RotateDegrees(north * 9, 26);
        var outside = Geometry.RotateDegrees(north * 9, 27);

        Assert.True(Geometry.InCone(inside, Vector2.Zero, north, 90, 10));
        Assert.False(Geometry.InCone(outside, Vector2.Zero, north, 90, 10));
        Assert.True(Geometry.InCone(new Vector2(0, -10), Vector2.Zero, north, 90, 10));
        Assert.True(Geometry.InCone(new Vector2(5, -10), Vector2.Zero, north, 90, 10));
        Assert.False(Geometry.InCone(new Vector2(5.01f, -10), Vector2.Zero, north, 90, 10));
        Assert.False(Geometry.InCone(new Vector2(0, -10.01f), Vector2.Zero, north, 90, 10));
    }

    [Fact]
    public void ArenaBoundaryMatchesSourceFloorRadius()
    {
        Assert.True(KefkaP4Mechanics.IsInsideArena(new Vector2(47, 0)));
        Assert.False(KefkaP4Mechanics.IsInsideArena(new Vector2(47.01f, 0)));
    }

    [Fact]
    public void FacingUsesStrictFortyFiveDegreeWindow()
    {
        var forward = new Vector2(0, -1);
        Assert.True(Geometry.IsFacing(
            Vector2.Zero,
            forward,
            Geometry.RotateDegrees(forward, 44.999f)));
        Assert.False(Geometry.IsFacing(
            Vector2.Zero,
            forward,
            Geometry.RotateDegrees(forward, 45f)));
        Assert.False(Geometry.IsFacing(
            Vector2.Zero,
            forward,
            Geometry.RotateDegrees(forward, 45.001f)));
        Assert.True(Geometry.IsFacing(
            Vector2.Zero,
            forward,
            Geometry.RotateDegrees(forward, -44.999f)));
        Assert.False(Geometry.IsFacing(
            Vector2.Zero,
            forward,
            Geometry.RotateDegrees(forward, -45f)));
    }
}
