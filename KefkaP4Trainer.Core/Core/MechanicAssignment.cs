namespace KefkaP4Trainer.Core;

public enum AssignmentKind
{
    ShortAcceleration,
    LongAcceleration,
    Water,
    Lightning,
}

public enum DebuffKind
{
    BlackWound,
    WhiteWound,
    BeyondDeath,
    AllaganField,
    CursedShriek,
    CompressedWater,
    ForkedLightning,
    AccelerationBomb,
    Entropy,
    DynamicFluid,
}

public sealed record SimulatedDebuff(
    PartyRole Role,
    DebuffKind Kind,
    string Name,
    string ShortLabel,
    double AppliedAt,
    double ExpiresAt,
    int Stacks = 1)
{
    public double RemainingAt(double time) =>
        double.IsPositiveInfinity(ExpiresAt) ? double.PositiveInfinity : Math.Max(0, ExpiresAt - time);

    public bool IsActiveAt(double time) => time >= AppliedAt && time < ExpiresAt;
}

