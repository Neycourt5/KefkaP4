using System.Numerics;
using Dalamud.Bindings.ImGui;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Rendering;

/// <summary>
/// Draws a stand-in for Neo Exdeath and its two Antilight banners.
/// </summary>
/// <remarks>
/// <para>
/// In the encounter the Flood sides are read off two coloured billboards
/// floating beside Neo at the arena edge, not off the floor. Reproducing that
/// means the habit this builds is "look at the boss and match the colour",
/// which is what actually transfers.
/// </para>
/// <para>
/// Geometry comes from <see cref="FloodStage"/>, which is transcribed from
/// <c>neo_move_fade_in</c> and <c>neo_exdeath.tscn</c>: Neo at the arena's north
/// edge rotated by the pull rotation, banners at his local X +/-17 and Z +5.
/// Every colour and side comes from the same <see cref="FloodResolution"/> the
/// grader uses, so a banner cannot contradict the verdict.
/// </para>
/// </remarks>
internal sealed class FloodStageRenderer
{
    private const float BannerRadius = 7.5f;
    private const float NeoRadius = 5f;
    private const float BannerHeight = 6.5f;
    private const float NeoHeight = 3.25f;

    private static readonly Vector3 PurpleColor = new(0.64f, 0.33f, 0.95f);
    private static readonly Vector3 BlueColor = new(0.22f, 0.62f, 1.00f);
    private static readonly Vector3 NeoColor = new(0.30f, 0.26f, 0.38f);
    private static readonly Vector3 RealColor = new(0.25f, 0.85f, 1.00f);
    private static readonly Vector3 FakeColor = new(1.00f, 0.45f, 0.10f);

    private readonly ProjectionHelper projection;
    private readonly Vector2[] ring = new Vector2[28];

    public FloodStageRenderer(ProjectionHelper projection)
    {
        this.projection = projection;
    }

    public void Draw(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        FloodStageView stage,
        Configuration configuration)
    {
        var scale = Math.Clamp(configuration.FloodStageScale, 0.4f, 3f);
        var ground = Math.Clamp(configuration.GroundHeightOffset, -2, 2);
        var thickness = Math.Clamp(configuration.LineThickness, 1, 8);

        DrawNeo(drawList, arena, stage, ground, scale, thickness);

        if (!stage.ShowAntilights)
        {
            return;
        }

        foreach (var side in new[] { ArenaSide.West, ArenaSide.East })
        {
            DrawBanner(drawList, arena, stage, side, ground, scale, thickness);
        }
    }

    private void DrawNeo(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        FloodStageView stage,
        float ground,
        float scale,
        float thickness)
    {
        var body = projection.Project(
            arena.SimulatorToWorld(stage.NeoPosition, ground + (NeoHeight * scale)));
        if (!body.Succeeded || !body.InView)
        {
            return;
        }

        var radius = ScreenRadius(arena, stage.NeoPosition, ground + (NeoHeight * scale), NeoRadius * scale);
        if (radius < 3)
        {
            return;
        }

        drawList.AddCircleFilled(
            body.ScreenPosition, radius, Pack(NeoColor, 0.55f), 24);
        drawList.AddCircle(
            body.ScreenPosition, radius, Pack(new Vector3(0.75f, 0.70f, 0.85f), 0.9f), 24, thickness);

        // The real/fake ring Waju shows on Neo during the cast.
        var fake = stage.Resolution.IsFake;
        var orbColor = fake ? FakeColor : RealColor;
        drawList.AddCircle(
            body.ScreenPosition, radius * 1.35f, Pack(orbColor, 0.95f), 28, thickness + 1.5f);

        var tag = fake ? "FAKE" : "REAL";
        Label(drawList, body.ScreenPosition - new Vector2(0, radius * 1.9f), tag, orbColor, 1f);
        Label(drawList, body.ScreenPosition + new Vector2(0, radius * 1.6f), "NEO EXDEATH", NeoColor, 0.85f);
    }

    private void DrawBanner(
        ImDrawListPtr drawList,
        ArenaTransform arena,
        FloodStageView stage,
        ArenaSide side,
        float ground,
        float scale,
        float thickness)
    {
        var resolution = stage.Resolution;
        var position = stage.BannerPosition(side);
        var height = ground + (BannerHeight * scale);
        var centre = projection.Project(arena.SimulatorToWorld(position, height));
        if (!centre.Succeeded || !centre.InView)
        {
            return;
        }

        var color = resolution.ColorOn(side) == FloodVisualColor.Purple ? PurpleColor : BlueColor;
        var required = side == resolution.RequiredSide;
        var radius = ScreenRadius(arena, position, height, BannerRadius * scale);
        if (radius < 4)
        {
            return;
        }

        // A filled disc rather than an outline: at range an outline reads as a
        // thin ring and the colour, which is the whole point, gets lost.
        drawList.AddCircleFilled(centre.ScreenPosition, radius, Pack(color, 0.55f), 28);
        drawList.AddCircle(
            centre.ScreenPosition,
            radius,
            Pack(color, 1f),
            28,
            required ? thickness + 2.5f : thickness);

        if (required)
        {
            // A second ring marks the one to stand in, so the answer survives
            // even if the player cannot tell the two hues apart.
            drawList.AddCircle(
                centre.ScreenPosition, radius * 1.28f, Pack(new Vector3(1, 1, 1), 0.85f), 28, thickness);
        }

        var colorWord = resolution.ColorOn(side).ToString().ToUpperInvariant();
        var sideWord = side.ToString().ToUpperInvariant();
        Label(
            drawList,
            centre.ScreenPosition - new Vector2(0, radius + (12 * scale)),
            $"{sideWord} - {colorWord}",
            color,
            1f);
        Label(
            drawList,
            centre.ScreenPosition + new Vector2(0, radius + (4 * scale)),
            $"{resolution.AntilightOn(side)} Antilight{(required ? "  <- GO HERE" : string.Empty)}",
            required ? new Vector3(1, 1, 1) : color,
            0.85f);
    }

    /// <summary>
    /// Screen radius from a second projected point, so the marker shrinks with
    /// distance instead of staying a fixed pixel size.
    /// </summary>
    private float ScreenRadius(
        ArenaTransform arena,
        Vector2 arenaPosition,
        float height,
        float worldRadius)
    {
        var centre = projection.Project(arena.SimulatorToWorld(arenaPosition, height));
        var edge = projection.Project(
            arena.SimulatorToWorld(arenaPosition + new Vector2(worldRadius, 0), height));
        if (!centre.Succeeded || !edge.Succeeded)
        {
            return 0;
        }

        var radius = Vector2.Distance(centre.ScreenPosition, edge.ScreenPosition);
        return float.IsFinite(radius) ? Math.Clamp(radius, 0, 400) : 0;
    }

    private static void Label(
        ImDrawListPtr drawList,
        Vector2 position,
        string text,
        Vector3 color,
        float alpha)
    {
        var size = ImGui.CalcTextSize(text);
        var origin = position - new Vector2(size.X * 0.5f, size.Y * 0.5f);
        drawList.AddText(origin + Vector2.One, Pack(Vector3.Zero, alpha * 0.9f), text);
        drawList.AddText(origin, Pack(color, alpha), text);
    }

    private static uint Pack(Vector3 color, float alpha) =>
        ImGui.GetColorU32(new Vector4(color, Math.Clamp(alpha, 0, 1)));
}
