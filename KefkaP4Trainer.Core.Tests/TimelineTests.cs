using System.Numerics;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Core.Tests;

public sealed class TimelineTests
{
    [Fact]
    public void EventsCrossedByLargeDeltaTriggerExactlyOnce()
    {
        var engine = new SimulationEngine(123, PartyRole.M1);
        engine.Start(0);

        _ = engine.Update(80, PlayerState.Unavailable);
        var afterFirstAdvance = engine.Encounter.AllShapes.Count;
        _ = engine.Update(0, PlayerState.Unavailable);
        _ = engine.Update(double.NaN, PlayerState.Unavailable);

        Assert.True(afterFirstAdvance > 4);
        Assert.Equal(afterFirstAdvance, engine.Encounter.AllShapes.Count);
        Assert.Equal(81.4, engine.NextEvent?.Time);
    }

    [Fact]
    public void SameTimeEventsRetainStableOrder()
    {
        var atPull = KefkaP4Timeline.Events
            .Where(timelineEvent => Math.Abs(timelineEvent.Time - 76.9) < 0.001)
            .ToArray();

        Assert.Equal(2, atPull.Length);
        Assert.Equal(TimelineEventKind.CastUltima, atPull[0].Kind);
        Assert.Equal(TimelineEventKind.MoveCenter, atPull[1].Kind);
    }

    [Fact]
    public void PauseAndPlaybackSpeedControlClockWithoutDuplicatingEvents()
    {
        var engine = new SimulationEngine(123, PartyRole.M1);
        engine.Clock.PlaybackSpeed = 2;
        engine.Start(0);

        _ = engine.Update(1.7, PlayerState.Unavailable);
        Assert.Equal(3.4, engine.Clock.Time, 6);
        Assert.Equal(3.6, engine.NextEvent?.Time);
        var shapeCount = engine.Encounter.AllShapes.Count;

        engine.Pause();
        _ = engine.Update(5, PlayerState.Unavailable);
        Assert.Equal(3.4, engine.Clock.Time, 6);
        Assert.Equal(shapeCount, engine.Encounter.AllShapes.Count);

        engine.Resume();
        _ = engine.Update(0.1, PlayerState.Unavailable);
        Assert.Equal(3.6, engine.Clock.Time, 6);
        Assert.Equal(6.4, engine.NextEvent?.Time);
    }

    [Fact]
    public void ResetRetainsSeedAndReproducesPull()
    {
        var engine = new SimulationEngine(24680, PartyRole.H2);
        engine.Start(0);
        var firstSignature = engine.Encounter.Assignments.Signature;
        _ = engine.Update(10, PlayerState.Unavailable);
        engine.Reset();
        engine.Start(0);

        Assert.Equal(24680, engine.Seed);
        Assert.Equal(firstSignature, engine.Encounter.Assignments.Signature);
    }

    [Fact]
    public void PausedSeekReplaysWithoutAddingResults()
    {
        var engine = new SimulationEngine(77, PartyRole.R1);
        engine.Start(0);
        _ = engine.Update(20, PlayerState.Unavailable);
        engine.Pause();

        Assert.True(engine.Seek(107.5, PlayerState.Unavailable));
        Assert.Equal(107.5, engine.Clock.Time, 3);
        Assert.Empty(engine.History);
        Assert.NotEmpty(engine.Encounter.AllShapes);
        Assert.NotEmpty(engine.Encounter.AllDebuffs);
    }

    [Fact]
    public void SeekToEventDoesNotProcessItAgainOnResume()
    {
        var engine = new SimulationEngine(77, PartyRole.R1);
        engine.Start(0);
        engine.Pause();

        Assert.True(engine.Seek(8.0, PlayerState.Unavailable));
        var shapeCount = engine.Encounter.AllShapes.Count;
        Assert.Equal(8.7, engine.NextEvent?.Time);

        engine.Resume();
        _ = engine.Update(0, PlayerState.Unavailable);

        Assert.Equal(shapeCount, engine.Encounter.AllShapes.Count);
        Assert.Equal(8.7, engine.NextEvent?.Time);
    }

    [Fact]
    public void ResetClearsStateAndRewindsEventCursorWithoutClearingHistory()
    {
        var engine = new SimulationEngine(9, PartyRole.M1);
        var unsafePlayer = new PlayerState(true, new Vector2(60, 0), new Vector2(0, -1), 0);
        engine.Start(0);
        _ = engine.Update(8, unsafePlayer);
        Assert.Single(engine.History);

        engine.Reset();

        Assert.Equal(SimulationState.Stopped, engine.Clock.State);
        Assert.Equal(0, engine.Clock.Time);
        Assert.Equal(3.3, engine.NextEvent?.Time);
        Assert.Empty(engine.Encounter.AllShapes);
        Assert.Empty(engine.Encounter.AllDebuffs);
        Assert.Single(engine.History);
    }

    [Fact]
    public void FailureResultIsRecordedOnlyOnce()
    {
        var engine = new SimulationEngine(9, PartyRole.M1);
        var unsafePlayer = new PlayerState(true, new Vector2(60, 0), new Vector2(0, -1), 0);
        engine.Start(0);

        _ = engine.Update(8, unsafePlayer);
        var count = engine.History.Count;
        _ = engine.Update(0, unsafePlayer);

        Assert.Equal(1, count);
        Assert.Equal(count, engine.History.Count);
        Assert.False(engine.LastResult?.Passed);
        Assert.Equal("outside arena boundary", engine.LastResult?.Reason);
    }

    [Fact]
    public void AutomaticRestartCanConsumeAnExplicitNextSeed()
    {
        var engine = new SimulationEngine(9, PartyRole.M1)
        {
            FailureBehavior = FailureBehavior.Restart,
            NextAutomaticRestartSeed = 123456,
        };
        var unsafePlayer = new PlayerState(
            true,
            new Vector2(60, 0),
            new Vector2(0, -1),
            0);
        engine.Start(0);
        engine.NextAutomaticRestartSeed = 123456;

        _ = engine.Update(8, unsafePlayer);

        Assert.Equal(2, engine.PullNumber);
        Assert.Equal(123456, engine.Seed);
        Assert.Equal(123456, engine.Encounter.Assignments.Seed);
        Assert.Null(engine.NextAutomaticRestartSeed);
        Assert.Single(engine.History);
    }
}
