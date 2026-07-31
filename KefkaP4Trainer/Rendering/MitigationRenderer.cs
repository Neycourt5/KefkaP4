using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Rendering;

/// <summary>
/// Shows the next mitigation the local player owes, counting down to it.
/// </summary>
/// <remarks>
/// Carry-overs are filtered out upstream: the plan marks them because the effect
/// is still running from an earlier press, so calling one would train a press
/// that should not happen.
/// </remarks>
internal sealed class MitigationRenderer
{
    private const float BaseWidth = 260;
    private const float BasePadding = 9;
    private const float BaseLineHeight = 19;

    private readonly RenderExceptionLogger errors;

    public MitigationRenderer(IPluginLog log)
    {
        errors = new RenderExceptionLogger(log, nameof(MitigationRenderer));
    }

    public void Draw(SimulationEngine engine, Configuration configuration, MitSlot? slot)
    {
        try
        {
            if (!configuration.ShowMitigationAlerts
                || slot is not { } seat
                || engine.Clock.State is SimulationState.Stopped or SimulationState.Completed)
            {
                return;
            }

            var time = engine.Clock.Time;
            if (KefkaP4Mitigation.Next(seat, time) is not { } call)
            {
                return;
            }

            var lead = Math.Clamp(configuration.MitigationLeadTime, 1, 30);
            var until = call.Time - time;
            if (until > lead)
            {
                return;
            }

            DrawCore(call, until, seat, configuration);
        }
        catch (Exception exception)
        {
            errors.Report(exception);
        }
    }

    private static void DrawCore(
        MitigationCall call,
        double until,
        MitSlot slot,
        Configuration configuration)
    {
        var scale = Math.Clamp(configuration.MitigationScale, 0.5f, 2.5f);
        var padding = BasePadding * scale;
        var lineHeight = BaseLineHeight * scale;

        var header = $"{slot} — {call.Mechanic}";
        var action = call.Action;
        var countdown = until <= 0 ? "NOW" : $"in {until:0.0}s";

        var width = MathF.Max(BaseWidth * scale, ImGui.CalcTextSize(action).X);
        width = MathF.Max(width, ImGui.CalcTextSize(header).X);
        var panelSize = new Vector2(width + (padding * 2), (padding * 2) + (lineHeight * 3));

        var display = ImGui.GetIO().DisplaySize;
        var position = ClampToDisplay(
            new Vector2(configuration.MitigationX, configuration.MitigationY), panelSize, display);

        // Goes hot inside the last second so the press lands on time.
        var imminent = until <= 1;
        var accent = imminent
            ? new Vector4(1.00f, 0.45f, 0.20f, 1)
            : new Vector4(0.45f, 0.90f, 0.60f, 1);

        var drawList = ImGui.GetForegroundDrawList();
        drawList.AddRectFilled(
            position,
            position + panelSize,
            ImGui.GetColorU32(new Vector4(0.03f, 0.05f, 0.04f, 0.88f)),
            6 * scale);
        drawList.AddRect(
            position, position + panelSize, ImGui.GetColorU32(accent), 6 * scale, 0, 2 * scale);

        var cursor = position + new Vector2(padding, padding);
        drawList.AddText(cursor, ImGui.GetColorU32(new Vector4(0.72f, 0.76f, 0.84f, 1)), header);
        cursor.Y += lineHeight;
        drawList.AddText(cursor, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), action);
        cursor.Y += lineHeight;
        drawList.AddText(cursor, ImGui.GetColorU32(accent), countdown);
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
}
