using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Rendering;

internal sealed class ArenaOverlayRenderer
{
    private static readonly IReadOnlyList<SimulatedGhost> NoGhosts = [];

    private readonly RenderExceptionLogger errors;
    private readonly ShapeRenderer shapes;
    private readonly GhostRenderer ghosts;
    private readonly ProjectionHelper projection;

    public ArenaOverlayRenderer(IGameGui gameGui, IPluginLog log)
    {
        errors = new RenderExceptionLogger(log, nameof(ArenaOverlayRenderer));
        projection = new ProjectionHelper(gameGui, errors);
        shapes = new ShapeRenderer(projection);
        ghosts = new GhostRenderer(projection, errors);
    }

    /// <summary>Projection failures recorded during the last drawn frame.</summary>
    public int LastProjectionFailureCount { get; private set; }

    public void Draw(
        ArenaTransform arena,
        SimulationEngine engine,
        PlayerState player,
        Configuration configuration,
        IReadOnlyList<SimulatedGhost>? testGhosts = null)
    {
        try
        {
            projection.BeginFrame();
            if (engine.Clock.State is not (
                SimulationState.Countdown
                or SimulationState.Running
                or SimulationState.Paused))
            {
                DrawTestGhostsOnly(arena, testGhosts, configuration);
                return;
            }

            var time = engine.Clock.Time;
            var activeShapes =
                engine.Encounter.ActiveShapes(time, configuration.TelegraphLeadTime);
            var requiredPosition = configuration.ShowSafeZones
                ? engine.Encounter.RequiredPositionShape(time)
                : null;
            DrawCore(
                arena,
                activeShapes,
                requiredPosition,
                player,
                engine.Encounter.SimulatedPartyPositions,
                engine.PlayerRole,
                Combine(engine.Encounter.ActiveGhosts(time), testGhosts),
                time,
                configuration);
        }
        catch (Exception exception)
        {
            errors.Report(exception);
        }
        finally
        {
            LastProjectionFailureCount = projection.FailureCount;
        }
    }

    /// <summary>
    /// Keeps developer test ghosts visible while the simulation is stopped so
    /// coordinate and facing checks can be made without starting a pull.
    /// </summary>
    private void DrawTestGhostsOnly(
        ArenaTransform arena,
        IReadOnlyList<SimulatedGhost>? testGhosts,
        Configuration configuration)
    {
        if (testGhosts is null
            || testGhosts.Count == 0
            || !configuration.OverlayEnabled
            || !arena.IsInitialized)
        {
            return;
        }

        ghosts.Draw(ImGui.GetForegroundDrawList(), arena, testGhosts, 0, configuration);
    }

    private static IReadOnlyList<SimulatedGhost> Combine(
        IReadOnlyList<SimulatedGhost> encounterGhosts,
        IReadOnlyList<SimulatedGhost>? testGhosts)
    {
        if (testGhosts is null || testGhosts.Count == 0)
        {
            return encounterGhosts;
        }

        var combined = new List<SimulatedGhost>(encounterGhosts.Count + testGhosts.Count);
        combined.AddRange(encounterGhosts);
        combined.AddRange(testGhosts);
        return combined;
    }

    public void Draw(
        ArenaTransform arena,
        IEnumerable<ArenaShape> activeShapes,
        PlayerState player,
        IReadOnlyDictionary<PartyRole, Vector2> partyPositions,
        Configuration configuration,
        IReadOnlyList<SimulatedGhost>? testGhosts = null,
        double time = 0)
    {
        try
        {
            DrawCore(
                arena,
                activeShapes,
                null,
                player,
                partyPositions,
                null,
                testGhosts ?? NoGhosts,
                time,
                configuration);
        }
        catch (Exception exception)
        {
            errors.Report(exception);
        }
    }

    private void DrawCore(
        ArenaTransform arena,
        IEnumerable<ArenaShape> activeShapes,
        ArenaShape? requiredPosition,
        PlayerState player,
        IReadOnlyDictionary<PartyRole, Vector2> partyPositions,
        PartyRole? playerRole,
        IReadOnlyList<SimulatedGhost> activeGhosts,
        double time,
        Configuration configuration)
    {
        if (!configuration.OverlayEnabled || !arena.IsInitialized)
        {
            return;
        }

        var background = ImGui.GetBackgroundDrawList();
        var foreground = ImGui.GetForegroundDrawList();

        if (configuration.ShowArenaBoundary)
        {
            shapes.Draw(
                background,
                foreground,
                arena,
                new ArenaShape
                {
                    Kind = ShapeKind.ArenaBoundary,
                    Label = "Arena boundary",
                    Phase = ShapePhase.Information,
                    StartsAt = double.NegativeInfinity,
                    EndsAt = double.PositiveInfinity,
                    Origin = Vector2.Zero,
                    Radius = KefkaP4Constants.ArenaRadius,
                },
                configuration);
        }

        foreach (var shape in activeShapes)
        {
            shapes.Draw(background, foreground, arena, shape, configuration);
        }

        if (requiredPosition is not null)
        {
            shapes.Draw(background, foreground, arena, requiredPosition, configuration);
        }

        foreach (var pair in partyPositions)
        {
            if (pair.Key == playerRole)
            {
                continue;
            }

            // A role with an active ghost is drawn by the mannequin instead, so
            // the flat bot marker is skipped to avoid stacking two indicators.
            if (HasGhost(activeGhosts, pair.Key))
            {
                continue;
            }

            shapes.DrawMarker(
                background,
                foreground,
                arena,
                pair.Value,
                pair.Key.Key(),
                1.1f,
                ShapePhase.Information,
                configuration,
                alwaysLabel: configuration.ShowDebugCoordinateLabels);
        }

        ghosts.Draw(foreground, arena, activeGhosts, time, configuration);

        if (player.IsValid)
        {
            shapes.DrawMarker(
                background,
                foreground,
                arena,
                player.ArenaPosition,
                "YOU",
                1.4f,
                ShapePhase.Success,
                configuration,
                alwaysLabel: true);

            DrawGazeAids(foreground, arena, player, activeGhosts, configuration);
        }

        if (configuration.ShowDebugCoordinateLabels)
        {
            DrawCoordinateLabels(foreground, arena, configuration);
        }
    }

    private static bool HasGhost(IReadOnlyList<SimulatedGhost> activeGhosts, PartyRole role)
    {
        for (var index = 0; index < activeGhosts.Count; index++)
        {
            if (string.Equals(activeGhosts[index].Id, role.Key(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Optional gaze solution aids. Every one of these is disabled by default
    /// because the browser simulator never reveals the answer this directly.
    /// </summary>
    private void DrawGazeAids(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        PlayerState player,
        IReadOnlyList<SimulatedGhost> activeGhosts,
        Configuration configuration)
    {
        var height = Math.Clamp(configuration.GroundHeightOffset, -2, 2);
        var thickness = Math.Clamp(configuration.LineThickness, 1, 8);
        var facing = Geometry.SafeNormalize(player.FacingDirection);

        if (configuration.ShowPlayerFacingArrow)
        {
            DrawArenaSegment(
                drawList,
                arena,
                player.ArenaPosition,
                player.ArenaPosition + (facing * 12f),
                height,
                ImGui.GetColorU32(new Vector4(0.25f, 1.00f, 0.55f, 0.95f)),
                thickness);
        }

        if (configuration.ShowGazeThresholdCone)
        {
            var coneColor = ImGui.GetColorU32(new Vector4(0.25f, 1.00f, 0.55f, 0.55f));
            foreach (var sign in stackalloc[] { -1f, 1f })
            {
                var edge = Geometry.RotateDegrees(facing, sign * KefkaP4Gaze.ThresholdDegrees);
                DrawArenaSegment(
                    drawList,
                    arena,
                    player.ArenaPosition,
                    player.ArenaPosition + (edge * 12f),
                    height,
                    coneColor,
                    thickness);
            }
        }

        if (!configuration.ShowGazeLines && !configuration.ShowGazeAngles)
        {
            return;
        }

        for (var index = 0; index < activeGhosts.Count; index++)
        {
            var ghost = activeGhosts[index];
            if (!ghost.IsGazeSource)
            {
                continue;
            }

            var diagnostic = KefkaP4Gaze.Evaluate(
                ghost.Id,
                player.ArenaPosition,
                player.FacingDirection,
                ghost.ArenaPosition,
                fake: false);

            var color = ImGui.GetColorU32(diagnostic.LookedToward
                ? new Vector4(1.00f, 0.25f, 0.30f, 0.9f)
                : new Vector4(0.35f, 0.68f, 1.00f, 0.75f));

            if (configuration.ShowGazeLines)
            {
                DrawArenaSegment(
                    drawList,
                    arena,
                    player.ArenaPosition,
                    ghost.ArenaPosition,
                    height,
                    color,
                    thickness);
            }

            if (configuration.ShowGazeAngles)
            {
                var midpoint = (player.ArenaPosition + ghost.ArenaPosition) * 0.5f;
                shapes.DrawWorldLabel(
                    drawList,
                    arena,
                    midpoint,
                    $"{ghost.Id} {diagnostic.AngleDegrees:0.0}deg",
                    height,
                    color);
            }
        }
    }

    private void DrawArenaSegment(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        Vector2 from,
        Vector2 to,
        float height,
        uint color,
        float thickness)
    {
        var start = projection.Project(arena.SimulatorToWorld(from, height));
        var end = projection.Project(arena.SimulatorToWorld(to, height));
        if (start.Succeeded && end.Succeeded)
        {
            drawList.AddLine(start.ScreenPosition, end.ScreenPosition, color, thickness);
        }
    }

    private void DrawCoordinateLabels(
        ImDrawListPtr foreground,
        ArenaTransform arena,
        Configuration configuration)
    {
        var radius = KefkaP4Constants.ArenaRadius;
        var height = Math.Clamp(configuration.GroundHeightOffset, -2, 2);
        var labelColor = ImGui.GetColorU32(new Vector4(0.85f, 0.90f, 1.00f, 1));
        shapes.DrawWorldLabel(foreground, arena, Vector2.Zero, "C (0, 0)", height, labelColor);
        shapes.DrawWorldLabel(
            foreground,
            arena,
            new Vector2(0, -radius),
            $"N (0, {-radius:0})",
            height,
            labelColor);
        shapes.DrawWorldLabel(
            foreground,
            arena,
            new Vector2(radius, 0),
            $"E ({radius:0}, 0)",
            height,
            labelColor);
        shapes.DrawWorldLabel(
            foreground,
            arena,
            new Vector2(0, radius),
            $"S (0, {radius:0})",
            height,
            labelColor);
        shapes.DrawWorldLabel(
            foreground,
            arena,
            new Vector2(-radius, 0),
            $"W ({-radius:0}, 0)",
            height,
            labelColor);
    }
}
