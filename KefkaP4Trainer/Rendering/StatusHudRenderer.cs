using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Rendering;

internal sealed record StatusHudContext(
    IReadOnlyList<SimulatedDebuff> Debuffs,
    double SimulationTime,
    SimulationState State,
    int PullNumber,
    long Seed,
    string CurrentMechanic,
    string AssignmentSummary,
    SimulationResult? LastResult);

internal sealed class StatusHudRenderer
{
    private const float BaseIconSize = 42;
    private const float BasePadding = 10;
    private const float BaseLineHeight = 18;
    private const float PositionHandleHeight = 48;
    private static readonly TimeSpan PositionSaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly RenderExceptionLogger errors;
    private readonly StatusIconProvider icons;

    /// <summary>The resolved icon table, for the debug atlas.</summary>
    public StatusIconProvider Icons => icons;
    private bool positionDirty;
    private DateTime positionChangedAtUtc;

    public StatusHudRenderer(IPluginLog log, ITextureProvider textures)
    {
        errors = new RenderExceptionLogger(log, nameof(StatusHudRenderer));
        icons = new StatusIconProvider(textures);
        // Resolved once, by name, so a transposed hardcoded id cannot silently
        // show the wrong wound colour. See StatusIconProvider for why.
        icons.ResolveFromGameData(Services.DataManager, Services.Log);
    }

    public void Draw(SimulationEngine engine, Configuration configuration)
    {
        try
        {
            if (engine.Clock.State is SimulationState.Stopped or SimulationState.Completed)
            {
                return;
            }

            var time = engine.Clock.Time;
            var cue = engine.Encounter.CurrentCue(time);
            if (string.IsNullOrWhiteSpace(cue) && engine.NextEvent is { } next)
            {
                cue = $"Next: {next.Name} ({Math.Max(0, next.Time - time):0.0}s)";
            }

            var context = new StatusHudContext(
                engine.Encounter.ActiveDebuffs(time, engine.PlayerRole),
                time,
                engine.Clock.State,
                engine.PullNumber,
                engine.Seed,
                cue,
                engine.Encounter.AssignmentSummary(time),
                engine.LastResult);
            DrawCore(context, configuration);
        }
        catch (Exception exception)
        {
            errors.Report(exception);
        }
    }

    public void Draw(StatusHudContext context, Configuration configuration)
    {
        try
        {
            DrawCore(context, configuration);
        }
        catch (Exception exception)
        {
            errors.Report(exception);
        }
    }

    private void DrawCore(StatusHudContext context, Configuration configuration)
    {
        if (!configuration.StatusHudVisible)
        {
            return;
        }

        var scale = Math.Clamp(configuration.StatusHudScale, 0.5f, 2.5f);
        var padding = BasePadding * scale;
        var iconSize = BaseIconSize * scale;
        var lineHeight = BaseLineHeight * scale;
        var spacing = Math.Clamp(configuration.StatusIconSpacing, 0, 30) * scale;
        var debuffs = OrderedDebuffs(context.Debuffs, configuration.SortStatusesLikeSource);

        var statusLine = $"{context.State.ToString().ToUpperInvariant()}  {FormatTime(context.SimulationTime)}";
        var pullLine = $"Pull {context.PullNumber}  Seed {context.Seed}";
        var mechanicLine = string.IsNullOrWhiteSpace(context.CurrentMechanic)
            ? "Full Kefka P4 sequence"
            : context.CurrentMechanic;
        mechanicLine = Truncate(mechanicLine, 82);
        var assignmentLine = $"You: {Truncate(context.AssignmentSummary, 77)}";
        var resultLine = context.LastResult is null
            ? "Last: --"
            : $"Last: {(context.LastResult.Passed ? "PASS" : "FAIL")} "
                + context.LastResult.Mechanic;
        resultLine = Truncate(resultLine, 82);

        var namesLine = debuffs.Count == 0
            ? "No simulated statuses"
            : string.Join(", ", debuffs.Select(debuff => debuff.Name));
        namesLine = Truncate(namesLine, 96);

        var iconsWidth = debuffs.Count == 0
            ? 0
            : (debuffs.Count * iconSize) + ((debuffs.Count - 1) * spacing);

        // Icons-only strips the panel down to the status row so it can sit
        // against the real debuff bar. It also drops the frame: a border is what
        // would give it away as a separate overlay.
        var iconsOnly = configuration.StatusHudIconsOnly;
        float contentWidth;
        float height;
        if (iconsOnly)
        {
            contentWidth = MathF.Max(iconSize, iconsWidth);
            height = iconSize;
        }
        else
        {
            contentWidth = MathF.Max(300 * scale, iconsWidth);
            contentWidth = MathF.Max(contentWidth, ImGui.CalcTextSize(mechanicLine).X);
            contentWidth = MathF.Max(contentWidth, ImGui.CalcTextSize(statusLine).X);
            contentWidth = MathF.Max(contentWidth, ImGui.CalcTextSize(pullLine).X);
            contentWidth = MathF.Max(contentWidth, ImGui.CalcTextSize(assignmentLine).X);
            contentWidth = MathF.Max(contentWidth, ImGui.CalcTextSize(resultLine).X);
            contentWidth = MathF.Max(contentWidth, ImGui.CalcTextSize(namesLine).X);

            var debugLines = configuration.ShowStatusDebugValues ? debuffs.Count : 0;
            height =
                (padding * 2)
                + (lineHeight * 5)
                + (debuffs.Count > 0 ? iconSize + spacing : 0)
                + lineHeight
                + (debugLines * lineHeight);
        }

        var framePadding = iconsOnly ? 0 : padding;

        var displaySize = ImGui.GetIO().DisplaySize;
        var requested = new Vector2(configuration.StatusHudX, configuration.StatusHudY);
        if (!configuration.StatusHudLocked)
        {
            requested = DrawPositionHandle(requested, configuration, displaySize);
        }
        else if (positionDirty)
        {
            configuration.Save();
            positionDirty = false;
        }

        var panelSize = new Vector2(contentWidth + (framePadding * 2), height);
        var position = ClampToDisplay(requested, panelSize, displaySize);
        var drawList = ImGui.GetForegroundDrawList();

        var textColor = ImGui.GetColorU32(new Vector4(0.96f, 0.97f, 1, 1));
        var mutedColor = ImGui.GetColorU32(new Vector4(0.72f, 0.76f, 0.84f, 1));

        if (!iconsOnly)
        {
            var panelColor = ImGui.GetColorU32(new Vector4(0.035f, 0.045f, 0.065f, 0.86f));
            var borderColor = ImGui.GetColorU32(new Vector4(0.62f, 0.72f, 0.90f, 0.9f));
            drawList.AddRectFilled(position, position + panelSize, panelColor, 6 * scale);
            drawList.AddRect(position, position + panelSize, borderColor, 6 * scale);
        }

        var cursor = position + new Vector2(framePadding, framePadding);
        if (!iconsOnly)
        {
            drawList.AddText(cursor, textColor, statusLine);
            cursor.Y += lineHeight;
            drawList.AddText(cursor, mutedColor, pullLine);
            cursor.Y += lineHeight;
            drawList.AddText(cursor, textColor, mechanicLine);
            cursor.Y += lineHeight;
            drawList.AddText(cursor, mutedColor, assignmentLine);
            cursor.Y += lineHeight;
            var resultColor = context.LastResult is { Passed: false }
                ? ImGui.GetColorU32(new Vector4(1, 0.34f, 0.38f, 1))
                : mutedColor;
            drawList.AddText(cursor, resultColor, resultLine);
            cursor.Y += lineHeight + spacing;
        }

        for (var index = 0; index < debuffs.Count; index++)
        {
            var debuff = debuffs[index];
            var iconMin = cursor + new Vector2(index * (iconSize + spacing), 0);
            var iconMax = iconMin + new Vector2(iconSize, iconSize);
            var iconBorder = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.85f));
            var wrap = configuration.UseGameStatusIcons ? icons.TryGet(debuff.Kind) : null;
            if (wrap is not null)
            {
                // Status icons are taller than they are wide, so fit to the slot
                // height and centre horizontally rather than stretching to square.
                var aspect = wrap.Height > 0 ? (float)wrap.Width / wrap.Height : 1f;
                var drawWidth = iconSize * aspect;
                var imageMin = iconMin + new Vector2((iconSize - drawWidth) * 0.5f, 0);
                drawList.AddImage(wrap.Handle, imageMin, imageMin + new Vector2(drawWidth, iconSize));
            }
            else
            {
                // Either icons are switched off or the texture is still loading;
                // the flat tile keeps the HUD readable either way.
                var iconColor = ImGui.GetColorU32(new Vector4(ColorFor(debuff.Kind), 0.92f));
                drawList.AddRectFilled(iconMin, iconMax, iconColor, 4 * scale);
                drawList.AddRect(iconMin, iconMax, iconBorder, 4 * scale);
                DrawCenteredText(drawList, debuff.ShortLabel, iconMin, iconSize, textColor, -5 * scale);
            }

            if (configuration.StatusTimerText)
            {
                var remaining = FormatRemaining(debuff.RemainingAt(context.SimulationTime));
                DrawCenteredText(drawList, remaining, iconMin, iconSize, textColor, 10 * scale);
            }

            if (debuff.Stacks > 1)
            {
                var stacks = debuff.Stacks.ToString();
                var size = ImGui.CalcTextSize(stacks);
                drawList.AddText(iconMax - size - new Vector2(3, 2), textColor, stacks);
            }
        }

        if (iconsOnly)
        {
            return;
        }

        if (debuffs.Count > 0)
        {
            cursor.Y += iconSize + spacing;
        }

        drawList.AddText(cursor, mutedColor, namesLine);
        cursor.Y += lineHeight;

        if (configuration.ShowStatusDebugValues)
        {
            foreach (var debuff in debuffs)
            {
                var debug =
                    $"{debuff.Role.Key()} {debuff.Kind}  "
                    + $"applied={debuff.AppliedAt:0.0} expires={FormatExpiry(debuff.ExpiresAt)}";
                drawList.AddText(cursor, mutedColor, debug);
                cursor.Y += lineHeight;
            }
        }
    }

    private Vector2 DrawPositionHandle(
        Vector2 hudPosition,
        Configuration configuration,
        Vector2 displaySize)
    {
        const float handleWidth = 238;
        const float gap = 4;
        var handleSize = new Vector2(handleWidth, PositionHandleHeight);
        var requestedHandlePosition =
            hudPosition - new Vector2(0, PositionHandleHeight + gap);
        requestedHandlePosition =
            ClampToDisplay(requestedHandlePosition, handleSize, displaySize);

        ImGui.SetNextWindowPos(requestedHandlePosition, ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(handleSize, ImGuiCond.Always);
        var flags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoNav;
        ImGui.Begin("Move Kefka P4 Status HUD##KefkaP4StatusHudPosition", flags);
        ImGui.TextUnformatted("Drag this handle; lock the HUD when done.");
        var handlePosition = ImGui.GetWindowPos();
        ImGui.End();

        var movedHudPosition =
            handlePosition + new Vector2(0, PositionHandleHeight + gap);
        if (Vector2.DistanceSquared(movedHudPosition, hudPosition) > 0.01f)
        {
            configuration.StatusHudX = movedHudPosition.X;
            configuration.StatusHudY = movedHudPosition.Y;
            positionDirty = true;
            positionChangedAtUtc = DateTime.UtcNow;
        }
        else if (positionDirty && DateTime.UtcNow - positionChangedAtUtc >= PositionSaveDelay)
        {
            configuration.Save();
            positionDirty = false;
        }

        return movedHudPosition;
    }

    private static IReadOnlyList<SimulatedDebuff> OrderedDebuffs(
        IReadOnlyList<SimulatedDebuff> debuffs,
        bool sortLikeSource)
    {
        if (!sortLikeSource || debuffs.Count < 2)
        {
            return debuffs;
        }

        return debuffs
            .OrderBy(debuff => KefkaP4Constants.DebuffOrder[debuff.Kind])
            .ThenBy(debuff => debuff.ExpiresAt)
            .ToArray();
    }

    private static void DrawCenteredText(
        ImDrawListPtr drawList,
        string text,
        Vector2 iconMin,
        float iconSize,
        uint color,
        float verticalOffset)
    {
        var size = ImGui.CalcTextSize(text);
        var position = iconMin + new Vector2(
            (iconSize - size.X) * 0.5f,
            ((iconSize - size.Y) * 0.5f) + verticalOffset);
        drawList.AddText(position + Vector2.One, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.9f)), text);
        drawList.AddText(position, color, text);
    }

    private static Vector2 ClampToDisplay(Vector2 requested, Vector2 panelSize, Vector2 displaySize)
    {
        if (!float.IsFinite(displaySize.X)
            || !float.IsFinite(displaySize.Y)
            || displaySize.X <= 0
            || displaySize.Y <= 0)
        {
            return requested;
        }

        return new Vector2(
            Math.Clamp(requested.X, 0, Math.Max(0, displaySize.X - panelSize.X)),
            Math.Clamp(requested.Y, 0, Math.Max(0, displaySize.Y - panelSize.Y)));
    }

    private static string FormatTime(double time)
    {
        if (!double.IsFinite(time))
        {
            return "--:--.-";
        }

        if (time < 0)
        {
            return $"-{Math.Abs(time):0.0}";
        }

        var minutes = (int)(time / 60);
        var seconds = time - (minutes * 60);
        return $"{minutes:00}:{seconds:00.0}";
    }

    private static string FormatRemaining(double remaining) =>
        double.IsPositiveInfinity(remaining)
            ? "--"
            : $"{Math.Max(0, remaining):0.0}";

    private static string FormatExpiry(double expiry) =>
        double.IsPositiveInfinity(expiry) ? "inf" : $"{expiry:0.0}";

    private static string Truncate(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters
            ? value
            : string.Concat(value.AsSpan(0, maximumCharacters - 3), "...");

    private static Vector3 ColorFor(DebuffKind kind) => kind switch
    {
        DebuffKind.BlackWound => new Vector3(0.34f, 0.22f, 0.48f),
        DebuffKind.WhiteWound => new Vector3(0.70f, 0.74f, 0.82f),
        DebuffKind.BeyondDeath => new Vector3(0.72f, 0.14f, 0.20f),
        DebuffKind.AllaganField => new Vector3(0.20f, 0.43f, 0.85f),
        DebuffKind.CursedShriek => new Vector3(0.56f, 0.22f, 0.78f),
        DebuffKind.CompressedWater => new Vector3(0.12f, 0.55f, 0.94f),
        DebuffKind.ForkedLightning => new Vector3(0.92f, 0.73f, 0.12f),
        DebuffKind.AccelerationBomb => new Vector3(0.94f, 0.43f, 0.10f),
        DebuffKind.Entropy => new Vector3(0.82f, 0.16f, 0.28f),
        DebuffKind.DynamicFluid => new Vector3(0.10f, 0.68f, 0.66f),
        _ => new Vector3(0.45f, 0.50f, 0.60f),
    };
}
