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
            // The guide needs this shape even when the filled safe-zone circle is
            // switched off. ShapeRenderer.Draw still self-gates on ShowSafeZones,
            // so fetching it here does not make the circle itself appear.
            var requiredPosition =
                configuration.ShowSafeZones || configuration.ShowDestinationGuide
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
                configuration,
                configuration.ShowMagicTell ? engine.Encounter.CurrentMagicTell(time) : null);
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
        Configuration configuration,
        MagicTell? magicTell = null)
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

        if (requiredPosition is not null && configuration.ShowDestinationGuide)
        {
            DrawDestinationGuide(
                foreground,
                arena,
                requiredPosition,
                player,
                time,
                configuration);
        }

        if (magicTell is { } tell)
        {
            DrawMagicTell(foreground, arena, tell, configuration);
        }

        if (configuration.ShowDebugCoordinateLabels)
        {
            DrawCoordinateLabels(foreground, arena, configuration);
        }
    }

    /// <summary>
    /// Mirrors the orbs on the rings around Kefka, where a question mark means
    /// that element resolves as the opposite of the orientation it telegraphs.
    /// </summary>
    /// <remarks>
    /// The telegraphs themselves are drawn identically whether or not a pattern
    /// is fake, so without this badge the flip carries no on-screen tell at all
    /// and the mechanic degrades into a coin flip per element.
    /// </remarks>
    private void DrawMagicTell(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        MagicTell tell,
        Configuration configuration)
    {
        if (!tell.HasThunder && !tell.HasIce)
        {
            return;
        }

        // Each badge is projected from its own world anchor over the arena centre,
        // which is where Kefka casts from. A shared anchor plus a screen-space
        // stack collapsed the pair as the camera pitched down; separate heights
        // keep lightning at his shoulders and ice at his knees from every angle.
        var ground = Math.Clamp(configuration.GroundHeightOffset, -2, 2);
        var scale = Math.Clamp(configuration.MagicTellScale, 0.5f, 3);
        var radius = 26 * scale;
        var captionGap = 10 * scale;
        var spread = Math.Clamp(configuration.MagicTellHorizontalSpread, 0, 12);
        var iceHeight = Math.Clamp(configuration.MagicTellIceHeight, -2, 12);
        var lightningHeight = Math.Clamp(configuration.MagicTellLightningHeight, -2, 12);

        var labelAnchor = Vector2.Zero;
        var hasLabelAnchor = false;

        if (tell.HasIce)
        {
            var anchor = projection.Project(
                arena.SimulatorToWorld(new Vector2(-spread, 0), ground + iceHeight));
            if (anchor.Succeeded && anchor.InView)
            {
                DrawTellRow(
                    drawList,
                    anchor.ScreenPosition,
                    radius,
                    captionGap,
                    $"ICE {FakeWord(tell.IceFake)}",
                    tell.IceFake);
                labelAnchor = anchor.ScreenPosition;
                hasLabelAnchor = true;
            }
        }

        if (tell.HasThunder)
        {
            var anchor = projection.Project(
                arena.SimulatorToWorld(new Vector2(spread, 0), ground + lightningHeight));
            if (anchor.Succeeded && anchor.InView)
            {
                DrawTellRow(
                    drawList,
                    anchor.ScreenPosition,
                    radius,
                    captionGap,
                    $"LIGHTNING {FakeWord(tell.ThunderFake)}",
                    tell.ThunderFake);

                // The caption rides above whichever badge is higher on screen,
                // which is normally lightning but need not be at every pitch.
                if (!hasLabelAnchor || anchor.ScreenPosition.Y < labelAnchor.Y)
                {
                    labelAnchor = anchor.ScreenPosition;
                    hasLabelAnchor = true;
                }
            }
        }

        if (!hasLabelAnchor)
        {
            return;
        }

        var labelSize = ImGui.CalcTextSize(tell.Label);
        DrawShadowedText(
            drawList,
            tell.Label,
            new Vector2(
                labelAnchor.X - (labelSize.X * 0.5f),
                labelAnchor.Y - radius - labelSize.Y - (8 * scale)),
            ImGui.GetColorU32(new Vector4(0.90f, 0.93f, 1.00f, 1)));
    }

    /// <summary>Centres a badge and its caption as one block on a projected anchor.</summary>
    private static void DrawTellRow(
        ImDrawListPtr drawList,
        Vector2 anchor,
        float radius,
        float captionGap,
        string caption,
        bool fake)
    {
        var groupWidth = (radius * 2) + captionGap + ImGui.CalcTextSize(caption).X;
        DrawTellBadge(
            drawList,
            new Vector2(anchor.X - (groupWidth * 0.5f) + radius, anchor.Y),
            radius,
            captionGap,
            caption,
            fake);
    }

    private static void DrawTellBadge(
        ImDrawListPtr drawList,
        Vector2 center,
        float radius,
        float captionGap,
        string caption,
        bool fake)
    {
        var accent = fake
            ? new Vector4(1.00f, 0.45f, 0.10f, 1)
            : new Vector4(0.25f, 0.60f, 1.00f, 1);
        var accentColor = ImGui.GetColorU32(accent);

        drawList.AddCircleFilled(
            center, radius, ImGui.GetColorU32(new Vector4(0.03f, 0.04f, 0.07f, 0.90f)), 32);
        drawList.AddCircle(center, radius, accentColor, 32, MathF.Max(2, radius * 0.16f));

        if (fake)
        {
            // A question mark is what the orb actually shows when an element is
            // inverted, so the badge rehearses the same read as the boss.
            var glyphSize = ImGui.CalcTextSize("?");
            DrawShadowedText(
                drawList,
                "?",
                center - (glyphSize * 0.5f),
                ImGui.GetColorU32(new Vector4(1, 1, 1, 1)));
        }
        else
        {
            drawList.AddCircleFilled(center, radius * 0.46f, accentColor, 32);
        }

        var captionSize = ImGui.CalcTextSize(caption);
        DrawShadowedText(
            drawList,
            caption,
            new Vector2(center.X + radius + captionGap, center.Y - (captionSize.Y * 0.5f)),
            accentColor);
    }

    private static string FakeWord(bool fake) => fake ? "FAKE" : "REAL";

    private static void DrawShadowedText(
        ImDrawListPtr drawList,
        string text,
        Vector2 position,
        uint color)
    {
        drawList.AddText(
            position + Vector2.One, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.9f)), text);
        drawList.AddText(position, color, text);
    }

    /// <summary>
    /// Emphasises where the player should be standing. The required-position
    /// circle is otherwise drawn in the same flat style as every hazard, which
    /// makes the one shape you need to act on the easiest one to lose.
    /// </summary>
    private void DrawDestinationGuide(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        ArenaShape destination,
        PlayerState player,
        double time,
        Configuration configuration)
    {
        var height = Math.Clamp(configuration.GroundHeightOffset, -2, 2);
        var thickness = Math.Clamp(configuration.LineThickness, 1, 8);
        var segments = Math.Clamp(configuration.CurveSegments, 8, 192);
        var radius = MathF.Max(destination.Radius, 0.5f);
        var arrived = player.IsValid
            && Vector2.Distance(player.ArenaPosition, destination.Origin) <= radius;

        // Pulsing separates the destination from the static telegraph fills. The
        // colour goes solid green on arrival so "am I there yet" is answerable
        // without reading the distance.
        var pulse = 0.55f + (0.45f * MathF.Sin((float)time * 5));
        var color = arrived
            ? ImGui.GetColorU32(new Vector4(0.20f, 0.95f, 0.45f, 0.95f))
            : ImGui.GetColorU32(new Vector4(0.35f, 1.00f, 0.85f, pulse));

        shapes.DrawEmphasisRing(
            drawList, arena, destination.Origin, radius, segments, height, color, thickness + 2);
        shapes.DrawEmphasisRing(
            drawList, arena, destination.Origin, radius * 0.45f, segments, height, color, thickness);

        if (!player.IsValid || arrived)
        {
            return;
        }

        var toDestination = destination.Origin - player.ArenaPosition;
        var distance = toDestination.Length();
        var direction = Geometry.SafeNormalize(toDestination);

        if (configuration.ShowDestinationPath)
        {
            // Stop at the ring edge rather than the centre so the arrowhead does
            // not sit on top of the inner ring.
            var tip = destination.Origin - (direction * radius);
            DrawArenaSegment(
                drawList, arena, player.ArenaPosition, tip, height, color, thickness + 1);
            foreach (var sign in stackalloc[] { -1f, 1f })
            {
                var barb = Geometry.RotateDegrees(-direction, sign * 30);
                DrawArenaSegment(
                    drawList, arena, tip, tip + (barb * 1.8f), height, color, thickness + 1);
            }
        }

        if (configuration.ShowDestinationDistance)
        {
            var midpoint = player.ArenaPosition + (direction * distance * 0.5f);
            shapes.DrawWorldLabel(
                drawList, arena, midpoint, $"{distance:0.0}m", height, color);
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
