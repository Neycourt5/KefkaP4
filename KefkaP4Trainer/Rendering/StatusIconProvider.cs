using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using KefkaP4Trainer.Core;

namespace KefkaP4Trainer.Rendering;

/// <summary>
/// Maps simulated debuffs onto the real in-game status icons.
/// </summary>
/// <remarks>
/// Icon ids come from the Status sheet. Several of these statuses exist as more
/// than one row (the Sigmascape originals and the Dancing Mad reissues), but every
/// duplicate of a given status shares a single icon id, so the mapping is
/// unambiguous and needs no Excel lookup at runtime.
/// </remarks>
internal sealed class StatusIconProvider
{
    private static readonly IReadOnlyDictionary<DebuffKind, uint> IconIds =
        new Dictionary<DebuffKind, uint>
        {
            [DebuffKind.BlackWound] = 215783,
            [DebuffKind.WhiteWound] = 215782,
            [DebuffKind.BeyondDeath] = 215780,
            [DebuffKind.AllaganField] = 215590,
            [DebuffKind.CursedShriek] = 215588,
            [DebuffKind.CompressedWater] = 215696,
            [DebuffKind.ForkedLightning] = 215623,
            [DebuffKind.AccelerationBomb] = 215727,
            [DebuffKind.Entropy] = 215902,
            [DebuffKind.DynamicFluid] = 215903,
        };

    private readonly ITextureProvider textures;

    public StatusIconProvider(ITextureProvider textures)
    {
        this.textures = textures;
    }

    /// <summary>
    /// Gets the icon to draw this frame, or null if the id is unknown or the
    /// texture has not finished loading. Callers fall back to a flat tile rather
    /// than blocking, because the provider loads asynchronously and unloads
    /// icons that go undrawn for a couple of seconds.
    /// </summary>
    public IDalamudTextureWrap? TryGet(DebuffKind kind) =>
        IconIds.TryGetValue(kind, out var iconId)
            ? textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault()
            : null;
}
