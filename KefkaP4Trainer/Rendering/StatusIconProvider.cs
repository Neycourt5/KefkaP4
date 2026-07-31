using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using KefkaP4Trainer.Core;

namespace KefkaP4Trainer.Rendering;

/// <summary>One row of the icon audit, for the debug atlas.</summary>
public sealed record StatusIconAudit(
    DebuffKind Kind,
    string ExpectedName,
    uint FallbackIconId,
    uint? ResolvedIconId,
    uint? StatusRowId,
    string? ResolvedName,
    bool Matched)
{
    public uint IconId => ResolvedIconId ?? FallbackIconId;

    public string Summary => Matched
        ? $"{ExpectedName}: status {StatusRowId}, icon {IconId} (sheet)"
        : ResolvedIconId is null
            ? $"{ExpectedName}: NOT FOUND in the Status sheet; using hardcoded icon {FallbackIconId}"
            : $"{ExpectedName}: sheet icon {ResolvedIconId} != hardcoded {FallbackIconId}";
}

/// <summary>
/// Maps simulated debuffs onto the real in-game status icons.
/// </summary>
/// <remarks>
/// <para>
/// Icon ids are resolved from the Status sheet <b>by name</b> at startup rather
/// than trusted from a hand-written table. A transposed pair in that table is
/// invisible in code review and produces exactly the failure that prompted this
/// change: a player reads the wrong colour off a wound icon and takes the wrong
/// Flood side while their reasoning was sound.
/// </para>
/// <para>
/// The hardcoded ids remain as a fallback for when the sheet cannot be read,
/// and <see cref="Audit"/> reports every row so a mismatch is visible in the
/// debug window rather than silent.
/// </para>
/// </remarks>
public sealed class StatusIconProvider
{
    /// <summary>
    /// Status names as they appear in the game's Status sheet. These are what
    /// the resolution is keyed on; the ids below are only a fallback.
    /// </summary>
    private static readonly IReadOnlyDictionary<DebuffKind, string> StatusNames =
        new Dictionary<DebuffKind, string>
        {
            [DebuffKind.BlackWound] = "Black Wound",
            [DebuffKind.WhiteWound] = "White Wound",
            [DebuffKind.BeyondDeath] = "Beyond Death",
            [DebuffKind.AllaganField] = "Allagan Field",
            [DebuffKind.CursedShriek] = "Cursed Shriek",
            [DebuffKind.CompressedWater] = "Compressed Water",
            [DebuffKind.ForkedLightning] = "Forked Lightning",
            [DebuffKind.AccelerationBomb] = "Acceleration Bomb",
            [DebuffKind.Entropy] = "Entropy",
            [DebuffKind.DynamicFluid] = "Dynamic Fluid",
        };

    private static readonly IReadOnlyDictionary<DebuffKind, uint> FallbackIconIds =
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
    private Dictionary<DebuffKind, uint> iconIds = new(FallbackIconIds);

    public StatusIconProvider(ITextureProvider textures)
    {
        this.textures = textures;
    }

    /// <summary>Per-debuff resolution detail, for the debug atlas.</summary>
    public IReadOnlyList<StatusIconAudit> Audit { get; private set; } = [];

    /// <summary>Rows whose sheet icon disagreed with the hardcoded id.</summary>
    public IReadOnlyList<StatusIconAudit> Mismatches =>
        Audit.Where(entry => !entry.Matched).ToArray();

    /// <summary>
    /// Looks every status up by name and adopts the sheet's icon id. Safe to
    /// call once at startup; failures leave the fallbacks in place.
    /// </summary>
    public void ResolveFromGameData(IDataManager data, IPluginLog log)
    {
        var audit = new List<StatusIconAudit>();
        var resolved = new Dictionary<DebuffKind, uint>(FallbackIconIds);

        try
        {
            var sheet = data.GetExcelSheet<Lumina.Excel.Sheets.Status>();
            if (sheet is null)
            {
                return;
            }

            // One pass over the sheet; several of these statuses exist as more
            // than one row (Sigmascape originals and the reissues), and any row
            // with the right name carries the right icon.
            var byName = new Dictionary<string, (uint RowId, uint Icon)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var row in sheet)
            {
                var name = row.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name) && !byName.ContainsKey(name))
                {
                    byName[name] = (row.RowId, row.Icon);
                }
            }

            foreach (var pair in StatusNames)
            {
                var fallback = FallbackIconIds[pair.Key];
                if (byName.TryGetValue(pair.Value, out var found) && found.Icon != 0)
                {
                    resolved[pair.Key] = found.Icon;
                    audit.Add(new StatusIconAudit(
                        pair.Key, pair.Value, fallback, found.Icon, found.RowId,
                        pair.Value, found.Icon == fallback));
                }
                else
                {
                    audit.Add(new StatusIconAudit(
                        pair.Key, pair.Value, fallback, null, null, null, false));
                }
            }
        }
        catch (Exception exception)
        {
            log.Warning(exception, "KefkaP4Trainer could not resolve status icons from game data.");
            return;
        }

        iconIds = resolved;
        Audit = audit;

        var mismatched = audit.Count(entry => !entry.Matched);
        if (mismatched > 0)
        {
            log.Warning(
                "KefkaP4Trainer resolved {Count} status icon(s) that disagreed with the "
                + "hardcoded table; see the debug window icon atlas.",
                mismatched);
        }
    }

    public uint IconIdFor(DebuffKind kind) =>
        iconIds.TryGetValue(kind, out var id) ? id : 0;

    /// <summary>
    /// Gets the icon to draw this frame, or null if the id is unknown or the
    /// texture has not finished loading. Callers fall back to a flat tile rather
    /// than blocking, because the provider loads asynchronously and unloads
    /// icons that go undrawn for a couple of seconds.
    /// </summary>
    public IDalamudTextureWrap? TryGet(DebuffKind kind) =>
        iconIds.TryGetValue(kind, out var iconId)
            ? textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault()
            : null;
}
