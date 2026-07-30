using System.Numerics;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Core.Tests;

/// <summary>
/// Ghost lifetime and Cursed Shriek gaze rules, checked against p4_seq.gd
/// (<c>check_if_facing</c>, <c>shriek_1_hit</c>, <c>shriek_2_hit</c>) and the
/// p4_main.tscn method tracks.
/// </summary>
public sealed class GhostAndGazeTests
{
    /// <summary>Shriek 1 carriers are placed at 68.8s and graded at 72.8s.</summary>
    private const double ShriekOneMove = 68.8;
    private const double ShriekOneResolve = 72.8;

    /// <summary>Shriek 2 carriers are placed at 91.4s and graded at 96.7s.</summary>
    private const double ShriekTwoMove = 91.4;
    private const double ShriekTwoResolve = 96.7;

    [Fact]
    public void GhostsSpawnWithTheMoveEventAndDespawnAtResolution()
    {
        var engine = Advance(seed: 4242, until: ShriekOneMove);
        var assignment = engine.Encounter.Assignments.GrandCrossOne;

        // The local player is a real character, so a carrier that happens to be
        // the player's own role never gets a stand-in.
        var expected = new[] { assignment.ShriekDps, assignment.ShriekSupport }
            .Count(role => role != engine.PlayerRole);

        var beforeMove = engine.Encounter.ActiveGhosts(ShriekOneMove - 0.1);
        var duringWindow = engine.Encounter.ActiveGhosts(ShriekOneMove + 1);
        var atResolution = engine.Encounter.ActiveGhosts(ShriekOneResolve);

        Assert.Empty(beforeMove);
        Assert.Equal(expected, duringWindow.Count);
        Assert.Empty(atResolution);
        Assert.All(duringWindow, ghost => Assert.True(ghost.IsGazeSource));
        Assert.All(
            duringWindow,
            ghost => Assert.Equal(GhostVisualState.GazeSource, ghost.VisualState));
        Assert.All(
            duringWindow,
            ghost => Assert.NotEqual(engine.PlayerRole.Key(), ghost.Id));
    }

    [Fact]
    public void BothCarriersGetGhostsWhenThePlayerCarriesNeither()
    {
        // R2 carries no Shriek on this seed, so both stand-ins are present.
        var engine = new SimulationEngine(4242, PartyRole.R2);
        engine.Start(0);
        _ = engine.Update(ShriekOneMove, PlayerState.Unavailable);

        var assignment = engine.Encounter.Assignments.GrandCrossOne;
        Assert.NotEqual(PartyRole.R2, assignment.ShriekDps);
        Assert.NotEqual(PartyRole.R2, assignment.ShriekSupport);
        Assert.Equal(2, engine.Encounter.ActiveGhosts(ShriekOneMove + 1).Count);
    }

    [Fact]
    public void GhostPositionsMatchTheSourceShriekPositions()
    {
        var engine = Advance(seed: 99, until: ShriekOneMove);
        var pattern = engine.Encounter.Assignments.MagicPatterns[3];
        var westSafe = pattern.WestLines != pattern.ThunderFake;
        var assignment = engine.Encounter.Assignments.GrandCrossOne;

        var expectedSupport =
            KefkaP4Positions.ShriekOne(true, westSafe, pattern.RotationDegrees);
        var expectedDps =
            KefkaP4Positions.ShriekOne(false, westSafe, pattern.RotationDegrees);

        var ghosts = engine.Encounter.ActiveGhosts(ShriekOneMove + 0.5);
        var support = ghosts.Single(g => g.Id == assignment.ShriekSupport.Key());
        var dps = ghosts.Single(g => g.Id == assignment.ShriekDps.Key());

        Assert.Equal(expectedSupport.X, support.ArenaPosition.X, 4);
        Assert.Equal(expectedSupport.Y, support.ArenaPosition.Y, 4);
        Assert.Equal(expectedDps.X, dps.ArenaPosition.X, 4);
        Assert.Equal(expectedDps.Y, dps.ArenaPosition.Y, 4);
    }

    [Fact]
    public void ShriekTwoGhostsUseTheStaticNorthSouthPositions()
    {
        var engine = Advance(seed: 7, until: ShriekTwoMove);
        var assignment = engine.Encounter.Assignments.GrandCrossTwo;
        var ghosts = engine.Encounter.ActiveGhosts(ShriekTwoMove + 0.5);

        // move_shriek_2_dodge places support at (0, 3) and dps at (0, -3),
        // unrotated.
        Assert.NotEmpty(ghosts);
        foreach (var ghost in ghosts)
        {
            var expectedY = ghost.Id == assignment.ShriekSupport.Key() ? 3f : -3f;
            Assert.Equal(0, ghost.ArenaPosition.X, 4);
            Assert.Equal(expectedY, ghost.ArenaPosition.Y, 4);
            Assert.Equal(ShriekTwoMove, ghost.SpawnTime);
            Assert.Equal(ShriekTwoResolve, ghost.DespawnTime);
        }
    }

    [Fact]
    public void GhostFadesInAfterSpawnAndOutBeforeDespawn()
    {
        var ghost = new SimulatedGhost(
            "h1",
            new Vector2(0, -3),
            0,
            SpawnTime: 10,
            DespawnTime: 20,
            IsGazeSource: true,
            GhostVisualState.GazeSource);

        Assert.Equal(0, ghost.AlphaAt(9.9, 1));
        Assert.Equal(0, ghost.AlphaAt(10, 1), 4);
        Assert.Equal(0.5f, ghost.AlphaAt(10.5, 1), 4);
        Assert.Equal(1, ghost.AlphaAt(15, 1), 4);
        Assert.Equal(0.5f, ghost.AlphaAt(19.5, 1), 4);
        Assert.Equal(0, ghost.AlphaAt(20, 1));

        // A zero fade duration must not divide by zero.
        Assert.Equal(1, ghost.AlphaAt(15, 0), 4);
    }

    [Fact]
    public void LookAwayGazePassesOnlyWhenEverySourceIsOutsideTheCone()
    {
        var player = Vector2.Zero;
        var north = new Vector2(0, -10);
        var south = new Vector2(0, 10);

        // Real Shriek: looking at either carrier fails.
        var facingNorth = new Vector2(0, -1);
        var toNorth = KefkaP4Gaze.Evaluate("a", player, facingNorth, north, fake: false);
        var toSouth = KefkaP4Gaze.Evaluate("b", player, facingNorth, south, fake: false);

        Assert.True(toNorth.LookedToward);
        Assert.False(toNorth.Passed);
        Assert.False(toSouth.LookedToward);
        Assert.True(toSouth.Passed);

        Assert.Equal(
            KefkaP4Gaze.LookedAtReason,
            KefkaP4Gaze.Combine([toNorth, toSouth], fake: false));

        // Facing east is away from both.
        var facingEast = new Vector2(1, 0);
        var eastToNorth = KefkaP4Gaze.Evaluate("a", player, facingEast, north, fake: false);
        var eastToSouth = KefkaP4Gaze.Evaluate("b", player, facingEast, south, fake: false);
        Assert.Null(KefkaP4Gaze.Combine([eastToNorth, eastToSouth], fake: false));
    }

    [Fact]
    public void FakeGazeRequiresFacingEverySourceSimultaneously()
    {
        var player = Vector2.Zero;
        var north = new Vector2(0, -10);
        var south = new Vector2(0, 10);
        var facingNorth = new Vector2(0, -1);

        var toNorth = KefkaP4Gaze.Evaluate("a", player, facingNorth, north, fake: true);
        var toSouth = KefkaP4Gaze.Evaluate("b", player, facingNorth, south, fake: true);

        Assert.True(toNorth.Passed);
        Assert.False(toSouth.Passed);

        // Source semantics: fake fails when the player fails to face EITHER
        // carrier, so opposed carriers can never both be satisfied.
        Assert.Equal(
            KefkaP4Gaze.DidNotLookReason,
            KefkaP4Gaze.Combine([toNorth, toSouth], fake: true));

        // Two carriers inside one cone do pass.
        var nearA = new Vector2(-1, -10);
        var nearB = new Vector2(1, -10);
        var a = KefkaP4Gaze.Evaluate("a", player, facingNorth, nearA, fake: true);
        var b = KefkaP4Gaze.Evaluate("b", player, facingNorth, nearB, fake: true);
        Assert.Null(KefkaP4Gaze.Combine([a, b], fake: true));
    }

    [Theory]
    [InlineData(44.0f, true)]
    [InlineData(44.99f, true)]
    [InlineData(45.0f, false)]
    [InlineData(45.1f, false)]
    public void GazeThresholdIsAStrictFortyFiveDegreeHalfCone(
        float offsetDegrees,
        bool expectedLookedToward)
    {
        var player = Vector2.Zero;
        var target = new Vector2(0, -10);
        var facing = Geometry.RotateDegrees(new Vector2(0, -1), offsetDegrees);

        var diagnostic = KefkaP4Gaze.Evaluate("a", player, facing, target, fake: false);

        Assert.Equal(expectedLookedToward, diagnostic.LookedToward);
        Assert.Equal(KefkaP4Gaze.ThresholdDegrees, diagnostic.ThresholdDegrees);
        Assert.Equal(offsetDegrees, diagnostic.AngleDegrees, 2);
    }

    [Fact]
    public void DiagnosticsCaptureNormalizedVectorsAndClampedDot()
    {
        var diagnostic = KefkaP4Gaze.Evaluate(
            "m2",
            new Vector2(5, 5),
            new Vector2(0, -3),
            new Vector2(5, -5),
            fake: false);

        Assert.Equal("m2", diagnostic.GhostId);
        Assert.Equal(1, diagnostic.PlayerFacing.Length(), 4);
        Assert.Equal(1, diagnostic.DirectionToGhost.Length(), 4);
        Assert.InRange(diagnostic.DotProduct, -1f, 1f);
        Assert.Equal(0, diagnostic.AngleDegrees, 3);
        Assert.True(diagnostic.LookedToward);
    }

    [Fact]
    public void GazeResolutionRecordsDiagnosticsAndFacingInHistory()
    {
        var engine = new SimulationEngine(2468, PartyRole.M1);
        engine.Start(0);

        // Stand at the centre facing north through the Shriek 1 resolution.
        var player = new PlayerState(true, Vector2.Zero, new Vector2(0, -1), 0);
        _ = engine.Update(ShriekOneResolve, player);

        var result = engine.History.Single(r => r.Mechanic == "Cursed Shriek 1");

        Assert.NotEmpty(result.GazeDiagnostics);
        Assert.Equal(new Vector2(0, -1), result.PlayerFacing);
        Assert.Equal(result.GazeDiagnostics, engine.Encounter.LastGazeDiagnostics);
        Assert.All(
            result.GazeDiagnostics,
            diagnostic => Assert.Equal(45f, diagnostic.ThresholdDegrees));

        // The recorded outcome and the per-source diagnostics must agree.
        Assert.Equal(result.Passed, result.GazeDiagnostics.All(d => d.Passed));
    }

    [Fact]
    public void ResetClearsGhostsAndDiagnostics()
    {
        // Diagnostics are only recorded when a valid player is graded.
        var engine = new SimulationEngine(31337, PartyRole.M1);
        engine.Start(0);
        _ = engine.Update(
            ShriekOneResolve,
            new PlayerState(true, Vector2.Zero, new Vector2(0, -1), 0));

        Assert.NotEmpty(engine.Encounter.LastGazeDiagnostics);

        engine.Reset();

        Assert.Empty(engine.Encounter.AllGhosts);
        Assert.Empty(engine.Encounter.LastGazeDiagnostics);
        Assert.Empty(engine.Encounter.ActiveGhosts(ShriekOneMove + 1));
    }

    [Fact]
    public void SameSeedReproducesIdenticalGhostPlacement()
    {
        var first = Advance(seed: 555, until: ShriekOneMove)
            .Encounter.ActiveGhosts(ShriekOneMove + 1);
        var second = Advance(seed: 555, until: ShriekOneMove)
            .Encounter.ActiveGhosts(ShriekOneMove + 1);

        Assert.Equal(first.Count, second.Count);
        for (var index = 0; index < first.Count; index++)
        {
            Assert.Equal(first[index].Id, second[index].Id);
            Assert.Equal(first[index].ArenaPosition, second[index].ArenaPosition);
            Assert.Equal(first[index].SpawnTime, second[index].SpawnTime);
            Assert.Equal(first[index].DespawnTime, second[index].DespawnTime);
        }
    }

    [Fact]
    public void ArenaFacingHelpersRoundTrip()
    {
        foreach (var degrees in new[] { 0f, 45f, 90f, 180f, 270f, 359f })
        {
            var radians = degrees * (MathF.PI / 180f);
            var direction = Geometry.DirectionFromAngle(radians);
            Assert.Equal(1, direction.Length(), 4);
            var recovered = Geometry.AngleFromDirection(direction);
            Assert.Equal(
                MathF.Cos(radians),
                MathF.Cos(recovered),
                4);
            Assert.Equal(
                MathF.Sin(radians),
                MathF.Sin(recovered),
                4);
        }

        // Angle 0 points toward simulator south (+Y).
        Assert.Equal(new Vector2(0, 1), Geometry.DirectionFromAngle(0));
    }

    private static SimulationEngine Advance(long seed, double until)
    {
        var engine = new SimulationEngine(seed, PartyRole.M1);
        engine.Start(0);
        _ = engine.Update(until, PlayerState.Unavailable);
        return engine;
    }
}
