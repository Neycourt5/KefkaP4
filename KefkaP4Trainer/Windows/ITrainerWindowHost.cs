using System.Numerics;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Healing;
using KefkaP4Trainer.Rendering;

namespace KefkaP4Trainer.Windows;

/// <summary>
/// Narrow bridge between the Windowing/ImGui layer and the plugin lifetime.
/// Plugin implements this interface so windows do not depend on its private
/// fields or on game services directly.
/// </summary>
internal interface ITrainerWindowHost
{
    Configuration Configuration { get; }

    SimulationEngine Engine { get; }

    ArenaTransform Transform { get; }

    PlayerState LastPlayer { get; }

    Vector3? LastPlayerWorldPosition { get; }

    float? LastPlayerGameRotation { get; }

    bool MainWindowVisible { get; set; }

    bool ConfigWindowVisible { get; set; }

    bool DebugWindowVisible { get; set; }

    bool HealerWindowVisible { get; set; }

    /// <summary>The simulated party and healer-observation pipeline.</summary>
    HealerPracticeService HealerPractice { get; }

    /// <summary>Status icon resolution detail, for the debug atlas.</summary>
    StatusIconProvider StatusIcons { get; }

    /// <summary>
    /// Developer-spawned ghosts used to verify projection and facing in game.
    /// These are drawn alongside encounter ghosts but never graded.
    /// </summary>
    IReadOnlyList<SimulatedGhost> TestGhosts { get; }

    /// <summary>Number of projections that failed during the last frame.</summary>
    int LastProjectionFailureCount { get; }

    void AddTestGhost(Vector2 arenaPosition, bool gazeSource);

    void ClearTestGhosts();

    bool Start();

    void Pause();

    void Resume();

    void Stop();

    void Reset();

    /// <summary>
    /// Snapshots an arena using the requested anchor and the north mode in
    /// <see cref="Configuration"/>. Returns false when the requested game
    /// object is unavailable.
    /// </summary>
    bool SetArena(ArenaAnchorMode anchorMode);

    void SetSeed(long seed);

    void RandomizeSeed();

    void ClearHistory();

    void SaveConfiguration();

    /// <summary>
    /// Plays a cue immediately and reports which addon carried it, or null if
    /// none could be reached.
    /// </summary>
    /// <remarks>
    /// Sound playback cannot be verified from outside the game, so settings
    /// offers a test that separates "no addon available" from "played, but you
    /// heard nothing" — the latter being a volume or sound-index problem.
    /// </remarks>
    string? TestSoundCue(int soundIndex);
}
