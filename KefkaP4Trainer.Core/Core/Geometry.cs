using System.Numerics;

namespace KefkaP4Trainer.Core;

public static class Geometry
{
    public const float Epsilon = 0.0001f;

    public static Vector2 Rotate(Vector2 value, float radians)
    {
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        return new Vector2(
            (value.X * cosine) - (value.Y * sine),
            (value.X * sine) + (value.Y * cosine));
    }

    public static Vector2 RotateDegrees(Vector2 value, float degrees) =>
        Rotate(value, degrees * (MathF.PI / 180f));

    public static bool InCircle(Vector2 point, Vector2 center, float radius) =>
        Vector2.DistanceSquared(point, center) <= (radius * radius) + Epsilon;

    public static bool InDonut(Vector2 point, Vector2 center, float innerRadius, float outerRadius)
    {
        var distanceSquared = Vector2.DistanceSquared(point, center);
        return distanceSquared > (innerRadius * innerRadius) + Epsilon
            && distanceSquared <= (outerRadius * outerRadius) + Epsilon;
    }

    public static bool InForwardRectangle(
        Vector2 point,
        Vector2 origin,
        Vector2 direction,
        float width,
        float length)
    {
        direction = SafeNormalize(direction);
        var relative = point - origin;
        var forward = Vector2.Dot(relative, direction);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var lateral = Vector2.Dot(relative, perpendicular);
        return forward >= -Epsilon
            && forward <= length + Epsilon
            && MathF.Abs(lateral) <= (width / 2f) + Epsilon;
    }

    public static bool InCone(
        Vector2 point,
        Vector2 origin,
        Vector2 direction,
        float angleDegrees,
        float length)
    {
        var relative = point - origin;
        direction = SafeNormalize(direction);
        var forward = Vector2.Dot(relative, direction);
        if (length <= 0
            || forward < -Epsilon
            || forward > length + Epsilon)
        {
            return false;
        }

        // Waju builds the collision from a PrismMesh triangle. PrismMesh.size.X
        // is the full base width, and Waju assigns tan(configuredHalfAngle) *
        // length to it. Therefore the collision is a triangle whose base
        // half-width is half that value, not a circular sector with the
        // configured angle.
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var lateral = MathF.Abs(Vector2.Dot(relative, perpendicular));
        var baseHalfWidth =
            MathF.Tan(angleDegrees * (MathF.PI / 360f)) * length / 2f;
        var allowedHalfWidth = baseHalfWidth * MathF.Max(0, forward) / length;
        return lateral <= allowedHalfWidth + Epsilon;
    }

    public static bool IsFacing(
        Vector2 position,
        Vector2 forward,
        Vector2 target,
        float halfAngleDegrees = 45f)
    {
        var toward = target - position;
        if (toward.LengthSquared() <= Epsilon || forward.LengthSquared() <= Epsilon)
        {
            return false;
        }

        var cosine = Vector2.Dot(Vector2.Normalize(forward), Vector2.Normalize(toward));
        // Waju's comparisons are strict: exactly 45 degrees is not facing.
        // One very small comparison tolerance absorbs the float normalization
        // error at the boundary without swallowing a meaningfully inside angle.
        const float comparisonTolerance = 0.000001f;
        return cosine
            > MathF.Cos(halfAngleDegrees * (MathF.PI / 180f)) + comparisonTolerance;
    }

    /// <summary>
    /// Arena-space facing angle to a unit direction. Angle 0 points toward +Y
    /// (simulator south) and increases the same way <see cref="Rotate"/> turns.
    /// </summary>
    public static Vector2 DirectionFromAngle(float radians) =>
        new(MathF.Sin(radians), MathF.Cos(radians));

    /// <summary>Inverse of <see cref="DirectionFromAngle"/>.</summary>
    public static float AngleFromDirection(Vector2 direction) =>
        MathF.Atan2(direction.X, direction.Y);

    public static Vector2 SafeNormalize(Vector2 value) =>
        value.LengthSquared() <= Epsilon ? new Vector2(0, -1) : Vector2.Normalize(value);
}
