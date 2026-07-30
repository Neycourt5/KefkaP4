using System.Numerics;

namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

public static class KefkaP4Mechanics
{
    public static IReadOnlyList<ArenaShape> CreateMagicShapes(
        MagicPattern pattern,
        bool resolved,
        bool includeLines,
        bool includeCones,
        double startsAt,
        double endsAt,
        string labelPrefix)
    {
        var shapes = new List<ArenaShape>(4);
        var lineWest = pattern.WestLines ^ (resolved && pattern.ThunderFake);
        var northSouth = pattern.NorthSouthIce ^ (resolved && pattern.IceFake);
        if (includeLines)
        {
            var originOne = lineWest
                ? KefkaP4Constants.WestLineOneOrigin
                : KefkaP4Constants.EastLineOneOrigin;
            var targetOne = lineWest
                ? KefkaP4Constants.WestLineOneTarget
                : KefkaP4Constants.EastLineOneTarget;
            var originTwo = lineWest
                ? KefkaP4Constants.WestLineTwoOrigin
                : KefkaP4Constants.EastLineTwoOrigin;
            var targetTwo = lineWest
                ? KefkaP4Constants.WestLineTwoTarget
                : KefkaP4Constants.EastLineTwoTarget;

            shapes.Add(Line(
                Geometry.RotateDegrees(originOne, pattern.RotationDegrees),
                Geometry.RotateDegrees(targetOne - originOne, pattern.RotationDegrees),
                startsAt,
                endsAt,
                $"{labelPrefix} (Line)",
                resolved));
            shapes.Add(Line(
                Geometry.RotateDegrees(originTwo, pattern.RotationDegrees),
                Geometry.RotateDegrees(targetTwo - originTwo, pattern.RotationDegrees),
                startsAt,
                endsAt,
                $"{labelPrefix} (Line)",
                resolved));
        }

        if (includeCones)
        {
            var directionOne = northSouth ? new Vector2(0, -1) : new Vector2(-1, 0);
            var directionTwo = northSouth ? new Vector2(0, 1) : new Vector2(1, 0);
            shapes.Add(Cone(
                Geometry.RotateDegrees(directionOne, pattern.RotationDegrees),
                startsAt,
                endsAt,
                $"{labelPrefix} (Cone)",
                resolved));
            shapes.Add(Cone(
                Geometry.RotateDegrees(directionTwo, pattern.RotationDegrees),
                startsAt,
                endsAt,
                $"{labelPrefix} (Cone)",
                resolved));
        }

        return shapes;
    }

    public static IReadOnlyList<ArenaShape> CreateManaReleaseShapes(
        MagicPattern sourcePattern,
        bool manaThunderFake,
        bool manaIceFake,
        double startsAt,
        double endsAt)
    {
        var effective = sourcePattern with
        {
            WestLines = sourcePattern.WestLines
                ^ (sourcePattern.ThunderFake != manaThunderFake),
            NorthSouthIce = sourcePattern.NorthSouthIce
                ^ (sourcePattern.IceFake != manaIceFake),
            ThunderFake = false,
            IceFake = false,
        };
        return CreateMagicShapes(
            effective,
            true,
            true,
            true,
            startsAt,
            endsAt,
            "Mana Release");
    }

    public static bool IsInsideArena(Vector2 position) =>
        Geometry.InCircle(position, Vector2.Zero, KefkaP4Constants.ArenaRadius);

    private static ArenaShape Line(
        Vector2 origin,
        Vector2 direction,
        double startsAt,
        double endsAt,
        string label,
        bool resolved) =>
        new()
        {
            Kind = ShapeKind.Rectangle,
            Label = label,
            Element = MagicElement.Thunder,
            Phase = resolved ? ShapePhase.Dangerous : ShapePhase.Upcoming,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Origin = origin,
            Direction = Geometry.SafeNormalize(direction),
            Width = KefkaP4Constants.MagicLineWidth,
            Length = KefkaP4Constants.MagicLineLength,
        };

    private static ArenaShape Cone(
        Vector2 direction,
        double startsAt,
        double endsAt,
        string label,
        bool resolved) =>
        new()
        {
            Kind = ShapeKind.Cone,
            Label = label,
            Element = MagicElement.Ice,
            Phase = resolved ? ShapePhase.Dangerous : ShapePhase.Upcoming,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Origin = Vector2.Zero,
            Direction = Geometry.SafeNormalize(direction),
            AngleDegrees = KefkaP4Constants.MagicConeAngle,
            Length = KefkaP4Constants.MagicConeLength,
        };
}

