using Dalamud.Configuration;
using KefkaP4Trainer.Core;

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

    // Thunder and ice telegraphs otherwise share the phase palette, leaving shape
    // (line versus cone) as the only thing telling the two elements apart.
    public bool ElementColoredTelegraphs { get; set; } = true;

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

    public bool MainWindowVisibleAfterStart { get; set; }

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
