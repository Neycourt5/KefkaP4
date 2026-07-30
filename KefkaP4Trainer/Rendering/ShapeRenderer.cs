using System.Numerics;
using Dalamud.Bindings.ImGui;
using KefkaP4Trainer.Core;

namespace KefkaP4Trainer.Rendering;

internal sealed class ShapeRenderer
{
    private static readonly Vector3 UpcomingColor = new(1.00f, 0.68f, 0.12f);
    private static readonly Vector3 DangerColor = new(1.00f, 0.20f, 0.18f);
    private static readonly Vector3 RequiredColor = new(0.20f, 0.95f, 0.55f);
    private static readonly Vector3 InformationColor = new(0.35f, 0.68f, 1.00f);
    private static readonly Vector3 SuccessColor = new(0.20f, 0.90f, 0.35f);
    private static readonly Vector3 FailureColor = new(1.00f, 0.15f, 0.55f);

    private readonly ProjectionHelper projection;
    private Vector2[] arenaPoints = new Vector2[64];
    private Vector2[] screenPoints = new Vector2[64];
    private ProjectionResult[] projectedPoints = new ProjectionResult[64];

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

        var color = ColorFor(shape);
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
            var color = Pack(ColorFor(marker), Math.Clamp(configuration.OutlineOpacity, 0, 1));
            DrawWorldLabel(
                foreground,
                arena,
                position,
                label,
                Math.Clamp(configuration.GroundHeightOffset, -2, 2),
                color);
        }
    }

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
        FillConvexIfComplete(drawList, segments, fillColor);
        DrawClosedOutline(drawList, segments, outlineColor, thickness);
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
        FillConvexIfComplete(drawList, 4, fillColor);
        DrawClosedOutline(drawList, 4, outlineColor, thickness);
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
        FillConvexIfComplete(drawList, 3, fillColor);
        DrawClosedOutline(drawList, 3, outlineColor, thickness);
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

    private void ProjectPoints(ArenaTransform arena, int count, float heightOffset)
    {
        for (var index = 0; index < count; index++)
        {
            projectedPoints[index] =
                projection.Project(arena.SimulatorToWorld(arenaPoints[index], heightOffset));
            screenPoints[index] = projectedPoints[index].ScreenPosition;
        }
    }

    private void FillConvexIfComplete(ImDrawListPtr drawList, int count, uint color)
    {
        if (color == 0)
        {
            return;
        }

        for (var index = 0; index < count; index++)
        {
            if (!projectedPoints[index].Succeeded)
            {
                return;
            }
        }

        drawList.AddConvexPolyFilled(ref screenPoints[0], count, color);
    }

    private void DrawClosedOutline(
        ImDrawListPtr drawList,
        int count,
        uint color,
        float thickness) =>
        DrawClosedOutline(drawList, 0, count, color, thickness);

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
    }

    private static Vector3 ColorFor(ArenaShape shape)
    {
        if (shape.Phase == ShapePhase.Information)
        {
            if (shape.Label.Contains("Black", StringComparison.OrdinalIgnoreCase))
            {
                return new Vector3(0.48f, 0.35f, 0.72f);
            }

            if (shape.Label.Contains("White", StringComparison.OrdinalIgnoreCase))
            {
                return new Vector3(0.88f, 0.90f, 1.00f);
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
