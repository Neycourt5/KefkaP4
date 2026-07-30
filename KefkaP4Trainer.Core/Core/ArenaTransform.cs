using System.Numerics;

namespace KefkaP4Trainer.Core;

/// <summary>
/// Converts Waju DMU simulator X/Y (Godot X/Z) to FFXIV world X/Z.
/// Positive rotation is clockwise geographically because +Y/+Z is south.
/// </summary>
public sealed class ArenaTransform
{
    public const float DmuScale = 2.3f;

    public Vector3 Center { get; private set; }

    public float RotationRadians { get; private set; }

    public bool IsInitialized { get; private set; }

    public float RotationDegrees => RotationRadians * (180f / MathF.PI);

    public void Set(Vector3 center, float clockwiseRotationRadians)
    {
        Center = center;
        RotationRadians = NormalizeRadians(clockwiseRotationRadians);
        IsInitialized = true;
    }

    public Vector3 SimulatorToWorld(Vector2 simulator, float heightOffset = 0.05f)
    {
        var rotated = Geometry.Rotate(simulator, RotationRadians) / DmuScale;
        return new Vector3(Center.X + rotated.X, Center.Y + heightOffset, Center.Z + rotated.Y);
    }

    public Vector2 WorldToSimulator(Vector3 world)
    {
        var delta = new Vector2(world.X - Center.X, world.Z - Center.Z) * DmuScale;
        return Geometry.Rotate(delta, -RotationRadians);
    }

    public Vector2 WorldDirectionToSimulator(Vector2 worldDirection) =>
        Geometry.SafeNormalize(Geometry.Rotate(worldDirection, -RotationRadians));

    public static float RotationFromFfxivFacing(float gameRotationRadians)
    {
        // FFXIV rotation 0 faces world south (+Z). Rotating simulator north
        // (0,-1) by PI-gameRotation aligns it with that facing direction.
        return NormalizeRadians(MathF.PI - gameRotationRadians);
    }

    public static Vector2 FfxivForward(float gameRotationRadians) =>
        Geometry.SafeNormalize(new Vector2(MathF.Sin(gameRotationRadians), MathF.Cos(gameRotationRadians)));

    private static float NormalizeRadians(float radians)
    {
        if (!float.IsFinite(radians))
        {
            throw new ArgumentOutOfRangeException(
                nameof(radians),
                "Arena rotation must be finite.");
        }

        var normalized = MathF.IEEERemainder(radians, MathF.Tau);
        if (normalized <= -MathF.PI)
        {
            normalized += MathF.Tau;
        }

        return normalized;
    }
}
