using System.Numerics;
using Dalamud.Plugin.Services;

namespace KefkaP4Trainer.Rendering;

internal readonly record struct ProjectionResult(
    Vector2 ScreenPosition,
    bool Succeeded,
    bool InView)
{
    public static ProjectionResult Failed => new(Vector2.Zero, false, false);
}

internal sealed class ProjectionHelper
{
    private const float MaximumScreenCoordinate = 1_000_000;

    private readonly IGameGui gameGui;
    private readonly RenderExceptionLogger errors;

    private int failureCount;

    public ProjectionHelper(IGameGui gameGui, RenderExceptionLogger errors)
    {
        this.gameGui = gameGui;
        this.errors = errors;
    }

    /// <summary>Projection failures recorded since the last <see cref="BeginFrame"/>.</summary>
    public int FailureCount => failureCount;

    public void BeginFrame() => failureCount = 0;

    public ProjectionResult Project(Vector3 worldPosition) =>
        Project(worldPosition, recordFailure: true);

    /// <summary>
    /// Projects without recording a failure, for callers that deliberately test
    /// points expected to be unprojectable.
    /// </summary>
    /// <remarks>
    /// Edge clipping bisects across the projection boundary, so roughly half its
    /// probes fail by design and would otherwise swamp <see cref="FailureCount"/>.
    /// </remarks>
    public ProjectionResult Probe(Vector3 worldPosition) =>
        Project(worldPosition, recordFailure: false);

    private ProjectionResult Project(Vector3 worldPosition, bool recordFailure)
    {
        if (!IsFinite(worldPosition))
        {
            if (recordFailure)
            {
                failureCount++;
            }

            return ProjectionResult.Failed;
        }

        try
        {
            var succeeded = gameGui.WorldToScreen(
                worldPosition,
                out var screenPosition,
                out var inView);
            if (!succeeded || !IsUsableScreenPosition(screenPosition))
            {
                failureCount++;
                return ProjectionResult.Failed;
            }

            return new ProjectionResult(screenPosition, true, inView);
        }
        catch (Exception exception)
        {
            // Projection is performed point-by-point. Treat a bad point as
            // unprojectable so the rest of the frame can still be rendered.
            errors.Report(exception);
            failureCount++;
            return ProjectionResult.Failed;
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X)
        && float.IsFinite(value.Y)
        && float.IsFinite(value.Z);

    private static bool IsUsableScreenPosition(Vector2 value) =>
        float.IsFinite(value.X)
        && float.IsFinite(value.Y)
        && MathF.Abs(value.X) <= MaximumScreenCoordinate
        && MathF.Abs(value.Y) <= MaximumScreenCoordinate;
}
