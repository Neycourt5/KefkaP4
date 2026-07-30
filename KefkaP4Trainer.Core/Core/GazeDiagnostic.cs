using System.Numerics;

namespace KefkaP4Trainer.Core;

/// <summary>
/// Everything used to decide one gaze source's outcome, retained so the debug
/// window can show exactly why a resolution passed or failed.
/// </summary>
public sealed record GazeDiagnostic(
    string GhostId,
    Vector2 PlayerPosition,
    Vector2 GhostPosition,
    Vector2 PlayerFacing,
    Vector2 DirectionToGhost,
    float DotProduct,
    float AngleDegrees,
    float ThresholdDegrees,
    bool LookedToward,
    bool Passed);
