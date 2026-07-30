using System.Numerics;

namespace KefkaP4Trainer.Core;

public sealed record SimulationResult(
    int PullNumber,
    long Seed,
    string Mechanic,
    double Timestamp,
    bool Passed,
    string Reason,
    Vector2 PlayerArenaPosition)
{
    /// <summary>Player facing at the moment the mechanic resolved.</summary>
    public Vector2 PlayerFacing { get; init; } = new(0, -1);

    /// <summary>Per-source gaze detail; empty for non-gaze mechanics.</summary>
    public IReadOnlyList<GazeDiagnostic> GazeDiagnostics { get; init; } = [];
}
