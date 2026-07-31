namespace KefkaP4Trainer.Core.Health;

/// <summary>A percentage damage reduction running on one member.</summary>
/// <remarks>
/// Fractions stack multiplicatively, matching the game: two 20% effects leave
/// 0.8 * 0.8 = 64% of the damage, not 60%.
/// </remarks>
public sealed class ActiveMitigation
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Reduction as a fraction in [0, 0.95].</summary>
    public required float Fraction { get; init; }

    public required double AppliedAt { get; init; }

    public required double ExpiresAt { get; init; }

    public required string Source { get; init; }

    public bool IsActiveAt(double time) => time >= AppliedAt && time < ExpiresAt;

    public double RemainingAt(double time) =>
        double.IsPositiveInfinity(ExpiresAt) ? double.PositiveInfinity : Math.Max(0, ExpiresAt - time);
}

/// <summary>An absorption shield. Consumed before HP is lost.</summary>
public sealed class ActiveShield
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required int InitialAmount { get; init; }

    public required double AppliedAt { get; init; }

    public required double ExpiresAt { get; init; }

    public required string Source { get; init; }

    /// <summary>Absorption left. Falls to zero as the shield is consumed.</summary>
    public int Remaining { get; set; }

    public bool IsActiveAt(double time) =>
        Remaining > 0 && time >= AppliedAt && time < ExpiresAt;
}

/// <summary>A periodic heal.</summary>
/// <remarks>
/// Ticks are driven off simulation time rather than wall time, so regens follow
/// playback speed and stop while the clock is paused, matching every other
/// timed thing in the trainer.
/// </remarks>
public sealed class ActiveRegen
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required int AmountPerTick { get; init; }

    public required double IntervalSeconds { get; init; }

    public required double AppliedAt { get; init; }

    public required double ExpiresAt { get; init; }

    public required string Source { get; init; }

    /// <summary>Simulation time the next tick is due.</summary>
    public double NextTickAt { get; set; }

    public bool IsActiveAt(double time) => time >= AppliedAt && time < ExpiresAt;
}
