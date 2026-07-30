using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using KefkaP4Trainer.Core;

namespace KefkaP4Trainer.Rendering;

/// <summary>
/// Large centred pull countdown, plus a brief "GO" once the clock starts.
/// </summary>
/// <remarks>
/// The clock runs negative through the countdown and crosses zero on the pull,
/// which is why the remaining time is the negated clock rather than a separate
/// timer.
/// </remarks>
internal sealed class CountdownRenderer
{
    private const double GoDuration = 0.8;

    private readonly RenderExceptionLogger errors;

    public CountdownRenderer(IPluginLog log)
    {
        errors = new RenderExceptionLogger(log, nameof(CountdownRenderer));
    }

    public void Draw(SimulationEngine engine, Configuration configuration)
    {
        try
        {
            if (!configuration.ShowCountdown)
            {
                return;
            }

            var state = engine.Clock.State;
            if (state is not (SimulationState.Countdown or SimulationState.Running))
            {
                return;
            }

            var time = engine.Clock.Time;
            string text;
            float emphasis;
            if (state == SimulationState.Countdown && time < 0)
            {
                var remaining = -time;
                text = Math.Max(1, (int)Math.Ceiling(remaining)).ToString();

                // Swells as each whole second closes so the beat is visible even
                // when the digit itself has not changed yet.
                emphasis = 1 + (0.35f * (float)(1 - (remaining - Math.Floor(remaining))));
            }
            else if (state == SimulationState.Running && time >= 0 && time < GoDuration)
            {
                text = "GO";
                emphasis = 1 + (0.25f * (float)(1 - (time / GoDuration)));
            }
            else
            {
                return;
            }

            DrawCentred(text, emphasis, configuration);
        }
        catch (Exception exception)
        {
            errors.Report(exception);
        }
    }

    private static void DrawCentred(string text, float emphasis, Configuration configuration)
    {
        var display = ImGui.GetIO().DisplaySize;
        if (!float.IsFinite(display.X) || !float.IsFinite(display.Y) || display.X <= 0)
        {
            return;
        }

        var baseSize = ImGui.GetFontSize();
        if (!float.IsFinite(baseSize) || baseSize <= 0)
        {
            return;
        }

        var scale = Math.Clamp(configuration.CountdownScale, 0.5f, 6);
        var fontSize = baseSize * 5 * scale * emphasis;

        // CalcTextSize measures at the current font size, so scale the result to
        // the size actually being drawn.
        var measured = ImGui.CalcTextSize(text) * (fontSize / baseSize);
        var position = new Vector2(
            (display.X - measured.X) * 0.5f,
            (display.Y * Math.Clamp(configuration.CountdownHeightFraction, 0.05f, 0.9f))
            - (measured.Y * 0.5f));

        var drawList = ImGui.GetForegroundDrawList();
        var font = ImGui.GetFont();
        drawList.AddText(
            font,
            fontSize,
            position + new Vector2(3, 3),
            ImGui.GetColorU32(new Vector4(0, 0, 0, 0.85f)),
            text);
        drawList.AddText(
            font,
            fontSize,
            position,
            ImGui.GetColorU32(new Vector4(1.00f, 0.92f, 0.55f, 1)),
            text);
    }
}
