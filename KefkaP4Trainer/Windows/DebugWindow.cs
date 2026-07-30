using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Windows;

internal sealed class DebugWindow : Window
{
    private readonly ITrainerWindowHost host;
    private Vector2 testGhostPosition = new(0, -10);

    public DebugWindow(ITrainerWindowHost host)
        : base("Kefka P4 Trainer Debug###KefkaP4TrainerDebug")
    {
        this.host = host;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 520),
            MaximumSize = new Vector2(1000, 1100),
        };
    }

    public override void Draw()
    {
        DrawPositionState();
        Section("Simulation");
        DrawSimulationState();
        Section("Timeline controls");
        DrawTimelineControls();
        Section("Timeline events");
        DrawTimelineEvents();
        Section("Active shapes");
        DrawShapes();
        Section("Ghosts and gaze");
        DrawGhostsAndGaze();
        Section("Simulated debuffs");
        DrawDebuffs();
        Section("Assignments");
        DrawAssignments();
        Section("Result history");
        DrawHistory();
    }

    private void DrawPositionState()
    {
        Section("Player and arena");

        if (host.LastPlayerWorldPosition is { } world)
        {
            ImGui.TextUnformatted(
                $"Raw world: X {world.X:0.000}, Y {world.Y:0.000}, Z {world.Z:0.000}");
        }
        else
        {
            ImGui.TextDisabled("Raw world: local player unavailable");
        }

        var player = host.LastPlayer;
        if (player.IsValid)
        {
            ImGui.TextUnformatted(
                $"Arena: X {player.ArenaPosition.X:0.000}, Y {player.ArenaPosition.Y:0.000}");
            ImGui.TextUnformatted(
                $"Facing: X {player.FacingDirection.X:0.000}, Y {player.FacingDirection.Y:0.000}"
                + $"  |  Speed {player.Speed:0.000}  |  Moving {player.IsMoving}");
        }
        else
        {
            ImGui.TextDisabled("Converted arena position: unavailable");
        }

        if (host.LastPlayerGameRotation is { } gameRotation)
        {
            ImGui.TextUnformatted(
                $"Raw game rotation: {gameRotation:0.0000} rad"
                + $" / {gameRotation * 180 / MathF.PI:0.0} deg");
        }

        if (host.Transform.IsInitialized)
        {
            var center = host.Transform.Center;
            ImGui.TextUnformatted(
                $"Arena center: X {center.X:0.000}, Y {center.Y:0.000}, Z {center.Z:0.000}");
            ImGui.TextUnformatted(
                $"Arena clockwise rotation: {host.Transform.RotationRadians:0.0000} rad"
                + $" / {host.Transform.RotationDegrees:0.0} deg");
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.55f, 0.25f, 1), "Arena transform is not initialized.");
        }
    }

    private void DrawSimulationState()
    {
        var engine = host.Engine;
        var clock = engine.Clock;
        ImGui.TextUnformatted($"State: {clock.State}");
        ImGui.TextUnformatted($"Timeline time: {clock.Time:0.000}s");
        ImGui.TextUnformatted($"Playback speed: {clock.PlaybackSpeed:0.00}x");
        ImGui.TextUnformatted($"Pull: {engine.PullNumber}");
        ImGui.TextUnformatted($"Seed: {engine.Seed}");
        ImGui.TextUnformatted($"Player role: {engine.PlayerRole.DisplayName()}");
        ImGui.TextWrapped($"Current cue: {engine.Encounter.CurrentCue(Math.Max(0, clock.Time))}");
        ImGui.TextWrapped(
            $"Current assignment: {engine.Encounter.AssignmentSummary(Math.Max(0, clock.Time))}");
        ImGui.TextWrapped($"Last evaluation: {engine.Encounter.LastMechanicEvaluation}");

        if (engine.NextEvent is { } next)
        {
            ImGui.TextUnformatted($"Next event: {next.Name} at {next.Time:0.0}s");
        }
        else
        {
            ImGui.TextDisabled("Next event: none");
        }
    }

    private void DrawTimelineControls()
    {
        var engine = host.Engine;
        var paused = engine.Clock.State == SimulationState.Paused;
        var scrubTime = (float)Math.Clamp(
            engine.Clock.Time,
            0,
            KefkaP4Constants.EncounterLength);

        ImGui.BeginDisabled(!paused);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat(
            "##TimelineScrubber",
            ref scrubTime,
            0,
            (float)KefkaP4Constants.EncounterLength,
            "%.1f s"))
        {
            engine.Seek(scrubTime, host.LastPlayer);
        }
        ImGui.EndDisabled();

        if (!paused)
        {
            ImGui.TextDisabled("Pause the simulation to scrub or jump to a checkpoint.");
        }
        else
        {
            ImGui.TextDisabled(
                "Seeking rebuilds encounter state without grading skipped resolution events.");
        }

        var availableWidth = Math.Max(180, ImGui.GetContentRegionAvail().X);
        var usedWidth = 0f;
        foreach (var checkpoint in KefkaP4Timeline.Checkpoints)
        {
            var label = $"{checkpoint.Name}##Checkpoint{checkpoint.Time:0.0}";
            var buttonWidth = ImGui.CalcTextSize(checkpoint.Name).X
                + (ImGui.GetStyle().FramePadding.X * 2);
            if (usedWidth > 0 && usedWidth + buttonWidth <= availableWidth)
            {
                ImGui.SameLine();
            }
            else
            {
                usedWidth = 0;
            }

            ImGui.BeginDisabled(!paused);
            if (ImGui.Button(label))
            {
                engine.Seek(checkpoint.Time, host.LastPlayer);
            }
            ImGui.EndDisabled();
            usedWidth += buttonWidth + ImGui.GetStyle().ItemSpacing.X;
        }
    }

    private void DrawTimelineEvents()
    {
        var time = Math.Max(0, host.Engine.Clock.Time);
        var events = KefkaP4Timeline.Events;
        var priorIndex = -1;
        for (var index = 0; index < events.Count; index++)
        {
            if (events[index].Time <= time + 0.000001)
            {
                priorIndex = index;
            }
            else
            {
                break;
            }
        }

        if (priorIndex >= 0)
        {
            var previous = events[priorIndex];
            ImGui.TextColored(
                new Vector4(0.55f, 0.9f, 1, 1),
                $"Most recent: {previous.Time,6:0.0}s  {previous.Name}");
        }
        else
        {
            ImGui.TextDisabled("Most recent: none");
        }

        ImGui.TextUnformatted("Upcoming:");
        var firstUpcoming = Math.Max(0, priorIndex + 1);
        var shown = 0;
        for (var index = firstUpcoming; index < events.Count && shown < 8; index++, shown++)
        {
            var timelineEvent = events[index];
            ImGui.BulletText(
                $"{timelineEvent.Time,6:0.0}s  [{timelineEvent.Kind}] {timelineEvent.Name}");
        }

        if (shown == 0)
        {
            ImGui.TextDisabled("No events remain.");
        }
    }

    private void DrawShapes()
    {
        var time = Math.Max(0, host.Engine.Clock.Time);
        var shapes = host.Engine.Encounter.ActiveShapes(
            time,
            host.Configuration.TelegraphLeadTime);
        ImGui.TextUnformatted($"Count: {shapes.Count}");

        foreach (var shape in shapes)
        {
            ImGui.BulletText(
                $"{shape.Phase}/{shape.Kind}: {shape.Label}"
                + $"  [{shape.StartsAt:0.0}-{shape.EndsAt:0.0}]"
                + $"  O=({shape.Origin.X:0.0},{shape.Origin.Y:0.0})");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(ShapeDetails(shape));
            }
        }

        if (host.Engine.Encounter.RequiredPositionShape(time) is { } required)
        {
            ImGui.BulletText(
                $"Required: {required.Label} at "
                + $"({required.Origin.X:0.00}, {required.Origin.Y:0.00})");
        }
    }

    private void DrawGhostsAndGaze()
    {
        var time = Math.Max(0, host.Engine.Clock.Time);
        var player = host.LastPlayer;
        var ghosts = host.Engine.Encounter.ActiveGhosts(time);

        ImGui.TextUnformatted($"Projection failures last frame: {host.LastProjectionFailureCount}");
        ImGui.TextUnformatted($"Active encounter ghosts: {ghosts.Count}");
        foreach (var ghost in ghosts)
        {
            ImGui.BulletText(
                $"{ghost.Id}: arena ({ghost.ArenaPosition.X:0.00}, {ghost.ArenaPosition.Y:0.00})"
                + $"  facing {ghost.ArenaFacing * 180 / MathF.PI:0.0} deg"
                + $"  [{ghost.SpawnTime:0.0}-{ghost.DespawnTime:0.0}]"
                + $"  {(ghost.IsGazeSource ? "gaze source" : "bystander")}"
                + $"  alpha {ghost.AlphaAt(time, host.Configuration.GhostFadeDuration):0.00}");
        }

        DrawLiveGaze(player, ghosts);
        DrawLastGazeResult();
        DrawTestGhostControls();
    }

    /// <summary>
    /// Live per-source gaze maths against the currently active sources. This
    /// mirrors what a resolution would record if it fired right now.
    /// </summary>
    private void DrawLiveGaze(PlayerState player, IReadOnlyList<SimulatedGhost> ghosts)
    {
        ImGui.Spacing();
        if (!player.IsValid)
        {
            ImGui.TextDisabled("Live gaze: local player unavailable.");
            return;
        }

        var sources = ghosts.Where(ghost => ghost.IsGazeSource).ToArray();
        if (sources.Length == 0)
        {
            ImGui.TextDisabled("Live gaze: no active gaze source.");
            return;
        }

        var fake = IsCurrentShriekFake();
        ImGui.TextUnformatted(
            $"Live gaze requirement: {(fake ? "LOOK TOWARD (Fake)" : "LOOK AWAY (Real)")}"
            + $"  |  threshold {KefkaP4Gaze.ThresholdDegrees:0.#} deg half-cone");
        ImGui.TextUnformatted(
            $"Player arena facing: ({player.FacingDirection.X:0.000}, {player.FacingDirection.Y:0.000})");

        var allPassed = true;
        foreach (var source in sources)
        {
            var diagnostic = KefkaP4Gaze.Evaluate(
                source.Id,
                player.ArenaPosition,
                player.FacingDirection,
                source.ArenaPosition,
                fake);
            allPassed &= diagnostic.Passed;
            ImGui.TextColored(
                diagnostic.Passed
                    ? new Vector4(0.35f, 0.95f, 0.45f, 1)
                    : new Vector4(1, 0.35f, 0.35f, 1),
                $"  {diagnostic.GhostId}: dir ({diagnostic.DirectionToGhost.X:0.000},"
                + $" {diagnostic.DirectionToGhost.Y:0.000})"
                + $"  dot {diagnostic.DotProduct:0.0000}"
                + $"  angle {diagnostic.AngleDegrees:0.00} deg"
                + $"  toward {diagnostic.LookedToward}"
                + $"  {(diagnostic.Passed ? "PASS" : "FAIL")}");
        }

        ImGui.TextColored(
            allPassed ? new Vector4(0.35f, 0.95f, 0.45f, 1) : new Vector4(1, 0.35f, 0.35f, 1),
            $"  Combined: {(allPassed ? "PASS" : "FAIL")}");
    }

    /// <summary>
    /// Which Grand Cross owns the gaze window currently on screen. Shriek 1
    /// resolves at 72.8s, so anything at or past it belongs to Shriek 2.
    /// </summary>
    private bool IsCurrentShriekFake()
    {
        var assignments = host.Engine.Encounter.Assignments;
        return host.Engine.Clock.Time >= 72.8
            ? assignments.GrandCrossTwo.Fake
            : assignments.GrandCrossOne.Fake;
    }

    private void DrawLastGazeResult()
    {
        var diagnostics = host.Engine.Encounter.LastGazeDiagnostics;
        ImGui.Spacing();
        if (diagnostics.Count == 0)
        {
            ImGui.TextDisabled("Last recorded gaze resolution: none this pull.");
            return;
        }

        ImGui.TextUnformatted("Last recorded gaze resolution:");
        foreach (var diagnostic in diagnostics)
        {
            ImGui.BulletText(
                $"{diagnostic.GhostId}: player ({diagnostic.PlayerPosition.X:0.00},"
                + $" {diagnostic.PlayerPosition.Y:0.00})"
                + $" -> ghost ({diagnostic.GhostPosition.X:0.00}, {diagnostic.GhostPosition.Y:0.00})");
            ImGui.TextUnformatted(
                $"      facing ({diagnostic.PlayerFacing.X:0.000}, {diagnostic.PlayerFacing.Y:0.000})"
                + $"  dot {diagnostic.DotProduct:0.0000}"
                + $"  angle {diagnostic.AngleDegrees:0.00}/{diagnostic.ThresholdDegrees:0.#} deg"
                + $"  toward {diagnostic.LookedToward}"
                + $"  {(diagnostic.Passed ? "PASS" : "FAIL")}");
        }
    }

    private void DrawTestGhostControls()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Test ghosts (developer verification only):");
        var distance = KefkaP4Constants.ArenaRadius * 0.4f;
        if (ImGui.Button("North##TestGhost"))
        {
            host.AddTestGhost(new Vector2(0, -distance), gazeSource: true);
        }

        ImGui.SameLine();
        if (ImGui.Button("East##TestGhost"))
        {
            host.AddTestGhost(new Vector2(distance, 0), gazeSource: true);
        }

        ImGui.SameLine();
        if (ImGui.Button("South##TestGhost"))
        {
            host.AddTestGhost(new Vector2(0, distance), gazeSource: true);
        }

        ImGui.SameLine();
        if (ImGui.Button("West##TestGhost"))
        {
            host.AddTestGhost(new Vector2(-distance, 0), gazeSource: true);
        }

        ImGui.SetNextItemWidth(220);
        ImGui.InputFloat2("Arena X/Y##TestGhostCoords", ref testGhostPosition);
        ImGui.SameLine();
        if (ImGui.Button("Spawn here##TestGhost"))
        {
            host.AddTestGhost(testGhostPosition, gazeSource: true);
        }

        ImGui.BeginDisabled(host.TestGhosts.Count == 0);
        if (ImGui.Button($"Clear test ghosts ({host.TestGhosts.Count})"))
        {
            host.ClearTestGhosts();
        }
        ImGui.EndDisabled();

        var configuration = host.Configuration;
        var facingArrow = configuration.ShowPlayerFacingArrow;
        if (ImGui.Checkbox("Show player facing arrow", ref facingArrow))
        {
            configuration.ShowPlayerFacingArrow = facingArrow;
            host.SaveConfiguration();
        }

        var gazeLines = configuration.ShowGazeLines;
        if (ImGui.Checkbox("Show lines to gaze sources", ref gazeLines))
        {
            configuration.ShowGazeLines = gazeLines;
            host.SaveConfiguration();
        }

        var cone = configuration.ShowGazeThresholdCone;
        if (ImGui.Checkbox("Show gaze threshold cone", ref cone))
        {
            configuration.ShowGazeThresholdCone = cone;
            host.SaveConfiguration();
        }

        var angles = configuration.ShowGazeAngles;
        if (ImGui.Checkbox("Show numeric gaze angles", ref angles))
        {
            configuration.ShowGazeAngles = angles;
            host.SaveConfiguration();
        }

        var ghostIds = configuration.ShowGhostIds;
        if (ImGui.Checkbox("Show ghost IDs", ref ghostIds))
        {
            configuration.ShowGhostIds = ghostIds;
            host.SaveConfiguration();
        }

        var ghostFacing = configuration.ShowGhostFacingArrows;
        if (ImGui.Checkbox("Show ghost facing arrows", ref ghostFacing))
        {
            configuration.ShowGhostFacingArrows = ghostFacing;
            host.SaveConfiguration();
        }
    }

    private void DrawDebuffs()
    {
        var time = Math.Max(0, host.Engine.Clock.Time);
        var playerRole = host.Engine.PlayerRole;
        var playerDebuffs = host.Engine.Encounter.ActiveDebuffs(time, playerRole);

        ImGui.TextUnformatted($"{playerRole.DisplayName()} active statuses: {playerDebuffs.Count}");
        foreach (var debuff in playerDebuffs)
        {
            var remaining = debuff.RemainingAt(time);
            var duration = double.IsPositiveInfinity(remaining)
                ? "permanent"
                : $"{remaining:0.0}s";
            ImGui.BulletText(
                $"{debuff.Name} ({debuff.ShortLabel}) - {duration}"
                + $"  applied {debuff.AppliedAt:0.0}, expires "
                + $"{(double.IsPositiveInfinity(debuff.ExpiresAt) ? "never" : $"{debuff.ExpiresAt:0.0}")}");
        }

        if (!ImGui.CollapsingHeader("All party statuses"))
        {
            return;
        }

        foreach (var role in PartyRoles.All)
        {
            var active = host.Engine.Encounter.ActiveDebuffs(time, role);
            if (active.Count == 0)
            {
                continue;
            }

            ImGui.TextUnformatted($"{role.Key()}:");
            ImGui.SameLine();
            ImGui.TextWrapped(string.Join(
                ", ",
                active.Select(
                    debuff =>
                    {
                        var remaining = debuff.RemainingAt(time);
                        return $"{debuff.ShortLabel} "
                            + (double.IsPositiveInfinity(remaining) ? "inf" : $"{remaining:0.0}s");
                    })));
        }
    }

    private void DrawAssignments()
    {
        var assignments = host.Engine.Encounter.Assignments;
        ImGui.TextUnformatted($"Assignment seed: {assignments.Seed}");
        ImGui.TextUnformatted(
            $"Neo: GC1 {(assignments.GrandCrossOne.Fake ? "Fake" : "Real")}, "
            + $"GC2 {(assignments.GrandCrossTwo.Fake ? "Fake" : "Real")}, "
            + $"GC3 {(assignments.NeoThreeFake ? "Fake" : "Real")}");
        ImGui.TextUnformatted(
            $"Chaos: {(assignments.InfernoFirst ? "Inferno first" : "Tsunami first")}, "
            + $"Inferno {(assignments.InfernoFake ? "Fake" : "Real")}, "
            + $"Tsunami {(assignments.TsunamiFake ? "Fake" : "Real")}");
        ImGui.TextUnformatted(
            $"Flood: {(assignments.FloodFake ? "Fake" : "Real")}, "
            + $"Black {(assignments.BlackWest ? "West" : "East")}, "
            + $"Neo rotation {assignments.NeoRotationDegrees:0} deg");
        ImGui.TextUnformatted(
            $"Mana Release: Thunder {(assignments.ManaReleaseThunderFake ? "Fake" : "Real")}, "
            + $"Ice {(assignments.ManaReleaseIceFake ? "Fake" : "Real")}");

        foreach (var role in PartyRoles.All)
        {
            var field = assignments.FieldRoles.Contains(role) ? "Field" : "Death";
            var wound = assignments.BlackWoundRoles.Contains(role) ? "Black" : "White";
            var safeColor = assignments.BlackSafeRoles.Contains(role) ? "black-safe" : "white-safe";
            ImGui.BulletText(
                $"{role.Key()}: GC1 {assignments.GrandCrossOne.Summary(role)}; "
                + $"GC2 {assignments.GrandCrossTwo.Summary(role)}; "
                + $"{field}; {wound} Wound; {safeColor}");
        }

        if (ImGui.CollapsingHeader("Deterministic signature"))
        {
            ImGui.TextWrapped(assignments.Signature);
            if (ImGui.Button("Copy assignment signature"))
            {
                ImGui.SetClipboardText(assignments.Signature);
            }
        }
    }

    private void DrawHistory()
    {
        var history = host.Engine.History;
        ImGui.TextUnformatted($"Recorded evaluations: {history.Count}");
        foreach (var result in history.TakeLast(12).Reverse())
        {
            var color = result.Passed
                ? new Vector4(0.35f, 0.95f, 0.45f, 1)
                : new Vector4(1, 0.35f, 0.35f, 1);
            ImGui.TextColored(
                color,
                $"P{result.PullNumber} {result.Timestamp,6:0.0}s "
                + $"{(result.Passed ? "PASS" : "FAIL")} {result.Mechanic}");
            if (!result.Passed)
            {
                ImGui.SameLine();
                ImGui.TextWrapped($"- {result.Reason}");
            }
        }

        ImGui.BeginDisabled(history.Count == 0);
        if (ImGui.Button("Clear result history"))
        {
            host.ClearHistory();
        }
        ImGui.EndDisabled();
    }

    private static string ShapeDetails(ArenaShape shape) =>
        $"Label: {shape.Label}\n"
        + $"Phase: {shape.Phase}\n"
        + $"Origin: {shape.Origin.X:0.000}, {shape.Origin.Y:0.000}\n"
        + $"Direction: {shape.Direction.X:0.000}, {shape.Direction.Y:0.000}\n"
        + $"Radius: {shape.Radius:0.000}; inner: {shape.InnerRadius:0.000}\n"
        + $"Width: {shape.Width:0.000}; length: {shape.Length:0.000}\n"
        + $"Angle: {shape.AngleDegrees:0.000}";

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.86f, 0.72f, 1, 1), title);
        ImGui.Separator();
    }
}
