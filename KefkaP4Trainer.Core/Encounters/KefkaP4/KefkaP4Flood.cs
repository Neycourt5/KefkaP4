using System.Numerics;

namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

/// <summary>Which Wound debuff a slot carries.</summary>
public enum WoundType
{
    Black,
    White,
}

/// <summary>The second Grand Cross 3 debuff, which decides whether the side swaps.</summary>
public enum SecondaryDebuffType
{
    AllaganField,
    BeyondDeath,
}

public enum FloodTruthState
{
    Real,
    Fake,
}

/// <summary>The canonical name of an Antilight, as the source and the game use it.</summary>
public enum AntilightType
{
    Black,
    White,
}

/// <summary>
/// What an Antilight actually looks like on screen.
/// </summary>
/// <remarks>
/// Deliberately a separate concept from <see cref="AntilightType"/>. The
/// mechanic is named in black/white but is presented in colour, and conflating
/// the two is precisely what makes this mechanic hard to read: a player looks at
/// a purple telegraph and is told to stand in "White".
/// </remarks>
public enum FloodVisualColor
{
    Purple,
    Blue,
}

public enum ArenaSide
{
    West,
    East,
}

/// <summary>
/// Maps canonical Antilight names onto the colours drawn for them.
/// </summary>
/// <remarks>
/// <b>Inferred, not proven.</b> Waju paints both Antilight quads with the same
/// purple emission and distinguishes them only by texture
/// (<c>flood1.png</c> for White, <c>flood2.png</c> for Black), and the
/// <c>assets/</c> directory is absent from the reference clone, so the actual
/// pixels cannot be inspected from source. The mapping below follows the
/// obvious reading — Black is the darker purple, White is the lighter blue —
/// and matches the colours this plugin already used for the two halves.
/// <para>
/// If a look in game shows the opposite, flip
/// <c>Configuration.SwapAntilightColors</c>; nothing else needs to change,
/// because every consumer reads the colour from here.
/// </para>
/// </remarks>
public static class FloodColors
{
    public static FloodVisualColor For(AntilightType antilight, bool swapped)
    {
        var natural = antilight == AntilightType.Black
            ? FloodVisualColor.Purple
            : FloodVisualColor.Blue;
        return swapped ? Opposite(natural) : natural;
    }

    public static FloodVisualColor Opposite(FloodVisualColor color) =>
        color == FloodVisualColor.Purple ? FloodVisualColor.Blue : FloodVisualColor.Purple;

    public static ArenaSide Opposite(ArenaSide side) =>
        side == ArenaSide.West ? ArenaSide.East : ArenaSide.West;

    public static AntilightType Opposite(AntilightType antilight) =>
        antilight == AntilightType.Black ? AntilightType.White : AntilightType.Black;
}

/// <summary>
/// Everything about one slot's Flood of Naughts answer, resolved once.
/// </summary>
/// <remarks>
/// <para>
/// The cue, the overlay, the debug panel and the grader all read this object.
/// Deriving any part of it separately is what lets a rendered rectangle end up
/// on one side while the grader checks the other.
/// </para>
/// <para>Source, <c>p4_seq.gd</c> Grand Cross 3 setup:</para>
/// <code>
/// black wound: black-safe when (i >= 4) != flood_fake
/// white wound: black-safe when (i >= 4) == flood_fake
/// </code>
/// <para>
/// <c>i &gt;= 4</c> is membership of <c>death_keys</c>, i.e. Beyond Death.
/// GDScript comparisons are left-associative and do not chain, so
/// <c>i &gt;= 4 != flood_fake</c> is <c>(i &gt;= 4) != flood_fake</c>.
/// </para>
/// <para>
/// Reduces to: Allagan Field takes the opposite colour, Beyond Death takes the
/// same colour, and a fake Flood inverts both.
/// </para>
/// </remarks>
public sealed record FloodResolution
{
    public required WoundType Wound { get; init; }

    public required SecondaryDebuffType Secondary { get; init; }

    public required FloodTruthState Truth { get; init; }

    /// <summary>Which side the Black Antilight is on this pull.</summary>
    public required ArenaSide BlackAntilightSide { get; init; }

    /// <summary>Set when the plugin's colour assignment has been flipped.</summary>
    public bool SwappedColors { get; init; }

    public bool IsFake => Truth == FloodTruthState.Fake;

    /// <summary>
    /// True when the safe Antilight matches the player's own Wound colour.
    /// </summary>
    public bool SameColourRule =>
        (RequiredAntilight == AntilightType.Black) == (Wound == WoundType.Black);

    /// <summary>Membership of Waju's <c>black_safe_keys</c>.</summary>
    public bool StandsInBlack
    {
        get
        {
            var beyondDeath = Secondary == SecondaryDebuffType.BeyondDeath;
            return Wound == WoundType.Black
                ? beyondDeath != IsFake
                : beyondDeath == IsFake;
        }
    }

    public AntilightType RequiredAntilight =>
        StandsInBlack ? AntilightType.Black : AntilightType.White;

    public AntilightType OppositeAntilight => FloodColors.Opposite(RequiredAntilight);

    public ArenaSide RequiredSide =>
        RequiredAntilight == AntilightType.Black
            ? BlackAntilightSide
            : FloodColors.Opposite(BlackAntilightSide);

    public ArenaSide OppositeSide => FloodColors.Opposite(RequiredSide);

    public FloodVisualColor RequiredColor => FloodColors.For(RequiredAntilight, SwappedColors);

    public FloodVisualColor OppositeColor => FloodColors.For(OppositeAntilight, SwappedColors);

    public FloodVisualColor WoundColor => FloodColors.For(
        Wound == WoundType.Black ? AntilightType.Black : AntilightType.White,
        SwappedColors);

    /// <summary>Which Antilight sits on <paramref name="side"/> this pull.</summary>
    public AntilightType AntilightOn(ArenaSide side) =>
        side == BlackAntilightSide ? AntilightType.Black : AntilightType.White;

    public FloodVisualColor ColorOn(ArenaSide side) =>
        FloodColors.For(AntilightOn(side), SwappedColors);

    /// <summary>
    /// The on-screen cue. Colour and side lead, because those are what the
    /// player can actually see; the canonical name trails as confirmation.
    /// </summary>
    public string Cue =>
        $"Flood {Truth.ToString().ToUpperInvariant()}: {RequiredSide.ToString().ToUpperInvariant()} "
        + $"- {RequiredColor.ToString().ToUpperInvariant()} line ({RequiredAntilight} Antilight)";

    /// <summary>Why, in one line, for the debug panel.</summary>
    public string Explanation =>
        $"{Wound} Wound ({WoundColor}) + {Secondary} + {Truth} -> "
        + $"{(SameColourRule ? "SAME" : "OPPOSITE")} colour -> "
        + $"{RequiredAntilight} Antilight = {RequiredColor} on the {RequiredSide}. "
        + $"Black Antilight is {BlackAntilightSide} this pull.";

    /// <summary>
    /// Grades a position given in the Neo-local frame, where negative X is west.
    /// </summary>
    /// <remarks>
    /// Waju compares strictly against 0.0, so the dividing line itself fails
    /// neither side.
    /// </remarks>
    public FloodVerdict Grade(float neoLocalX)
    {
        var onCentre = MathF.Abs(neoLocalX) <= Geometry.Epsilon;
        ArenaSide? detected = onCentre
            ? null
            : neoLocalX < 0 ? ArenaSide.West : ArenaSide.East;

        var passed = detected is null || detected == RequiredSide;
        return new FloodVerdict(
            detected,
            detected is { } side ? ColorOn(side) : null,
            detected is { } named ? AntilightOn(named) : null,
            passed,
            passed
                ? null
                : $"needed {RequiredColor.ToString().ToUpperInvariant()} "
                    + $"{RequiredSide.ToString().ToUpperInvariant()} "
                    + $"({RequiredAntilight} Antilight), stood in "
                    + $"{ColorOn(detected!.Value).ToString().ToUpperInvariant()} "
                    + $"{detected.Value.ToString().ToUpperInvariant()}");
    }
}

/// <summary>The outcome of grading a position against a <see cref="FloodResolution"/>.</summary>
public sealed record FloodVerdict(
    ArenaSide? DetectedSide,
    FloodVisualColor? DetectedColor,
    AntilightType? DetectedAntilight,
    bool Passed,
    string? FailureReason)
{
    public bool OnCentreLine => DetectedSide is null;
}

/// <summary>
/// Where Waju parks Neo Exdeath and its two Antilight billboards for Flood.
/// </summary>
/// <remarks>
/// From <c>p4_seq.gd</c> <c>neo_move_fade_in</c>:
/// <c>NEO_EXDEATH_NORTH = Vector2(0, -47)</c> rotated by <c>neo_rotation_deg</c>,
/// which is the arena's north edge at the full 47-unit radius. The billboards
/// are children at local X +/-17 and Z +5 (<c>neo_exdeath.tscn</c>), and the
/// scene's own defaults place White at -17 and Black at +17, which is the
/// black-east case; <c>show_antilight</c> only moves them when black is west.
/// </remarks>
public static class FloodStage
{
    /// <summary>Neo's distance from the arena centre, in simulator units.</summary>
    public const float NeoDistance = 47f;

    /// <summary>Sideways offset of each Antilight billboard from Neo.</summary>
    public const float AntilightOffsetX = 17f;

    /// <summary>How far the billboards sit in front of Neo, toward the arena.</summary>
    public const float AntilightOffsetZ = 5f;

    public static Vector2 NeoPosition(float rotationDegrees) =>
        Geometry.RotateDegrees(new Vector2(0, -NeoDistance), rotationDegrees);

    /// <summary>Arena position of the billboard on <paramref name="side"/>.</summary>
    public static Vector2 AntilightPosition(ArenaSide side, float rotationDegrees)
    {
        var x = side == ArenaSide.West ? -AntilightOffsetX : AntilightOffsetX;
        return Geometry.RotateDegrees(
            new Vector2(x, -NeoDistance + AntilightOffsetZ),
            rotationDegrees);
    }
}

/// <summary>
/// What to draw for the Flood stage at a given moment.
/// </summary>
/// <remarks>
/// Neo fades in at 47.5 and the two Antilight billboards light up with the cast
/// at 49.7, matching <c>neo_move_fade_in</c> and <c>flood_cast</c>.
/// </remarks>
public sealed record FloodStageView(
    FloodResolution Resolution,
    float RotationDegrees,
    bool ShowAntilights)
{
    /// <summary>Neo's own arena position.</summary>
    public Vector2 NeoPosition => FloodStage.NeoPosition(RotationDegrees);

    public Vector2 BannerPosition(ArenaSide side) =>
        FloodStage.AntilightPosition(side, RotationDegrees);
}
