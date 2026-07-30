using System.Numerics;

namespace KefkaP4Trainer.Core;

/// <summary>
/// How a ghost should be presented. The Waju source never varies the bot
/// appearance, so these states only drive plugin-side emphasis and never
/// affect grading.
/// </summary>
public enum GhostVisualState
{
    /// <summary>A simulated party member that carries no gaze this window.</summary>
    Bystander,

    /// <summary>A Cursed Shriek carrier that must be looked at or away from.</summary>
    GazeSource,
}

/// <summary>
/// A simulated stand-in for one of the Waju bots. Ghosts exist purely so the
/// plugin can show where a scripted party member is standing; the encounter
/// grades the real local player against <see cref="ArenaPosition"/>.
/// </summary>
public sealed record SimulatedGhost(
    string Id,
    Vector2 ArenaPosition,
    float ArenaFacing,
    double SpawnTime,
    double DespawnTime,
    bool IsGazeSource,
    GhostVisualState VisualState)
{
    public bool IsActiveAt(double time) => time >= SpawnTime && time < DespawnTime;

    /// <summary>
    /// Fade weight in [0, 1] driven purely by simulation time so playback speed
    /// and pausing behave correctly. Ghosts fade in after spawning and back out
    /// as they approach despawn.
    /// </summary>
    public float AlphaAt(double time, double fadeDuration)
    {
        if (!IsActiveAt(time))
        {
            return 0;
        }

        if (fadeDuration <= 0)
        {
            return 1;
        }

        var sinceSpawn = time - SpawnTime;
        var untilDespawn = DespawnTime - time;
        var weight = Math.Min(sinceSpawn, untilDespawn) / fadeDuration;
        return (float)Math.Clamp(weight, 0, 1);
    }
}
