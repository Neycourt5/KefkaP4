using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Rendering;

/// <summary>
/// Stacked cast bars for the casts in flight, laid out like the game's enemy
/// cast bar: ability name over a bar that fills as the cast completes.
/// </summary>
/// <remarks>
/// Several casts overlap in this phase — a Grand Cross runs for nine seconds
/// while Mysterious Magic starts and resolves inside it — so every active cast
/// gets its own row rather than showing only the most recent.
/// </remarks>
internal sealed class CastBarRenderer
{
    private const float BaseWidth = 230;
    private const float BaseBarHeight = 12;
    private const float BasePadding = 8;
    private const float BaseLineHeight = 18;
    private const float BaseRowGap = 8;

    private readonly RenderExceptionLogger errors;
    private readonly List<CastBar> active = new(4);

    public CastBarRenderer(IPluginLog log)
    {
        errors = new RenderExceptionLogger(log, nameof(CastBarRenderer));
    }

    public void Draw(SimulationEngine engine, Configuration configuration)
    {
        try
        {
            if (!configuration.ShowCastBars
                || engine.Clock.State is SimulationState.Stopped or SimulationState.Completed)
            {
                return;
            }

            var time = engine.Clock.Time;
            KefkaP4CastBars.CollectActive(engine.Encounter.CastBars, time, active);
            if (active.Count == 0)
            {
                return;
            }

            DrawCore(time, configuration);
        }
        catch (Exception exception)
        {
            errors.Report(exception);
        }
    }

    private void DrawCore(double time, Configuration configuration)
    {
        var scale = Math.Clamp(configuration.CastBarScale, 0.5f, 2.5f);
        var width = BaseWidth * scale;
        var barHeight = BaseBarHeight * scale;
        var padding = BasePadding * scale;
        var lineHeight = BaseLineHeight * scale;
        var rowGap = BaseRowGap * scale;
        var rowHeight = lineHeight + barHeight;

        var panelSize = new Vector2(
            width + (padding * 2),
            (padding * 2) + (active.Count * rowHeight) + ((active.Count - 1) * rowGap));

        var displaySize = ImGui.GetIO().DisplaySize;
        var position = ClampToDisplay(
            new Vector2(configuration.CastBarX, configuration.CastBarY), panelSize, displaySize);

        var drawList = ImGui.GetForegroundDrawList();
        drawList.AddRectFilled(
            position,
            position + panelSize,
            ImGui.GetColorU32(new Vector4(0.035f, 0.045f, 0.065f, 0.82f)),
            6 * scale);
        drawList.AddRect(
            position,
            position + panelSize,
            ImGui.GetColorU32(new Vector4(0.62f, 0.72f, 0.90f, 0.85f)),
            6 * scale);

        var cursor = position + new Vector2(padding, padding);
        var textColor = ImGui.GetColorU32(new Vector4(0.96f, 0.97f, 1, 1));
        var mutedColor = ImGui.GetColorU32(new Vector4(0.72f, 0.76f, 0.84f, 1));

        for (var index = 0; index < active.Count; index++)
        {
            var cast = active[index];
            var remaining = $"{cast.RemainingAt(time):0.0}";
            var remainingSize = ImGui.CalcTextSize(remaining);

            drawList.AddText(cursor, textColor, Truncate(cast.Name, 30));
            drawList.AddText(
                new Vector2(cursor.X + width - remainingSize.X, cursor.Y), mutedColor, remaining);

            var barMin = new Vector2(cursor.X, cursor.Y + lineHeight);
            var barMax = barMin + new Vector2(width, barHeight);
            drawList.AddRectFilled(
                barMin, barMax, ImGui.GetColorU32(new Vector4(0.10f, 0.11f, 0.15f, 0.95f)), 2 * scale);

            var progress = cast.ProgressAt(time);
            if (progress > 0)
            {
                // Warms towards the end of the cast so an imminent resolve reads
                // without having to parse the countdown.
                var fill = new Vector4(
                    0.35f + (0.55f * progress),
                    0.72f - (0.34f * progress),
                    0.95f - (0.66f * progress),
                    0.95f);
                drawList.AddRectFilled(
                    barMin,
                    new Vector2(barMin.X + (width * progress), barMax.Y),
                    ImGui.GetColorU32(fill),
                    2 * scale);
            }

            drawList.AddRect(
                barMin, barMax, ImGui.GetColorU32(new Vector4(0.62f, 0.72f, 0.90f, 0.75f)), 2 * scale);
            cursor.Y += rowHeight + rowGap;
        }
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

    private static string Truncate(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters
            ? value
            : string.Concat(value.AsSpan(0, maximumCharacters - 3), "...");
}
