using System.Numerics;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Core.Tests;

public sealed class SourceSolutionIntegrationTests
{
    public static IEnumerable<object[]> RepresentativePulls()
    {
        foreach (var seed in new long[] { -99123, 0, 1, 42, 8675309 })
        {
            foreach (var role in PartyRoles.All)
            {
                yield return [seed, role];
            }
        }
    }

    [Theory]
    [MemberData(nameof(RepresentativePulls))]
    public void GeneratedSourceSolutionPassesFullTimeline(long seed, PartyRole role)
    {
        var engine = new SimulationEngine(seed, role);
        engine.Start(0);
        var failures = new List<string>();

        while (engine.NextEvent is { } nextEvent)
        {
            var player = SourceSolutionPlayer(engine, nextEvent);
            var delta = Math.Max(0, nextEvent.Time - engine.Clock.Time);
            foreach (var result in engine.Update(delta, player))
            {
                if (!result.Passed)
                {
                    failures.Add($"{result.Timestamp:0.0} {result.Mechanic}: {result.Reason}");
                }
            }
        }

        _ = engine.Update(
            KefkaP4Constants.EncounterLength - engine.Clock.Time,
            SourceSolutionPlayer(engine, null));

        Assert.True(
            failures.Count == 0,
            $"Seed {seed}, role {role}: {string.Join("; ", failures)}");
        Assert.Equal(ExpectedResolutionCount, engine.History.Count);
        Assert.All(engine.History, result => Assert.True(result.Passed, result.Reason));
        Assert.Equal(SimulationState.Completed, engine.Clock.State);
        Assert.Equal(KefkaP4Constants.EncounterLength, engine.Clock.Time, 6);
    }

    private static PlayerState SourceSolutionPlayer(
        SimulationEngine engine,
        TimelineEvent? nextEvent)
    {
        var encounter = engine.Encounter;
        var position = encounter.RequiredPosition
            ?? KefkaP4Positions.Initial(engine.PlayerRole);
        var speed = nextEvent?.Kind switch
        {
            TimelineEventKind.ResolveShortDebuffs =>
                AccelerationSpeed(encounter, shortSet: true),
            TimelineEventKind.ResolveLongDebuffs =>
                AccelerationSpeed(encounter, shortSet: false),
            _ => 0,
        };
        var facing = nextEvent?.Kind switch
        {
            TimelineEventKind.ResolveShriekOne =>
                FindShriekFacing(encounter, position, first: true),
            TimelineEventKind.ResolveShriekTwo =>
                FindShriekFacing(encounter, position, first: false),
            _ => new Vector2(0, -1),
        };

        return new PlayerState(true, position, facing, speed);
    }

    private static float AccelerationSpeed(KefkaP4Encounter encounter, bool shortSet)
    {
        var kind = shortSet
            ? AssignmentKind.ShortAcceleration
            : AssignmentKind.LongAcceleration;
        var assignment = encounter.Assignments.GrandCrossOne.Find(encounter.PlayerRole) == kind
            ? encounter.Assignments.GrandCrossOne
            : encounter.Assignments.GrandCrossTwo.Find(encounter.PlayerRole) == kind
                ? encounter.Assignments.GrandCrossTwo
                : null;

        return assignment?.Fake == true
            ? 1
            : 0;
    }

    private static Vector2 FindShriekFacing(
        KefkaP4Encounter encounter,
        Vector2 playerPosition,
        bool first)
    {
        var assignment = first
            ? encounter.Assignments.GrandCrossOne
            : encounter.Assignments.GrandCrossTwo;
        var targets = new[] { assignment.ShriekDps, assignment.ShriekSupport }
            .Where(role => role != encounter.PlayerRole)
            .Select(role => encounter.SimulatedPartyPositions[role])
            .ToArray();

        for (var tenthDegree = 0; tenthDegree < 3600; tenthDegree++)
        {
            var facing = Geometry.RotateDegrees(new Vector2(0, -1), tenthDegree / 10f);
            var targetFacings = targets
                .Select(target => Geometry.IsFacing(playerPosition, facing, target))
                .ToArray();
            var succeeds = assignment.Fake
                ? targetFacings.All(value => value)
                : targetFacings.All(value => !value);
            if (succeeds)
            {
                return facing;
            }
        }

        throw new InvalidOperationException(
            $"No source-valid Shriek facing for role {encounter.PlayerRole}, "
            + $"fake={assignment.Fake}, first={first}.");
    }

    private static readonly int ExpectedResolutionCount =
        KefkaP4Timeline.Events.Count(timelineEvent => timelineEvent.Kind is
            TimelineEventKind.ResolveMysteriousMagic
            or TimelineEventKind.ResolveFlood
            or TimelineEventKind.ResolveShortDebuffs
            or TimelineEventKind.ResolveThrummingThunder
            or TimelineEventKind.ResolveShriekOne
            or TimelineEventKind.ResolveInferno
            or TimelineEventKind.ResolveLongDebuffs
            or TimelineEventKind.ResolveBlizzardBlowout
            or TimelineEventKind.ResolveShriekTwo
            or TimelineEventKind.ResolveManaRelease);
}
