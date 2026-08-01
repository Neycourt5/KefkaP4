using Dalamud.Configuration;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Health;

namespace KefkaP4Trainer;

public enum ArenaAnchorMode
{
    Target,
    Player,
    Manual,
}

public enum ArenaNorthMode
{
    WorldNorth,
    PlayerFacing,
    Manual,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public long Seed { get; set; } = 8675309;

    public PartyRole PlayerRole { get; set; } = PartyRole.M1;

    public float PlaybackSpeed { get; set; } = 1;

    public float CountdownSeconds { get; set; } = 3;

    public bool NewSeedEveryPull { get; set; }

    public FailureBehavior FailureBehavior { get; set; } = FailureBehavior.Continue;

    public ArenaAnchorMode AnchorMode { get; set; } = ArenaAnchorMode.Target;

    public ArenaNorthMode NorthMode { get; set; } = ArenaNorthMode.WorldNorth;

    public float ManualCenterX { get; set; } = 100;

    public float ManualCenterY { get; set; }

    public float ManualCenterZ { get; set; } = 100;

    public float ManualRotationDegrees { get; set; }

    public bool OverlayEnabled { get; set; } = true;

    public float FillOpacity { get; set; } = 0.24f;

    public float OutlineOpacity { get; set; } = 0.9f;

    public float LineThickness { get; set; } = 2;

    public int CurveSegments { get; set; } = 48;

    public bool ShowSafeZones { get; set; }

    // Destination guidance. Unlike the gaze aids below this is on by default:
    // it emphasises a position the encounter already knows, and without it the
    // required-position circle is drawn identically to every hazard telegraph.
    public bool ShowDestinationGuide { get; set; } = true;

    public bool ShowDestinationPath { get; set; } = true;

    public bool ShowDestinationDistance { get; set; } = true;

    // Real/fake badges over the boss. On by default: the telegraphs for a real
    // and a fake cast are drawn identically, so without this the element flips
    // are unreadable rather than merely difficult.
    public bool ShowMagicTell { get; set; } = true;

    public float MagicTellScale { get; set; } = 1;

    // Badge heights in yalms above the arena floor. The two badges are projected
    // from separate world points rather than stacked in screen space, so the gap
    // holds as the camera pitches and orbits.
    //
    // Tuned against the game, not against the source scene. The scene rides its
    // rings at y=7.23 and y=3.58, but those are set on its own Kefka model, and
    // the arena scale matching says nothing about model scale: transplanted
    // directly, lightning floats well above anything you anchor on. Ice sits
    // deliberately low so it reads at the shins, with the pair about four yalms
    // apart. Both are sliders; the right value depends on what you anchored to.
    public float MagicTellIceHeight { get; set; } = 0.4f;

    public float MagicTellLightningHeight { get; set; } = 4.2f;

    // Simulator units applied in opposite directions on the arena X axis, for
    // camera angles where the badges would otherwise line up behind each other.
    public float MagicTellHorizontalSpread { get; set; }

    // Thunder and ice telegraphs otherwise share the phase palette, leaving shape
    // (line versus cone) as the only thing telling the two elements apart.
    public bool ElementColoredTelegraphs { get; set; } = true;

    // Flips which Antilight is drawn purple and which blue. FloodColors infers
    // Black -> purple, White -> blue, because Waju's textures are absent from
    // the reference clone and the pixels could not be checked from source.
    // Observed in game to be the other way round, so this defaults on; the
    // inference and the correction stay separable rather than being quietly
    // folded together.
    public bool SwapAntilightColors { get; set; } = true;

    /// <summary>Draw a stand-in for Neo Exdeath and its two Antilight banners.</summary>
    public bool ShowFloodStage { get; set; } = true;

    public float FloodStageScale { get; set; } = 1;

    public bool ShowArenaBoundary { get; set; } = true;

    public bool ShowDebugCoordinateLabels { get; set; }

    public float TelegraphLeadTime { get; set; } = 4.7f;

    public float GroundHeightOffset { get; set; } = 0.05f;

    // Ghost stand-ins. Defaults favour readability over realism.
    public bool ShowGhostMannequins { get; set; } = true;

    public bool ShowGhostGroundRings { get; set; } = true;

    public bool ShowGazeEyes { get; set; } = true;

    public bool ShowGhostFacingArrows { get; set; }

    public bool ShowGhostIds { get; set; }

    public float GhostOpacity { get; set; } = 0.9f;

    public float GhostSizeMultiplier { get; set; } = 1;

    public float GhostFadeDuration { get; set; } = 0.5f;

    // Explicit gaze solution aids. Off by default so normal practice does not
    // reveal more than the browser simulator does.
    public bool ShowPlayerFacingArrow { get; set; }

    public bool ShowGazeLines { get; set; }

    public bool ShowGazeThresholdCone { get; set; }

    public bool ShowGazeAngles { get; set; }

    public bool StatusHudVisible { get; set; } = true;

    public bool StatusHudLocked { get; set; } = true;

    public float StatusHudX { get; set; } = 560;

    public float StatusHudY { get; set; } = 240;

    public float StatusHudScale { get; set; } = 1;

    public float StatusIconSpacing { get; set; } = 4;

    public bool StatusTimerText { get; set; } = true;

    public bool SortStatusesLikeSource { get; set; } = true;

    public bool ShowStatusDebugValues { get; set; }

    public bool UseGameStatusIcons { get; set; } = true;

    // Cast bars, defaulted to the right of a 1080p screen and clamped to the
    // display at draw time so an odd resolution cannot strand them off-screen.
    public bool ShowCastBars { get; set; } = true;

    public float CastBarX { get; set; } = 1500;

    public float CastBarY { get; set; } = 300;

    public float CastBarScale { get; set; } = 1;

    // Built-in alert indices, matching the <se.N> chat macros. The real ability
    // audio is not reachable: it lives in .scd files inside the game archives.
    public bool PlaySoundCues { get; set; }

    public int CastStartSound { get; set; } = 1;

    public int CastFinishSound { get; set; } = 4;

    // Mitigation alerts. The seat is detected from the job where that is
    // unambiguous; the tank and melee pairs need these two answers.
    public bool ShowMitigationAlerts { get; set; } = true;

    public bool PlayerIsMainTank { get; set; } = true;

    public bool PlayerIsFirstMelee { get; set; } = true;

    public float MitigationLeadTime { get; set; } = 6;

    public float MitigationX { get; set; } = 760;

    public float MitigationY { get; set; } = 640;

    public float MitigationScale { get; set; } = 1;

    public bool ShowCountdown { get; set; } = true;

    public float CountdownScale { get; set; } = 1;

    public float CountdownHeightFraction { get; set; } = 0.35f;

    public bool MainWindowVisibleAfterStart { get; set; }

    // Healer practice. Off by default: a DPS practising movement should never
    // have a simulated party quietly taking damage behind the overlay.
    public bool HealerPracticeEnabled { get; set; }

    /// <summary>Whether the scripted damage table fires during a pull.</summary>
    public bool HealerDamageEnabled { get; set; } = true;

    /// <summary>
    /// Maximum HP every simulated slot is given. Damage is normalised against
    /// the reference pool the calibration figures were taken at, so changing
    /// this keeps each mechanic's severity constant.
    /// </summary>
    public int HealerSimulatedMaximumHp { get; set; } = 148_000;

    /// <summary>
    /// How much the simulated co-healer does. Standard covers alternate
    /// raidwides, leaving the ones in between to the real healer.
    /// </summary>
    public CoHealerAssistance CoHealerAssistance { get; set; } = CoHealerAssistance.Standard;

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
