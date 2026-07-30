using System.Numerics;

namespace KefkaP4Trainer.Core;

public readonly record struct PlayerState(
    bool IsValid,
    Vector2 ArenaPosition,
    Vector2 FacingDirection,
    float Speed)
{
    public static PlayerState Unavailable => new(false, Vector2.Zero, new Vector2(0, -1), 0);

    // Waju compares velocity.length_squared() strictly against 0.1.
    // Exactly 0.1 therefore satisfies both the fake and real boundary checks.
    public bool IsMoving => (Speed * Speed) > 0.1f;
}
