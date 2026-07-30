using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using KefkaP4Trainer.Core;

namespace KefkaP4Trainer.Rendering;

/// <summary>
/// Draws simulated bots as camera-facing vector mannequins. Nothing here is a
/// game actor: every pixel is an ImGui primitive anchored to a projected
/// world position, and nothing in this class influences grading.
/// </summary>
internal sealed class GhostRenderer
{
    /// <summary>Mannequin height in yalms, used for the head anchor.</summary>
    private const float BodyHeightYalms = 2.0f;

    /// <summary>Ground ring radius in simulator units (~0.5 yalms).</summary>
    private const float GroundRingRadius = 1.2f;

    private const int GroundRingSegments = 24;

    private static readonly Vector3 GazeSourceColor = new(1.00f, 0.45f, 0.30f);
    private static readonly Vector3 BystanderColor = new(0.62f, 0.72f, 0.88f);
    private static readonly Vector3 OutlineColor = new(0.04f, 0.04f, 0.07f);

    private readonly ProjectionHelper projection;
    private readonly RenderExceptionLogger errors;
    private readonly Vector2[] ringScreen = new Vector2[GroundRingSegments];
    private readonly Vector2[] eyeScreen = new Vector2[24];

    public GhostRenderer(ProjectionHelper projection, RenderExceptionLogger errors)
    {
        this.projection = projection;
        this.errors = errors;
    }

    public void Draw(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        IReadOnlyList<SimulatedGhost> ghosts,
        double time,
        Configuration configuration)
    {
        if (ghosts.Count == 0 || !arena.IsInitialized)
        {
            return;
        }

        for (var index = 0; index < ghosts.Count; index++)
        {
            try
            {
                DrawGhost(drawList, arena, ghosts[index], time, configuration);
            }
            catch (Exception exception)
            {
                // One unprojectable ghost must never take down the frame.
                errors.Report(exception);
            }
        }
    }

    private void DrawGhost(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        SimulatedGhost ghost,
        double time,
        Configuration configuration)
    {
        var fade = ghost.AlphaAt(time, configuration.GhostFadeDuration);
        if (fade <= 0.001f)
        {
            return;
        }

        var alpha = fade * Math.Clamp(configuration.GhostOpacity, 0, 1);
        var scale = Math.Clamp(configuration.GhostSizeMultiplier, 0.25f, 4f);
        var groundHeight = Math.Clamp(configuration.GroundHeightOffset, -2, 2);
        var baseColor = ghost.IsGazeSource ? GazeSourceColor : BystanderColor;

        var footWorld = arena.SimulatorToWorld(ghost.ArenaPosition, groundHeight);
        var headWorld = arena.SimulatorToWorld(
            ghost.ArenaPosition,
            groundHeight + (BodyHeightYalms * scale));

        var foot = projection.Project(footWorld);
        var head = projection.Project(headWorld);

        if (configuration.ShowGhostGroundRings)
        {
            DrawGroundRing(drawList, arena, ghost, groundHeight, baseColor, alpha, configuration);
        }

        if (configuration.ShowGhostFacingArrows)
        {
            DrawFacingArrow(drawList, arena, ghost, groundHeight, baseColor, alpha, configuration);
        }

        if (!foot.Succeeded || !head.Succeeded)
        {
            return;
        }

        var axis = head.ScreenPosition - foot.ScreenPosition;
        var height = axis.Length();
        if (!float.IsFinite(height) || height < 4f)
        {
            // Too far away or degenerate on screen to be worth drawing.
            return;
        }

        var up = axis / height;
        var right = new Vector2(-up.Y, up.X);
        var top = head.ScreenPosition;

        if (configuration.ShowGhostMannequins)
        {
            DrawMannequin(drawList, top, up, right, height, baseColor, alpha, configuration);
        }

        if (configuration.ShowGazeEyes && ghost.IsGazeSource)
        {
            DrawGazeEye(drawList, Point(top, up, right, height, -0.20f, 0), height, alpha, time);
        }

        if (configuration.ShowGhostIds)
        {
            DrawLabel(
                drawList,
                Point(top, up, right, height, -0.42f, 0),
                ghost.Id,
                baseColor,
                alpha);
        }
    }

    /// <summary>
    /// Resolves a mannequin point. <paramref name="down"/> is a fraction of the
    /// projected body height measured downward from the head anchor, and
    /// <paramref name="lateral"/> is the same fraction sideways. Using the
    /// projected foot-to-head axis keeps the figure aligned with the camera.
    /// </summary>
    private static Vector2 Point(
        Vector2 top,
        Vector2 up,
        Vector2 right,
        float height,
        float down,
        float lateral) =>
        top - (up * (down * height)) + (right * (lateral * height));

    private void DrawMannequin(
        ImDrawListPtr drawList,
        Vector2 top,
        Vector2 up,
        Vector2 right,
        float height,
        Vector3 color,
        float alpha,
        Configuration configuration)
    {
        var thickness = Math.Clamp(configuration.LineThickness, 1, 8);
        var body = Pack(color, alpha);
        var outline = Pack(OutlineColor, alpha * 0.8f);
        var fill = Pack(color, alpha * 0.28f);

        var headRadius = height * 0.105f;
        var headCenter = Point(top, up, right, height, 0.105f, 0);
        var neck = Point(top, up, right, height, 0.23f, 0);
        var shoulderLeft = Point(top, up, right, height, 0.27f, -0.20f);
        var shoulderRight = Point(top, up, right, height, 0.27f, 0.20f);
        var hipLeft = Point(top, up, right, height, 0.60f, -0.11f);
        var hipRight = Point(top, up, right, height, 0.60f, 0.11f);
        var hipCenter = Point(top, up, right, height, 0.60f, 0);
        var handLeft = Point(top, up, right, height, 0.54f, -0.27f);
        var handRight = Point(top, up, right, height, 0.54f, 0.27f);
        var footLeft = Point(top, up, right, height, 1.00f, -0.13f);
        var footRight = Point(top, up, right, height, 1.00f, 0.13f);

        // Translucent torso, drawn first so the limb strokes sit on top.
        var torso = new[] { shoulderLeft, shoulderRight, hipRight, hipLeft };
        drawList.AddConvexPolyFilled(ref torso[0], torso.Length, fill);

        // Shadow pass gives the figure contrast against bright ground effects.
        var shadow = new Vector2(thickness * 0.6f, thickness * 0.6f);
        DrawSkeleton(drawList, shadow, outline, thickness + 1.5f);
        DrawSkeleton(drawList, Vector2.Zero, body, thickness);

        drawList.AddCircleFilled(headCenter, headRadius, fill);
        drawList.AddCircle(headCenter + shadow, headRadius, outline, 16, thickness + 1.5f);
        drawList.AddCircle(headCenter, headRadius, body, 16, thickness);

        void DrawSkeleton(ImDrawListPtr list, Vector2 offset, uint stroke, float width)
        {
            list.AddLine(headCenter + offset, neck + offset, stroke, width);
            list.AddLine(shoulderLeft + offset, shoulderRight + offset, stroke, width);
            list.AddLine(neck + offset, hipCenter + offset, stroke, width);
            list.AddLine(shoulderLeft + offset, handLeft + offset, stroke, width);
            list.AddLine(shoulderRight + offset, handRight + offset, stroke, width);
            list.AddLine(hipCenter + offset, footLeft + offset, stroke, width);
            list.AddLine(hipCenter + offset, footRight + offset, stroke, width);
        }
    }

    /// <summary>
    /// A vector eye: almond outline, filled pupil and a soft glow ring that
    /// pulses on simulation time so it stays in step with playback speed.
    /// </summary>
    private void DrawGazeEye(
        ImDrawListPtr drawList,
        Vector2 center,
        float height,
        float alpha,
        double time)
    {
        var halfWidth = height * 0.13f;
        var halfHeight = halfWidth * 0.62f;
        if (halfWidth < 2f)
        {
            return;
        }

        var pulse = 0.75f + (0.25f * MathF.Sin((float)time * 4f));
        var eyeColor = Pack(new Vector3(1.00f, 0.86f, 0.40f), alpha);
        var outline = Pack(OutlineColor, alpha * 0.85f);

        // Lens outline: y = +/- halfHeight * (1 - t^2) across t in [-1, 1].
        const int half = 12;
        for (var index = 0; index < half; index++)
        {
            var t = -1f + (2f * index / (half - 1f));
            var offset = halfHeight * (1f - (t * t));
            eyeScreen[index] = center + new Vector2(t * halfWidth, -offset);
            eyeScreen[(half * 2) - 1 - index] = center + new Vector2(t * halfWidth, offset);
        }

        drawList.AddPolyline(ref eyeScreen[0], half * 2, outline, ImDrawFlags.Closed, 3f);
        drawList.AddPolyline(ref eyeScreen[0], half * 2, eyeColor, ImDrawFlags.Closed, 1.8f);
        drawList.AddCircleFilled(center, halfHeight * 0.72f, eyeColor, 12);
        drawList.AddCircle(center, halfWidth * pulse, Pack(new Vector3(1, 0.55f, 0.25f), alpha * 0.55f), 16, 2f);
    }

    private void DrawGroundRing(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        SimulatedGhost ghost,
        float groundHeight,
        Vector3 color,
        float alpha,
        Configuration configuration)
    {
        var complete = true;
        for (var index = 0; index < GroundRingSegments; index++)
        {
            var angle = MathF.Tau * index / GroundRingSegments;
            var point = ghost.ArenaPosition
                + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * GroundRingRadius);
            var projected = projection.Project(arena.SimulatorToWorld(point, groundHeight));
            if (!projected.Succeeded)
            {
                complete = false;
                break;
            }

            ringScreen[index] = projected.ScreenPosition;
        }

        if (!complete)
        {
            return;
        }

        var ringAlpha = alpha * Math.Clamp(configuration.OutlineOpacity, 0, 1);
        drawList.AddConvexPolyFilled(ref ringScreen[0], GroundRingSegments, Pack(color, alpha * 0.18f));
        drawList.AddPolyline(
            ref ringScreen[0],
            GroundRingSegments,
            Pack(color, ringAlpha),
            ImDrawFlags.Closed,
            Math.Clamp(configuration.LineThickness, 1, 8));
    }

    private void DrawFacingArrow(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        SimulatedGhost ghost,
        float groundHeight,
        Vector3 color,
        float alpha,
        Configuration configuration)
    {
        var direction = Geometry.DirectionFromAngle(ghost.ArenaFacing);
        var tip = ghost.ArenaPosition + (direction * GroundRingRadius * 3f);
        var start = projection.Project(arena.SimulatorToWorld(ghost.ArenaPosition, groundHeight));
        var end = projection.Project(arena.SimulatorToWorld(tip, groundHeight));
        if (!start.Succeeded || !end.Succeeded)
        {
            return;
        }

        drawList.AddLine(
            start.ScreenPosition,
            end.ScreenPosition,
            Pack(color, alpha * Math.Clamp(configuration.OutlineOpacity, 0, 1)),
            Math.Clamp(configuration.LineThickness, 1, 8));
    }

    private static void DrawLabel(
        ImDrawListPtr drawList,
        Vector2 position,
        string text,
        Vector3 color,
        float alpha)
    {
        var size = ImGui.CalcTextSize(text);
        var origin = position - new Vector2(size.X * 0.5f, size.Y);
        drawList.AddText(origin + Vector2.One, Pack(Vector3.Zero, alpha * 0.9f), text);
        drawList.AddText(origin, Pack(color, alpha), text);
    }

    private static uint Pack(Vector3 color, float alpha) =>
        ImGui.GetColorU32(new Vector4(color, Math.Clamp(alpha, 0, 1)));
}
