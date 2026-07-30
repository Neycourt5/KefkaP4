using System.Numerics;

namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

public static class KefkaP4Constants
{
    public const double EncounterLength = 125;
    public const float ArenaRadius = 47;

    public const float MagicLineWidth = 23.5f;
    public const float MagicLineLength = 100;
    public const float MagicConeAngle = 127.5f;
    public const float MagicConeLength = 100;
    public const double MagicHitLifetime = 0.4;

    public const float WaterRadius = 25;
    public const double WaterLifetime = 0.5;
    public const float TwisterInnerRadius = 11.5f;
    public const float TwisterOuterRadius = 60;
    public const double TwisterLifetime = 0.8;

    public static readonly Vector2 WestLineOneOrigin =
        new(MagicLineWidth * -1.5f, MagicLineLength * -0.5f);

    public static readonly Vector2 WestLineOneTarget =
        new(MagicLineWidth * -1.5f, 0);

    public static readonly Vector2 WestLineTwoOrigin =
        new(MagicLineWidth * 0.5f, MagicLineLength * -0.5f);

    public static readonly Vector2 WestLineTwoTarget =
        new(MagicLineWidth * 0.5f, 0);

    public static readonly Vector2 EastLineOneOrigin =
        new(MagicLineWidth * -0.5f, MagicLineLength * -0.5f);

    public static readonly Vector2 EastLineOneTarget =
        new(MagicLineWidth * -0.5f, 0);

    public static readonly Vector2 EastLineTwoOrigin =
        new(MagicLineWidth * 1.5f, MagicLineLength * -0.5f);

    public static readonly Vector2 EastLineTwoTarget =
        new(MagicLineWidth * 1.5f, 0);

    public static readonly IReadOnlyDictionary<DebuffKind, int> DebuffOrder =
        new Dictionary<DebuffKind, int>
        {
            [DebuffKind.BlackWound] = 0,
            [DebuffKind.WhiteWound] = 1,
            [DebuffKind.BeyondDeath] = 2,
            [DebuffKind.AllaganField] = 3,
            [DebuffKind.CursedShriek] = 4,
            [DebuffKind.CompressedWater] = 5,
            [DebuffKind.ForkedLightning] = 6,
            [DebuffKind.AccelerationBomb] = 7,
            [DebuffKind.Entropy] = 8,
            [DebuffKind.DynamicFluid] = 9,
        };
}

