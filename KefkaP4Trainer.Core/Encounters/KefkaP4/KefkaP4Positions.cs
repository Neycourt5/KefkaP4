using System.Numerics;

namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

public static class KefkaP4Positions
{
    private static readonly Vector2 SpreadOne = new(0.3f, 0.2f);
    private static readonly Vector2 SpreadTwo = new(0.15f, -0.28f);
    private static readonly Vector2 SpreadThree = new(-0.2f, 0.1f);

    public static Vector2 Initial(PartyRole role) => role switch
    {
        PartyRole.T1 => new(0, -5),
        PartyRole.T2 => new(0, 5),
        PartyRole.H1 => new(-5, 0),
        PartyRole.H2 => new(5, 0),
        PartyRole.M1 => new(-5, 5),
        PartyRole.M2 => new(5, 5),
        PartyRole.R1 => new(-5, -5),
        PartyRole.R2 => new(5, -5),
        _ => Vector2.Zero,
    };

    public static Vector2 Magic(
        PartyRole role,
        bool northSouthIce,
        bool westSafe,
        bool outer,
        float rotationDegrees)
    {
        Vector2 center;
        if (northSouthIce)
        {
            center = outer
                ? new Vector2(westSafe ? -17 : 17, 0)
                : new Vector2(westSafe ? -3 : 3, 0);
        }
        else
        {
            center = outer
                ? new Vector2(westSafe ? -6.5f : 6.5f, 15.7f)
                : new Vector2(westSafe ? -2.3f : 2.3f, 5.5f);
        }

        return Geometry.RotateDegrees(center + StandardSpread(role), rotationDegrees);
    }

    public static Vector2 Flood(PartyRole role, bool west, float rotationDegrees)
    {
        var center = new Vector2(west ? -9 : 9, 0);
        return Geometry.RotateDegrees(center + StandardSpread(role), rotationDegrees);
    }

    public static Vector2 ShortDebuff(AssignmentKind kind, bool isDps, bool fake) =>
        (kind, isDps, fake) switch
        {
            (AssignmentKind.ShortAcceleration, true, _) => new(0, 25),
            (AssignmentKind.LongAcceleration, true, _) => new(0, 25),
            (AssignmentKind.ShortAcceleration, false, _) => new(0, -25),
            (AssignmentKind.LongAcceleration, false, _) => new(0, -25),
            (AssignmentKind.Water, true, false) => new(0, 25),
            (AssignmentKind.Water, true, true) => new(25, 0),
            (AssignmentKind.Lightning, true, false) => new(25, 0),
            (AssignmentKind.Lightning, true, true) => new(0, 25),
            (AssignmentKind.Water, false, false) => new(0, -25),
            (AssignmentKind.Water, false, true) => new(-25, 0),
            (AssignmentKind.Lightning, false, false) => new(-25, 0),
            (AssignmentKind.Lightning, false, true) => new(0, -25),
            _ => Vector2.Zero,
        };

    public static Vector2 ThrummingThunder(PartyRole role, bool west, float rotationDegrees)
    {
        var center = role.IsDps()
            ? new Vector2(west ? -4 : 4, 8)
            : new Vector2(west ? -4 : 4, -8);
        return Geometry.RotateDegrees(center + StandardSpread(role), rotationDegrees);
    }

    public static Vector2 ShriekOne(bool support, bool west, float rotationDegrees)
    {
        var position = (support, west) switch
        {
            (true, true) => new Vector2(-2, -3),
            (false, true) => new Vector2(-2, 3),
            (true, false) => new Vector2(2, -3),
            _ => new Vector2(2, 3),
        };
        return Geometry.RotateDegrees(position, rotationDegrees);
    }

    public static Vector2 Center(PartyRole role) => role switch
    {
        PartyRole.T1 => new(0, -0.2f),
        PartyRole.T2 => new(0, 0.2f),
        PartyRole.H1 => new(-0.2f, 0),
        PartyRole.H2 => new(0.2f, 0),
        PartyRole.M1 => new(-0.2f, 0.2f),
        PartyRole.M2 => new(0.2f, 0.2f),
        PartyRole.R1 => new(-0.2f, -0.2f),
        PartyRole.R2 => new(0.2f, -0.2f),
        _ => Vector2.Zero,
    };

    public static Vector2 InfernoDodge(PartyRole role)
    {
        var center = role.IsDps() ? new Vector2(0, 19) : new Vector2(0, -19);
        return center + StandardSpread(role);
    }

    public static Vector2 LongDebuff(
        AssignmentKind kind,
        bool isDps,
        bool fake,
        bool northEastSouthWest)
    {
        var offset = northEastSouthWest ? -4f : 4f;
        var waterDps = new Vector2(offset, 25);
        var waterSupport = new Vector2(-offset, -25);
        var lightDps = new Vector2(25, offset);
        var lightSupport = new Vector2(-25, -offset);

        return (kind, isDps, fake) switch
        {
            (AssignmentKind.ShortAcceleration, true, _) => waterDps,
            (AssignmentKind.LongAcceleration, true, _) => waterDps,
            (AssignmentKind.ShortAcceleration, false, _) => waterSupport,
            (AssignmentKind.LongAcceleration, false, _) => waterSupport,
            (AssignmentKind.Water, true, false) => waterDps,
            (AssignmentKind.Water, true, true) => lightDps,
            (AssignmentKind.Lightning, true, false) => lightDps,
            (AssignmentKind.Lightning, true, true) => waterDps,
            (AssignmentKind.Water, false, false) => waterSupport,
            (AssignmentKind.Water, false, true) => lightSupport,
            (AssignmentKind.Lightning, false, false) => lightSupport,
            (AssignmentKind.Lightning, false, true) => waterSupport,
            _ => Vector2.Zero,
        };
    }

    public static Vector2 ShriekTwo(PartyRole role) =>
        ThrummingThunder(role, true, 0);

    private static Vector2 StandardSpread(PartyRole role) => role switch
    {
        PartyRole.T1 => SpreadOne,
        PartyRole.T2 => SpreadTwo,
        PartyRole.H1 => SpreadThree,
        PartyRole.H2 => Vector2.Zero,
        PartyRole.M1 => -SpreadOne,
        PartyRole.M2 => -SpreadTwo,
        PartyRole.R1 => -SpreadThree,
        PartyRole.R2 => Vector2.Zero,
        _ => Vector2.Zero,
    };
}
