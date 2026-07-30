using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using KefkaP4Trainer.Core;

namespace KefkaP4Trainer.Rendering;

internal sealed class ResultOverlayRenderer
{
    private readonly RenderExceptionLogger errors;

    public ResultOverlayRenderer(IPluginLog log)
    {
        errors = new RenderExceptionLogger(log, nameof(ResultOverlayRenderer));
    }

    public void Draw(SimulationEngine engine, float visibleSimulatedSeconds = 3)
    {
        try
        {
            var result = engine.LastResult;
            if (result is null || result.PullNumber != engine.PullNumber)
            {
                return;
            }

            DrawCore(result, engine.Clock.Time, visibleSimulatedSeconds);
        }
        catch (Exception exception)
        {
            errors.Report(exception);
        }
    }

    public void Draw(
        SimulationResult? result,
        double simulationTime,
        float visibleSimulatedSeconds = 3)
    {
        try
        {
            if (result is not null)
            {
                DrawCore(result, simulationTime, visibleSimulatedSeconds);
            }
        }
        catch (Exception exception)
        {
            errors.Report(exception);
        }
    }

    private static void DrawCore(
        SimulationResult result,
        double simulationTime,
        float visibleSimulatedSeconds)
    {
        var duration = Math.Clamp(visibleSimulatedSeconds, 0.25f, 15);
        var elapsed = simulationTime - result.Timestamp;
        if (!double.IsFinite(elapsed) || elapsed < -0.001 || elapsed >= duration)
        {
            return;
        }

        var fadeStart = Math.Max(0, duration - 0.5f);
        var opacity = elapsed <= fadeStart
            ? 1
            : (float)Math.Clamp((duration - elapsed) / (duration - fadeStart), 0, 1);
        var title = $"{(result.Passed ? "PASS" : "FAIL")} - {result.Mechanic}";
        var detail = result.Passed ? $"Pull {result.PullNumber}" : Truncate(result.Reason, 100);
        var titleSize = ImGui.CalcTextSize(title);
        var detailSize = ImGui.CalcTextSize(detail);
        var contentWidth = MathF.Max(titleSize.X, detailSize.X);
        var panelSize = new Vector2(contentWidth + 36, titleSize.Y + detailSize.Y + 28);
        var display = ImGui.GetIO().DisplaySize;
        var position = new Vector2(
            MathF.Max(0, (display.X - panelSize.X) * 0.5f),
            Math.Clamp(display.Y * 0.12f, 20, 160));

        var drawList = ImGui.GetForegroundDrawList();
        var accent = result.Passed
            ? new Vector4(0.20f, 0.92f, 0.40f, opacity)
            : new Vector4(1.00f, 0.22f, 0.26f, opacity);
        var panel = new Vector4(0.025f, 0.03f, 0.045f, 0.88f * opacity);
        var text = new Vector4(1, 1, 1, opacity);
        drawList.AddRectFilled(position, position + panelSize, ImGui.GetColorU32(panel), 7);
        drawList.AddRect(position, position + panelSize, ImGui.GetColorU32(accent), 7);

        var titlePosition =
            position + new Vector2((panelSize.X - titleSize.X) * 0.5f, 8);
        var detailPosition =
            position + new Vector2((panelSize.X - detailSize.X) * 0.5f, titleSize.Y + 14);
        drawList.AddText(titlePosition, ImGui.GetColorU32(accent), title);
        drawList.AddText(detailPosition, ImGui.GetColorU32(text), detail);
    }

    private static string Truncate(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters
            ? value
            : string.Concat(value.AsSpan(0, maximumCharacters - 3), "...");
}
