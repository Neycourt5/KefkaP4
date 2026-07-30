using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using KefkaP4Trainer.Core;

namespace KefkaP4Trainer.Windows;

internal sealed class ConfigWindow : Window
{
    private readonly ITrainerWindowHost host;
    private string? arenaMessage;

    public ConfigWindow(ITrainerWindowHost host)
        : base("Kefka P4 Trainer Settings###KefkaP4TrainerConfig")
    {
        this.host = host;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(510, 520),
            MaximumSize = new Vector2(760, 980),
        };
    }

    public override void Draw()
    {
        var changed = false;
        var config = host.Configuration;

        Section("Simulation");
        changed |= DrawSimulation(config);

        Section("Arena anchor and north");
        changed |= DrawArena(config);

        Section("Ground overlay");
        changed |= DrawOverlay(config);

        Section("Gaze ghosts");
        changed |= DrawGhosts(config);

        Section("Fake status HUD");
        changed |= DrawStatusHud(config);

        if (changed)
        {
            host.SaveConfiguration();
        }

        ImGui.Spacing();
        if (ImGui.Button("Save settings"))
        {
            host.SaveConfiguration();
        }

        ImGui.SameLine();
        if (ImGui.Button("Open main window"))
        {
            host.MainWindowVisible = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Open debug window"))
        {
            host.DebugWindowVisible = true;
        }
    }

    private bool DrawSimulation(Configuration config)
    {
        var changed = false;

        var role = config.PlayerRole;
        if (RoleCombo("Player role", ref role))
        {
            config.PlayerRole = role;
            host.Engine.SetPlayerRole(role);
            changed = true;
        }

        var speed = config.PlaybackSpeed;
        if (ImGui.SliderFloat("Playback speed", ref speed, 0.1f, 4, "%.1fx"))
        {
            config.PlaybackSpeed = speed;
            host.Engine.Clock.PlaybackSpeed = speed;
            changed = true;
        }

        var countdown = config.CountdownSeconds;
        if (ImGui.SliderFloat("Countdown before pull", ref countdown, 0, 15, "%.1f s"))
        {
            config.CountdownSeconds = countdown;
            changed = true;
        }

        var failure = config.FailureBehavior;
        if (EnumCombo("On failure", ref failure))
        {
            config.FailureBehavior = failure;
            host.Engine.FailureBehavior = failure;
            changed = true;
        }

        var newSeed = config.NewSeedEveryPull;
        if (ImGui.Checkbox("Generate a new seed every pull", ref newSeed))
        {
            config.NewSeedEveryPull = newSeed;
            changed = true;
        }

        var keepMainOpen = config.MainWindowVisibleAfterStart;
        if (ImGui.Checkbox("Keep main window visible after Start", ref keepMainOpen))
        {
            config.MainWindowVisibleAfterStart = keepMainOpen;
            changed = true;
        }

        ImGui.TextDisabled(
            "Only the source-faithful full sequence is currently available."
            + " Turn off new-seed mode to repeat the current pull.");
        return changed;
    }

    private bool DrawArena(Configuration config)
    {
        var changed = false;

        var anchor = config.AnchorMode;
        if (EnumCombo("Default anchor", ref anchor))
        {
            config.AnchorMode = anchor;
            changed = true;
        }

        var north = config.NorthMode;
        if (EnumCombo("Arena north", ref north))
        {
            config.NorthMode = north;
            changed = true;
        }

        var manualCenter = new Vector3(
            config.ManualCenterX,
            config.ManualCenterY,
            config.ManualCenterZ);
        if (ImGui.DragFloat3("Manual center X / Y / Z", ref manualCenter, 0.1f))
        {
            config.ManualCenterX = manualCenter.X;
            config.ManualCenterY = manualCenter.Y;
            config.ManualCenterZ = manualCenter.Z;
            changed = true;
        }

        var rotation = config.ManualRotationDegrees;
        if (ImGui.DragFloat("Manual clockwise north", ref rotation, 0.5f, -180, 180, "%.1f deg"))
        {
            config.ManualRotationDegrees = rotation;
            changed = true;
        }

        if (ImGui.Button("Set arena: target"))
        {
            arenaMessage = host.SetArena(ArenaAnchorMode.Target)
                ? "Arena snapped to target."
                : "No valid target was available.";
        }

        ImGui.SameLine();
        if (ImGui.Button("Set arena: player"))
        {
            arenaMessage = host.SetArena(ArenaAnchorMode.Player)
                ? "Arena snapped to player."
                : "No valid local player was available.";
        }

        ImGui.SameLine();
        if (ImGui.Button("Set arena: manual"))
        {
            arenaMessage = host.SetArena(ArenaAnchorMode.Manual)
                ? "Arena snapped to manual coordinates."
                : "Manual coordinates were invalid.";
        }

        if (arenaMessage is not null)
        {
            ImGui.TextWrapped(arenaMessage);
        }

        ImGui.TextDisabled(
            "Arena center and rotation are snapshots. They do not follow the player or target.");
        return changed;
    }

    private static bool DrawOverlay(Configuration config)
    {
        var changed = false;

        var enabled = config.OverlayEnabled;
        if (ImGui.Checkbox("Enable world overlay", ref enabled))
        {
            config.OverlayEnabled = enabled;
            changed = true;
        }

        var fill = config.FillOpacity;
        if (ImGui.SliderFloat("Fill opacity", ref fill, 0, 1, "%.2f"))
        {
            config.FillOpacity = fill;
            changed = true;
        }

        var outline = config.OutlineOpacity;
        if (ImGui.SliderFloat("Outline opacity", ref outline, 0, 1, "%.2f"))
        {
            config.OutlineOpacity = outline;
            changed = true;
        }

        var thickness = config.LineThickness;
        if (ImGui.SliderFloat("Outline thickness", ref thickness, 0.5f, 8, "%.1f px"))
        {
            config.LineThickness = thickness;
            changed = true;
        }

        var segments = config.CurveSegments;
        if (ImGui.SliderInt("Curve segments", ref segments, 12, 128))
        {
            config.CurveSegments = segments;
            changed = true;
        }

        var leadTime = config.TelegraphLeadTime;
        if (ImGui.SliderFloat("Telegraph lead time", ref leadTime, 0, 10, "%.1f s"))
        {
            config.TelegraphLeadTime = leadTime;
            changed = true;
        }

        var height = config.GroundHeightOffset;
        if (ImGui.DragFloat("Ground height offset", ref height, 0.005f, -2, 2, "%.3f"))
        {
            config.GroundHeightOffset = height;
            changed = true;
        }

        var safeZones = config.ShowSafeZones;
        if (ImGui.Checkbox("Show suggested positions", ref safeZones))
        {
            config.ShowSafeZones = safeZones;
            changed = true;
        }

        var destinationGuide = config.ShowDestinationGuide;
        if (ImGui.Checkbox("Highlight where to stand", ref destinationGuide))
        {
            config.ShowDestinationGuide = destinationGuide;
            changed = true;
        }

        if (config.ShowDestinationGuide)
        {
            ImGui.Indent();

            var destinationPath = config.ShowDestinationPath;
            if (ImGui.Checkbox("Draw path arrow to destination", ref destinationPath))
            {
                config.ShowDestinationPath = destinationPath;
                changed = true;
            }

            var destinationDistance = config.ShowDestinationDistance;
            if (ImGui.Checkbox("Show distance to destination", ref destinationDistance))
            {
                config.ShowDestinationDistance = destinationDistance;
                changed = true;
            }

            ImGui.Unindent();
        }

        var elementColors = config.ElementColoredTelegraphs;
        if (ImGui.Checkbox("Colour telegraphs by element", ref elementColors))
        {
            config.ElementColoredTelegraphs = elementColors;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Lightning lines draw purple and ice cones blue, staying dim while\n"
                + "telegraphed and brightening as they go live. Off returns both to\n"
                + "the shared orange/red pair.");
        }

        var magicTell = config.ShowMagicTell;
        if (ImGui.Checkbox("Show real/fake badges over Kefka", ref magicTell))
        {
            config.ShowMagicTell = magicTell;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Lightning and ice are flagged independently. A '?' badge means that\n"
                + "element resolves as the opposite of the orientation it telegraphs,\n"
                + "matching the question-mark orbs on the rings around Kefka.");
        }

        if (config.ShowMagicTell)
        {
            ImGui.Indent();
            var tellScale = config.MagicTellScale;
            if (ImGui.SliderFloat("Badge size", ref tellScale, 0.5f, 3, "%.2fx"))
            {
                config.MagicTellScale = tellScale;
                changed = true;
            }

            ImGui.Unindent();
        }

        var boundary = config.ShowArenaBoundary;
        if (ImGui.Checkbox("Show arena boundary", ref boundary))
        {
            config.ShowArenaBoundary = boundary;
            changed = true;
        }

        var coordinateLabels = config.ShowDebugCoordinateLabels;
        if (ImGui.Checkbox("Show debug coordinate labels", ref coordinateLabels))
        {
            config.ShowDebugCoordinateLabels = coordinateLabels;
            changed = true;
        }

        return changed;
    }

    private static bool DrawStatusHud(Configuration config)
    {
        var changed = false;

        var visible = config.StatusHudVisible;
        if (ImGui.Checkbox("Show fake status HUD", ref visible))
        {
            config.StatusHudVisible = visible;
            changed = true;
        }

        var locked = config.StatusHudLocked;
        if (ImGui.Checkbox("Lock HUD position", ref locked))
        {
            config.StatusHudLocked = locked;
            changed = true;
        }

        var position = new Vector2(config.StatusHudX, config.StatusHudY);
        if (ImGui.DragFloat2("HUD screen position", ref position, 1))
        {
            config.StatusHudX = position.X;
            config.StatusHudY = position.Y;
            changed = true;
        }

        var scale = config.StatusHudScale;
        if (ImGui.SliderFloat("HUD scale", ref scale, 0.5f, 2.5f, "%.2fx"))
        {
            config.StatusHudScale = scale;
            changed = true;
        }

        var spacing = config.StatusIconSpacing;
        if (ImGui.SliderFloat("Icon spacing", ref spacing, 0, 24, "%.1f px"))
        {
            config.StatusIconSpacing = spacing;
            changed = true;
        }

        var gameIcons = config.UseGameStatusIcons;
        if (ImGui.Checkbox("Use real game status icons", ref gameIcons))
        {
            config.UseGameStatusIcons = gameIcons;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Draws the actual in-game icon for each simulated status.\n"
                + "Falls back to lettered colour tiles while an icon loads.");
        }

        var timers = config.StatusTimerText;
        if (ImGui.Checkbox("Show timer text", ref timers))
        {
            config.StatusTimerText = timers;
            changed = true;
        }

        var sourceSort = config.SortStatusesLikeSource;
        if (ImGui.Checkbox("Use source simulator status order", ref sourceSort))
        {
            config.SortStatusesLikeSource = sourceSort;
            changed = true;
        }

        var debugValues = config.ShowStatusDebugValues;
        if (ImGui.Checkbox("Show assignment values in HUD", ref debugValues))
        {
            config.ShowStatusDebugValues = debugValues;
            changed = true;
        }

        ImGui.Separator();

        var castBars = config.ShowCastBars;
        if (ImGui.Checkbox("Show cast bars", ref castBars))
        {
            config.ShowCastBars = castBars;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "One bar per cast in flight, so an overlapping Grand Cross and\n"
                + "Mysterious Magic each get their own row.");
        }

        if (config.ShowCastBars)
        {
            ImGui.Indent();

            var castX = config.CastBarX;
            if (ImGui.DragFloat("Cast bar X", ref castX, 1, 0, 6000, "%.0f px"))
            {
                config.CastBarX = castX;
                changed = true;
            }

            var castY = config.CastBarY;
            if (ImGui.DragFloat("Cast bar Y", ref castY, 1, 0, 4000, "%.0f px"))
            {
                config.CastBarY = castY;
                changed = true;
            }

            var castScale = config.CastBarScale;
            if (ImGui.SliderFloat("Cast bar scale", ref castScale, 0.5f, 2.5f, "%.2fx"))
            {
                config.CastBarScale = castScale;
                changed = true;
            }

            ImGui.Unindent();
        }

        var countdown = config.ShowCountdown;
        if (ImGui.Checkbox("Show large countdown", ref countdown))
        {
            config.ShowCountdown = countdown;
            changed = true;
        }

        if (config.ShowCountdown)
        {
            ImGui.Indent();

            var countdownScale = config.CountdownScale;
            if (ImGui.SliderFloat("Countdown size", ref countdownScale, 0.5f, 6, "%.2fx"))
            {
                config.CountdownScale = countdownScale;
                changed = true;
            }

            var countdownHeight = config.CountdownHeightFraction;
            if (ImGui.SliderFloat("Countdown height", ref countdownHeight, 0.05f, 0.9f, "%.2f"))
            {
                config.CountdownHeightFraction = countdownHeight;
                changed = true;
            }

            ImGui.Unindent();
        }

        return changed;
    }

    private static bool RoleCombo(string label, ref PartyRole value)
    {
        var changed = false;
        if (!ImGui.BeginCombo(label, value.DisplayName()))
        {
            return false;
        }

        foreach (var role in PartyRoles.All)
        {
            var selected = role == value;
            if (ImGui.Selectable(role.DisplayName(), selected))
            {
                value = role;
                changed = true;
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static bool EnumCombo<T>(string label, ref T value)
        where T : struct, Enum
    {
        var changed = false;
        if (!ImGui.BeginCombo(label, value.ToString()))
        {
            return false;
        }

        foreach (var candidate in Enum.GetValues<T>())
        {
            var selected = EqualityComparer<T>.Default.Equals(candidate, value);
            if (ImGui.Selectable(candidate.ToString(), selected))
            {
                value = candidate;
                changed = true;
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static bool DrawGhosts(Configuration config)
    {
        var changed = false;

        ImGui.TextWrapped(
            "Ghosts are plugin-drawn stand-ins for the two simulated Cursed Shriek "
            + "carriers. They are never game actors and never affect grading.");

        var mannequins = config.ShowGhostMannequins;
        if (ImGui.Checkbox("Show ghost mannequins", ref mannequins))
        {
            config.ShowGhostMannequins = mannequins;
            changed = true;
        }

        var rings = config.ShowGhostGroundRings;
        if (ImGui.Checkbox("Show ghost ground rings", ref rings))
        {
            config.ShowGhostGroundRings = rings;
            changed = true;
        }

        var eyes = config.ShowGazeEyes;
        if (ImGui.Checkbox("Show gaze eyes", ref eyes))
        {
            config.ShowGazeEyes = eyes;
            changed = true;
        }

        var facing = config.ShowGhostFacingArrows;
        if (ImGui.Checkbox("Show ghost facing arrows", ref facing))
        {
            config.ShowGhostFacingArrows = facing;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "The Waju source never reads a Shriek carrier's facing, so this "
                + "arrow is decorative and points at the arena centre.");
        }

        var ids = config.ShowGhostIds;
        if (ImGui.Checkbox("Show ghost IDs", ref ids))
        {
            config.ShowGhostIds = ids;
            changed = true;
        }

        var opacity = config.GhostOpacity;
        if (ImGui.SliderFloat("Ghost opacity", ref opacity, 0.1f, 1f, "%.2f"))
        {
            config.GhostOpacity = opacity;
            changed = true;
        }

        var size = config.GhostSizeMultiplier;
        if (ImGui.SliderFloat("Ghost size multiplier", ref size, 0.25f, 4f, "%.2f"))
        {
            config.GhostSizeMultiplier = size;
            changed = true;
        }

        var fade = config.GhostFadeDuration;
        if (ImGui.SliderFloat("Ghost fade duration", ref fade, 0f, 3f, "%.2f s"))
        {
            config.GhostFadeDuration = fade;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextDisabled(
            "Explicit gaze aids (facing arrow, gaze lines, threshold cone, numeric "
            + "angles) live in the debug window and stay off by default.");

        return changed;
    }

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.86f, 0.72f, 1, 1), title);
        ImGui.Separator();
    }
}
