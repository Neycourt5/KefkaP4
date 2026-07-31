using System.Numerics;
using Dalamud.Bindings.ImGui;
using KefkaP4Trainer.Core;

namespace KefkaP4Trainer.Rendering;

internal sealed class ShapeRenderer
{
    private static readonly Vector3 UpcomingColor = new(1.00f, 0.68f, 0.12f);
    private static readonly Vector3 DangerColor = new(1.00f, 0.20f, 0.18f);

    // Element telegraphs carry their hue from the element and their urgency from
    // brightness, so thunder and ice stay apart without losing the "this is live
    // now" read that the shared orange/red pair provided.
    private static readonly Vector3 ThunderUpcomingColor = new(0.58f, 0.36f, 0.92f);
    private static readonly Vector3 ThunderDangerColor = new(0.85f, 0.28f, 1.00f);
    private static readonly Vector3 IceUpcomingColor = new(0.26f, 0.62f, 0.96f);
    private static readonly Vector3 IceDangerColor = new(0.36f, 0.90f, 1.00f);
    private static readonly Vector3 RequiredColor = new(0.20f, 0.95f, 0.55f);
    private static readonly Vector3 InformationColor = new(0.35f, 0.68f, 1.00f);

    // The two Flood Antilights. In game these read as a blue and a purple
    // telegraph, so they are drawn as such rather than as "black" and "white",
    // which no player can match to what is on screen.
    private static readonly Vector3 AntilightPurple = new(0.64f, 0.33f, 0.95f);
    private static readonly Vector3 AntilightBlue = new(0.22f, 0.62f, 1.00f);
    private static readonly Vector3 SuccessColor = new(0.20f, 0.90f, 0.35f);
    private static readonly Vector3 FailureColor = new(1.00f, 0.15f, 0.55f);

    private readonly ProjectionHelper projection;
    private Vector2[] arenaPoints = new Vector2[64];
    private Vector2[] screenPoints = new Vector2[64];
    private ProjectionResult[] projectedPoints = new ProjectionResult[64];

    // Clipping can introduce a boundary vertex per crossed edge, so this needs
    // room for twice the source point count.
    private Vector2[] clippedPoints = new Vector2[128];

    public ShapeRenderer(ProjectionHelper projection)
    {
        this.projection = projection;
    }

    public void Draw(
        ImDrawListPtr background,
        ImDrawListPtr foreground,
        ArenaTransform arena,
        ArenaShape shape,
        Configuration configuration)
    {
        if (shape.Phase == ShapePhase.Required && !configuration.ShowSafeZones)
        {
            return;
        }

        var color = ColorFor(shape, configuration);
        var fillOpacity = Math.Clamp(configuration.FillOpacity, 0, 1);
        if (shape.Phase == ShapePhase.Information)
        {
            fillOpacity *= 0.65f;
        }

        var fillColor = Pack(color, fillOpacity);
        var outlineColor = Pack(color, Math.Clamp(configuration.OutlineOpacity, 0, 1));
        var thickness = Math.Clamp(configuration.LineThickness, 0.5f, 12);
        var segments = Math.Clamp(configuration.CurveSegments, 8, 192);
        var heightOffset = Math.Clamp(configuration.GroundHeightOffset, -2, 2);

        switch (shape.Kind)
        {
            case ShapeKind.Circle:
            case ShapeKind.PlayerMarker:
            case ShapeKind.RequiredPosition:
                DrawCircle(
                    background,
                    arena,
                    shape.Origin,
                    MathF.Max(shape.Radius, 0.5f),
                    segments,
                    heightOffset,
                    fillColor,
                    outlineColor,
                    thickness);
                break;
            case ShapeKind.Donut:
                DrawDonut(
                    background,
                    arena,
                    shape.Origin,
                    shape.InnerRadius,
                    shape.Radius,
                    segments,
                    heightOffset,
                    fillColor,
                    outlineColor,
                    thickness);
                break;
            case ShapeKind.Rectangle:
                DrawRectangle(
                    background,
                    arena,
                    shape,
                    heightOffset,
                    fillColor,
                    outlineColor,
                    thickness);
                break;
            case ShapeKind.Cone:
                DrawCone(
                    background,
                    arena,
                    shape,
                    heightOffset,
                    fillColor,
                    outlineColor,
                    thickness);
                break;
            case ShapeKind.ArenaBoundary:
                DrawCircle(
                    background,
                    arena,
                    shape.Origin,
                    shape.Radius,
                    segments,
                    heightOffset,
                    0,
                    outlineColor,
                    thickness);
                break;
            case ShapeKind.DirectionArrow:
                DrawDirectionArrow(
                    background,
                    arena,
                    shape,
                    heightOffset,
                    fillColor,
                    outlineColor,
                    thickness);
                break;
        }

        if (configuration.ShowDebugCoordinateLabels)
        {
            var text = $"{shape.Label} ({shape.Origin.X:0.0}, {shape.Origin.Y:0.0})";
            DrawWorldLabel(foreground, arena, shape.Origin, text, heightOffset, outlineColor);
        }
    }

    public void DrawMarker(
        ImDrawListPtr background,
        ImDrawListPtr foreground,
        ArenaTransform arena,
        Vector2 position,
        string label,
        float radius,
        ShapePhase phase,
        Configuration configuration,
        bool alwaysLabel)
    {
        var marker = new ArenaShape
        {
            Kind = ShapeKind.PlayerMarker,
            Label = label,
            Phase = phase,
            StartsAt = 0,
            EndsAt = double.PositiveInfinity,
            Origin = position,
            Radius = radius,
        };
        Draw(background, foreground, arena, marker, configuration);

        if (alwaysLabel && !configuration.ShowDebugCoordinateLabels)
        {
            var color = Pack(
                ColorFor(marker, configuration),
                Math.Clamp(configuration.OutlineOpacity, 0, 1));
            DrawWorldLabel(
                foreground,
                arena,
                position,
                label,
                Math.Clamp(configuration.GroundHeightOffset, -2, 2),
                color);
        }
    }

    /// <summary>
    /// Draws an unfilled ring at an explicit colour and thickness, bypassing the
    /// phase palette and the <see cref="Configuration.ShowSafeZones"/> gate that
    /// <see cref="Draw"/> applies. Used to pick the destination out from the
    /// hazard telegraphs it sits among.
    /// </summary>
    public void DrawEmphasisRing(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        Vector2 center,
        float radius,
        int segments,
        float heightOffset,
        uint color,
        float thickness) =>
        DrawCircle(
            drawList,
            arena,
            center,
            radius,
            Math.Clamp(segments, 8, 192),
            heightOffset,
            0,
            color,
            thickness);

    public void DrawWorldLabel(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        Vector2 arenaPosition,
        string text,
        float heightOffset,
        uint color)
    {
        var projected = projection.Project(arena.SimulatorToWorld(arenaPosition, heightOffset));
        if (!projected.Succeeded || !projected.InView)
        {
            return;
        }

        var textSize = ImGui.CalcTextSize(text);
        var position = projected.ScreenPosition - new Vector2(textSize.X * 0.5f, textSize.Y + 4);
        drawList.AddText(position + Vector2.One, Pack(Vector3.Zero, 0.9f), text);
        drawList.AddText(position, color, text);
    }

    private void DrawCircle(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        Vector2 center,
        float radius,
        int segments,
        float heightOffset,
        uint fillColor,
        uint outlineColor,
        float thickness)
    {
        if (!float.IsFinite(radius) || radius <= Geometry.Epsilon)
        {
            return;
        }

        EnsureCapacity(segments);
        for (var index = 0; index < segments; index++)
        {
            var angle = MathF.Tau * index / segments;
            arenaPoints[index] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }

        ProjectPoints(arena, segments, heightOffset);
        DrawClippedPolygon(
            drawList, arena, segments, heightOffset, fillColor, outlineColor, thickness);
    }

    private void DrawDonut(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        Vector2 center,
        float innerRadius,
        float outerRadius,
        int segments,
        float heightOffset,
        uint fillColor,
        uint outlineColor,
        float thickness)
    {
        if (!float.IsFinite(innerRadius)
            || !float.IsFinite(outerRadius)
            || innerRadius <= Geometry.Epsilon
            || outerRadius <= innerRadius + Geometry.Epsilon)
        {
            return;
        }

        EnsureCapacity(segments * 2);
        for (var index = 0; index < segments; index++)
        {
            var angle = MathF.Tau * index / segments;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            arenaPoints[index] = center + direction * outerRadius;
            arenaPoints[segments + index] = center + direction * innerRadius;
        }

        ProjectPoints(arena, segments * 2, heightOffset);
        if (fillColor != 0)
        {
            for (var index = 0; index < segments; index++)
            {
                var next = (index + 1) % segments;
                var outerA = projectedPoints[index];
                var outerB = projectedPoints[next];
                var innerB = projectedPoints[segments + next];
                var innerA = projectedPoints[segments + index];
                if (outerA.Succeeded
                    && outerB.Succeeded
                    && innerB.Succeeded
                    && innerA.Succeeded)
                {
                    drawList.AddQuadFilled(
                        outerA.ScreenPosition,
                        outerB.ScreenPosition,
                        innerB.ScreenPosition,
                        innerA.ScreenPosition,
                        fillColor);
                }
            }
        }

        DrawClosedOutline(drawList, 0, segments, outlineColor, thickness);
        DrawClosedOutline(drawList, segments, segments, outlineColor, thickness);
    }

    private void DrawRectangle(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        ArenaShape shape,
        float heightOffset,
        uint fillColor,
        uint outlineColor,
        float thickness)
    {
        if (!float.IsFinite(shape.Width)
            || !float.IsFinite(shape.Length)
            || shape.Width <= Geometry.Epsilon
            || shape.Length <= Geometry.Epsilon)
        {
            return;
        }

        var direction = Geometry.SafeNormalize(shape.Direction);
        var side = new Vector2(-direction.Y, direction.X) * (shape.Width * 0.5f);
        var end = shape.Origin + direction * shape.Length;
        EnsureCapacity(4);
        arenaPoints[0] = shape.Origin - side;
        arenaPoints[1] = shape.Origin + side;
        arenaPoints[2] = end + side;
        arenaPoints[3] = end - side;
        ProjectPoints(arena, 4, heightOffset);
        DrawClippedPolygon(drawList, arena, 4, heightOffset, fillColor, outlineColor, thickness);
    }

    private void DrawCone(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        ArenaShape shape,
        float heightOffset,
        uint fillColor,
        uint outlineColor,
        float thickness)
    {
        if (!float.IsFinite(shape.AngleDegrees)
            || !float.IsFinite(shape.Length)
            || shape.AngleDegrees <= Geometry.Epsilon
            || shape.Length <= Geometry.Epsilon)
        {
            return;
        }

        // Waju constructs this collision/visual from a triangular PrismMesh.
        // Its configured tangent width is the full base width, so reproduce
        // that triangle rather than drawing a conventional circular sector.
        var clampedAngle = Math.Clamp(shape.AngleDegrees, 1, 179);
        var direction = Geometry.SafeNormalize(shape.Direction);
        var side = new Vector2(-direction.Y, direction.X);
        var end = shape.Origin + direction * shape.Length;
        var baseHalfWidth =
            MathF.Tan(clampedAngle * (MathF.PI / 360f)) * shape.Length / 2f;
        EnsureCapacity(3);
        arenaPoints[0] = shape.Origin;
        arenaPoints[1] = end + side * baseHalfWidth;
        arenaPoints[2] = end - side * baseHalfWidth;
        ProjectPoints(arena, 3, heightOffset);
        DrawClippedPolygon(drawList, arena, 3, heightOffset, fillColor, outlineColor, thickness);
    }

    private void DrawDirectionArrow(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        ArenaShape shape,
        float heightOffset,
        uint fillColor,
        uint outlineColor,
        float thickness)
    {
        var length = shape.Length > Geometry.Epsilon ? shape.Length : 8;
        var width = shape.Width > Geometry.Epsilon ? shape.Width : 2.5f;
        if (!float.IsFinite(length) || !float.IsFinite(width))
        {
            return;
        }

        var direction = Geometry.SafeNormalize(shape.Direction);
        var side = new Vector2(-direction.Y, direction.X);
        var shaftEnd = shape.Origin + direction * (length * 0.65f);
        var tip = shape.Origin + direction * length;

        EnsureCapacity(7);
        arenaPoints[0] = shape.Origin - side * width * 0.2f;
        arenaPoints[1] = shape.Origin + side * width * 0.2f;
        arenaPoints[2] = shaftEnd + side * width * 0.2f;
        arenaPoints[3] = shaftEnd - side * width * 0.2f;
        arenaPoints[4] = shaftEnd - side * width * 0.55f;
        arenaPoints[5] = shaftEnd + side * width * 0.55f;
        arenaPoints[6] = tip;
        ProjectPoints(arena, 7, heightOffset);

        DrawProjectedPolygon(drawList, 0, 4, fillColor, outlineColor, thickness);
        DrawProjectedPolygon(drawList, 4, 3, fillColor, outlineColor, thickness);
    }

    private void DrawProjectedPolygon(
        ImDrawListPtr drawList,
        int offset,
        int count,
        uint fillColor,
        uint outlineColor,
        float thickness)
    {
        var complete = fillColor != 0;
        for (var index = 0; index < count; index++)
        {
            complete &= projectedPoints[offset + index].Succeeded;
        }

        if (complete)
        {
            for (var index = 0; index < count; index++)
            {
                screenPoints[index] = projectedPoints[offset + index].ScreenPosition;
            }

            drawList.AddConvexPolyFilled(ref screenPoints[0], count, fillColor);
        }

        DrawClosedOutline(drawList, offset, count, outlineColor, thickness);
    }

    /// <summary>
    /// Fills and outlines the current arena points, clipping any edge that leaves
    /// the projectable region rather than discarding it.
    /// </summary>
    /// <remarks>
    /// The game cannot project a point behind the camera. Treating that as "skip
    /// the vertex" removed the whole fill and both adjoining edges, so a cone
    /// angled towards the camera disappeared instead of being cut off.
    /// </remarks>
    private void DrawClippedPolygon(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        int count,
        float heightOffset,
        uint fillColor,
        uint outlineColor,
        float thickness)
    {
        var clipped = BuildClippedPolygon(arena, count, heightOffset);
        if (clipped < 2)
        {
            return;
        }

        if (fillColor != 0 && clipped >= 3)
        {
            drawList.AddConvexPolyFilled(ref clippedPoints[0], clipped, fillColor);
        }

        if (outlineColor == 0)
        {
            return;
        }

        for (var index = 0; index < clipped; index++)
        {
            var next = (index + 1) % clipped;
            drawList.AddLine(clippedPoints[index], clippedPoints[next], outlineColor, thickness);
        }
    }

    private int BuildClippedPolygon(ArenaTransform arena, int count, float heightOffset)
    {
        var clipped = 0;
        for (var index = 0; index < count; index++)
        {
            var next = (index + 1) % count;
            var current = projectedPoints[index];
            var following = projectedPoints[next];

            if (current.Succeeded)
            {
                clippedPoints[clipped++] = current.ScreenPosition;
            }

            // Exactly one end projected, so the edge crosses the boundary and the
            // crossing point becomes a vertex of the clipped polygon.
            if (current.Succeeded != following.Succeeded
                && TryFindBoundary(
                    arena,
                    arenaPoints[current.Succeeded ? index : next],
                    arenaPoints[current.Succeeded ? next : index],
                    heightOffset,
                    out var boundary))
            {
                clippedPoints[clipped++] = boundary;
            }
        }

        return clipped;
    }

    private bool TryFindBoundary(
        ArenaTransform arena,
        Vector2 inside,
        Vector2 outside,
        float heightOffset,
        out Vector2 screenPosition)
    {
        // Bisect for the last point on the edge that still projects. Twelve steps
        // resolves the crossing far finer than a pixel at any raid distance.
        var good = inside;
        var bad = outside;
        var result = projection.Probe(arena.SimulatorToWorld(good, heightOffset));
        for (var step = 0; step < 12; step++)
        {
            var middle = (good + bad) * 0.5f;
            var candidate = projection.Probe(arena.SimulatorToWorld(middle, heightOffset));
            if (candidate.Succeeded)
            {
                good = middle;
                result = candidate;
            }
            else
            {
                bad = middle;
            }
        }

        screenPosition = result.ScreenPosition;
        return result.Succeeded;
    }

    private void ProjectPoints(ArenaTransform arena, int count, float heightOffset)
    {
        for (var index = 0; index < count; index++)
        {
            projectedPoints[index] =
                projection.Project(arena.SimulatorToWorld(arenaPoints[index], heightOffset));
            screenPoints[index] = projectedPoints[index].ScreenPosition;
        }
    }

    private void DrawClosedOutline(
        ImDrawListPtr drawList,
        int offset,
        int count,
        uint color,
        float thickness)
    {
        if (color == 0)
        {
            return;
        }

        for (var index = 0; index < count; index++)
        {
            var next = (index + 1) % count;
            var first = projectedPoints[offset + index];
            var second = projectedPoints[offset + next];
            if (first.Succeeded && second.Succeeded)
            {
                drawList.AddLine(first.ScreenPosition, second.ScreenPosition, color, thickness);
            }
        }
    }

    private void EnsureCapacity(int count)
    {
        if (arenaPoints.Length >= count)
        {
            return;
        }

        var capacity = Math.Max(count, arenaPoints.Length * 2);
        Array.Resize(ref arenaPoints, capacity);
        Array.Resize(ref screenPoints, capacity);
        Array.Resize(ref projectedPoints, capacity);
        Array.Resize(ref clippedPoints, capacity * 2);
    }

    private static Vector3 ColorFor(ArenaShape shape, Configuration configuration)
    {
        // Only the two live phases are recoloured. A resolved-into-required or
        // information shape keeps its palette so the phase language stays intact.
        if (configuration.ElementColoredTelegraphs
            && shape.Element != MagicElement.None
            && shape.Phase is ShapePhase.Upcoming or ShapePhase.Dangerous)
        {
            var dangerous = shape.Phase == ShapePhase.Dangerous;
            return shape.Element switch
            {
                MagicElement.Thunder => dangerous ? ThunderDangerColor : ThunderUpcomingColor,
                MagicElement.Ice => dangerous ? IceDangerColor : IceUpcomingColor,
                _ => dangerous ? DangerColor : UpcomingColor,
            };
        }

        if (shape.Phase == ShapePhase.Information)
        {
            // Antilight labels lead with the resolved colour word, so the drawn
            // colour cannot drift from what the cue and debug panel say.
            if (shape.Label.StartsWith("PURPLE", StringComparison.Ordinal))
            {
                return AntilightPurple;
            }

            if (shape.Label.StartsWith("BLUE", StringComparison.Ordinal))
            {
                return AntilightBlue;
            }
        }

        return shape.Phase switch
        {
            ShapePhase.Upcoming => UpcomingColor,
            ShapePhase.Dangerous => DangerColor,
            ShapePhase.Required => RequiredColor,
            ShapePhase.Information => InformationColor,
            ShapePhase.Success => SuccessColor,
            ShapePhase.Failure => FailureColor,
            _ => InformationColor,
        };
    }

    private static uint Pack(Vector3 color, float opacity) =>
        ImGui.GetColorU32(new Vector4(color, Math.Clamp(opacity, 0, 1)));
}
