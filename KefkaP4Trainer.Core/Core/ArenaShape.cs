using System.Numerics;

namespace KefkaP4Trainer.Core;

public enum ShapeKind
{
    Circle,
    Donut,
    Rectangle,
    Cone,
    PlayerMarker,
    RequiredPosition,
    ArenaBoundary,
    DirectionArrow,
}

public enum ShapePhase
{
    Upcoming,
    Dangerous,
    Required,
    Information,
    Success,
    Failure,
}

/// <summary>
/// Which element a telegraph belongs to. Thunder is cast as the lines and ice as
/// the cones, so the two are otherwise distinguishable only by shape.
/// </summary>
public enum MagicElement
{
    None,
    Thunder,
    Ice,
}

public sealed record ArenaShape
{
    public required ShapeKind Kind { get; init; }

    public required string Label { get; init; }

    public required ShapePhase Phase { get; init; }

    /// <summary>
    /// Element used to hue the telegraph. Defaults to <see cref="MagicElement.None"/>
    /// so non-elemental shapes keep the phase palette.
    /// </summary>
    public MagicElement Element { get; init; }

    public required double StartsAt { get; init; }

    public required double EndsAt { get; init; }

    public Vector2 Origin { get; init; }

    public Vector2 Direction { get; init; } = new(0, -1);

    public float Radius { get; init; }

    public float InnerRadius { get; init; }

    public float Width { get; init; }

    public float Length { get; init; }

    public float AngleDegrees { get; init; }

    public bool Contains(Vector2 point) => Kind switch
    {
        ShapeKind.Circle => Geometry.InCircle(point, Origin, Radius),
        ShapeKind.Donut => Geometry.InDonut(point, Origin, InnerRadius, Radius),
        ShapeKind.Rectangle => Geometry.InForwardRectangle(point, Origin, Direction, Width, Length),
        ShapeKind.Cone => Geometry.InCone(point, Origin, Direction, AngleDegrees, Length),
        _ => false,
    };
}

