using System.Numerics;

namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

/// <summary>
/// Cursed Shriek gaze rules, ported from p4_seq.gd.
///
/// Source: <c>check_if_facing()</c> compares the player's model rotation to the
/// bearing of the target and treats the player as facing it when the absolute
/// angular difference is strictly below <c>GAZE_MIN_ANGLE</c> (45 degrees).
/// <c>GAZE_MAX_ANGLE</c> (315) is only the wrap-around companion of the same
/// 45-degree half-cone, not a second threshold.
///
/// Source: <c>shriek_1_hit()</c> / <c>shriek_2_hit()</c> combine the two
/// carriers asymmetrically:
///   real  -> fail when the player faces EITHER carrier;
///   fake  -> fail when the player fails to face EITHER carrier,
///            i.e. both must be inside the 45-degree cone at once.
/// Distance never affects the rule, and no positional check is combined with
/// the gaze at these two resolution times.
/// </summary>
public static class KefkaP4Gaze
{
    /// <summary>Half-angle of the facing cone, from <c>GAZE_MIN_ANGLE</c>.</summary>
    public const float ThresholdDegrees = 45f;

    public const string LookedAtReason = "looked at Cursed Shriek";

    public const string DidNotLookReason = "failed to look at Cursed Shriek (Fake)";

    /// <summary>
    /// Builds the diagnostic for a single gaze source. <paramref name="fake"/>
    /// selects which outcome counts as a pass for that source.
    /// </summary>
    public static GazeDiagnostic Evaluate(
        string ghostId,
        Vector2 playerPosition,
        Vector2 playerFacing,
        Vector2 ghostPosition,
        bool fake)
    {
        var toward = ghostPosition - playerPosition;
        var direction = Geometry.SafeNormalize(toward);
        var facing = Geometry.SafeNormalize(playerFacing);

        // Grading uses the same predicate as every other ported mechanic so the
        // diagnostic can never disagree with the recorded pass/fail.
        var lookedToward = Geometry.IsFacing(
            playerPosition,
            playerFacing,
            ghostPosition,
            ThresholdDegrees);

        var dot = Math.Clamp(Vector2.Dot(facing, direction), -1f, 1f);
        var angleDegrees = MathF.Acos(dot) * (180f / MathF.PI);

        return new GazeDiagnostic(
            ghostId,
            playerPosition,
            ghostPosition,
            facing,
            direction,
            dot,
            angleDegrees,
            ThresholdDegrees,
            lookedToward,
            fake ? lookedToward : !lookedToward);
    }

    /// <summary>
    /// Applies the source's multi-source combination rule. Returns the failure
    /// reason, or <see langword="null"/> when the resolution passed.
    /// </summary>
    public static string? Combine(IReadOnlyList<GazeDiagnostic> diagnostics, bool fake)
    {
        if (diagnostics.Count == 0)
        {
            return null;
        }

        foreach (var diagnostic in diagnostics)
        {
            if (!diagnostic.Passed)
            {
                return fake ? DidNotLookReason : LookedAtReason;
            }
        }

        return null;
    }
}
