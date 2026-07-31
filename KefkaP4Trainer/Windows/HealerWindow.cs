using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Health;
using KefkaP4Trainer.Healing;

namespace KefkaP4Trainer.Windows;

/// <summary>
/// The simulated party panel and healer-observation diagnostics.
/// </summary>
/// <remarks>
/// A standalone window rather than an overlay on the native party list. The
/// bars here are entirely simulated and are drawn in the plugin's own style so
/// they cannot be mistaken for real party HP. Nothing in this window reads or
/// writes real health, and no native UI node is touched.
/// </remarks>
internal sealed class HealerWindow : Window
{
    private static readonly Vector4 HpColor = new(0.30f, 0.80f, 0.35f, 1);
    private static readonly Vector4 HpLowColor = new(0.90f, 0.65f, 0.20f, 1);
    private static readonly Vector4 HpCriticalColor = new(0.95f, 0.25f, 0.25f, 1);
    private static readonly Vector4 ShieldColor = new(0.95f, 0.90f, 0.55f, 1);
    private static readonly Vector4 DeadColor = new(0.45f, 0.45f, 0.50f, 1);

    private readonly ITrainerWindowHost host;
    private int manualDamage = 40_000;
    private int manualHeal = 20_000;

    public HealerWindow(ITrainerWindowHost host)
        : base("Kefka P4 Healer Practice###KefkaP4TrainerHealer")
    {
        this.host = host;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 520),
            MaximumSize = new Vector2(1100, 1200),
        };
    }

    public override void Draw()
    {
        var service = host.HealerPractice;
        var configuration = host.Configuration;

        DrawModeControls(configuration);
        Section("Co-healer");
        DrawCoHealer(service, configuration);
        Section("Simulated party");
        DrawParty(service, host.Engine.Clock.Time);
        Section("Observation");
        DrawObservation(service);
        Section("Debug controls");
        DrawDebugControls(service);
        Section("Action log");
        DrawActionLog(service);
        Section("Party health history");
        DrawHealthHistory(service);
    }

    private void DrawModeControls(Configuration configuration)
    {
        var enabled = configuration.HealerPracticeEnabled;
        if (ImGui.Checkbox("Enable healer practice", ref enabled))
        {
            configuration.HealerPracticeEnabled = enabled;
            host.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Off by default. The party below is entirely simulated: no real HP,\n"
                + "no real statuses, and nothing is ever applied to a real character.");
        }

        ImGui.SameLine();
        var damage = configuration.HealerDamageEnabled;
        if (ImGui.Checkbox("Run scripted damage", ref damage))
        {
            configuration.HealerDamageEnabled = damage;
            host.SaveConfiguration();
        }

        var maximumHp = configuration.HealerSimulatedMaximumHp;
        ImGui.SetNextItemWidth(220);
        if (ImGui.InputInt("Simulated max HP", ref maximumHp, 1_000, 10_000))
        {
            configuration.HealerSimulatedMaximumHp = Math.Clamp(maximumHp, 1_000, 2_000_000);
            host.SaveConfiguration();
            host.HealerPractice.Party.SetMaximumHp(configuration.HealerSimulatedMaximumHp);
        }

        ImGui.TextDisabled(
            $"Damage is scaled from a {DamageScaling.DefaultReferenceMaximumHp:N0} reference pool, "
            + "so severity holds as this changes.");
    }

    /// <summary>
    /// Co-healer controls and what it is currently covering.
    /// </summary>
    /// <remarks>
    /// Its contribution is never hidden: every shield, mitigation, regen and
    /// heal it provides appears in the action log and the party history
    /// attributed to "co-healer", and boss damage is never quietly reduced.
    /// </remarks>
    private void DrawCoHealer(HealerPracticeService service, Configuration configuration)
    {
        var job = service.PlayerJob;
        var profile = job.Profile();

        if (profile == HealerProfile.None)
        {
            ImGui.TextDisabled(
                $"Your job ({job}) is not a healer, so no co-healer is offered.");
            return;
        }

        ImGui.TextUnformatted(
            $"You are {job} ({profile}). A {profile.Complement()} co-healer complements you.");

        var level = configuration.CoHealerAssistance;
        if (EnumCombo("Assistance", ref level))
        {
            configuration.CoHealerAssistance = level;
            host.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Minimal  - party mitigation on alternate raidwides only.\n"
                + "Standard - adds a shield or party heal on those same raidwides.\n"
                + "           The raidwides in between are yours.\n"
                + "Strong   - covers every raidwide, plus regens and one emergency\n"
                + "           heal on a long cooldown. This can carry the phase.");
        }

        if (service.CoHealer is not { } coHealer)
        {
            ImGui.TextDisabled("No co-healer active.");
            return;
        }

        var settings = coHealer.Settings;
        ImGui.TextUnformatted(
            $"{coHealer.SourceName}: mitigation {Yes(settings.Mitigation)}, "
            + $"core healing {Yes(settings.CoreHealing)}, regens {Yes(settings.Regens)}, "
            + $"emergency {Yes(settings.EmergencyHealing)}");
        ImGui.TextDisabled(
            settings.CoverageStride <= 1
                ? "Covering every raidwide."
                : $"Covering one raidwide in {settings.CoverageStride}; the rest are yours.");
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

    private static void DrawParty(HealerPracticeService service, double time)
    {
        var party = service.Party;
        ImGui.TextUnformatted(
            $"Alive {party.LivingCount}/8" + (party.IsWiped ? "   WIPED" : string.Empty));

        foreach (var member in party.Members)
        {
            var shield = member.TotalShieldAt(time);
            var mitigation = member.MitigationFractionAt(time);
            var fraction = member.HpFraction;
            var color = !member.IsAlive
                ? DeadColor
                : fraction < 0.25f ? HpCriticalColor
                : fraction < 0.6f ? HpLowColor : HpColor;

            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
            ImGui.ProgressBar(
                fraction,
                new Vector2(240, 0),
                member.IsAlive ? $"{member.CurrentHp:N0}" : "DEAD");
            ImGui.PopStyleColor();

            ImGui.SameLine();
            ImGui.TextUnformatted($"{member.DisplayName} ({member.Job})");

            var extras = new List<string>();
            if (shield > 0)
            {
                extras.Add($"shield {shield:N0}");
            }

            if (mitigation > 0)
            {
                extras.Add($"mit {mitigation * 100:0.#}%");
            }

            if (member.Regens.Count > 0)
            {
                extras.Add($"regen x{member.Regens.Count}");
            }

            if (!member.IsAlive && member.DeathReason is { } reason)
            {
                extras.Add($"died to {reason}");
            }

            if (extras.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(ShieldColor, string.Join("  ", extras));
            }
        }
    }

    private static void DrawObservation(HealerPracticeService service)
    {
        var capabilities = service.Observer.Capabilities;
        ImGui.TextUnformatted(
            $"Casted: {Yes(capabilities.CastedActions)}   "
            + $"Instant/oGCD: {Yes(capabilities.InstantOgcds)}   "
            + $"Statuses: {Yes(capabilities.StatusEffects)}   "
            + $"Targets: {Yes(capabilities.TargetResolution)}");
        ImGui.TextWrapped(capabilities.Notes);

        if (service.Observer is not HealerActionObserver observer)
        {
            return;
        }

        if (observer.NameMismatches.Count == 0)
        {
            ImGui.TextColored(
                new Vector4(0.35f, 0.95f, 0.45f, 1),
                $"All {HealerActionDatabase.All.Count} action ids match the game's Action sheet.");
            return;
        }

        ImGui.TextColored(
            new Vector4(1, 0.55f, 0.25f, 1),
            $"{observer.NameMismatches.Count} action id(s) disagree with the game sheet:");
        foreach (var mismatch in observer.NameMismatches)
        {
            ImGui.BulletText(mismatch);
        }
    }

    private void DrawDebugControls(HealerPracticeService service)
    {
        var time = host.Engine.Clock.Time;
        var party = service.Party;

        ImGui.SetNextItemWidth(160);
        ImGui.InputInt("Damage amount", ref manualDamage, 1_000, 10_000);
        ImGui.SameLine();
        if (ImGui.Button("Raidwide"))
        {
            _ = party.ApplyRawDamage("Debug Raidwide", manualDamage, DamageTargetRule.Party, time);
        }

        ImGui.SameLine();
        if (ImGui.Button("Tanks"))
        {
            _ = party.ApplyRawDamage("Debug Tankbuster", manualDamage, DamageTargetRule.Tanks, time);
        }

        ImGui.SameLine();
        if (ImGui.Button("Your slot"))
        {
            _ = party.ApplyRawDamage(
                "Debug Targeted", manualDamage, DamageTargetRule.Slot, time, host.Engine.PlayerRole);
        }

        ImGui.SetNextItemWidth(160);
        ImGui.InputInt("Heal amount", ref manualHeal, 1_000, 10_000);
        ImGui.SameLine();
        if (ImGui.Button("Heal party"))
        {
            _ = party.HealParty("Debug Heal", "debug", manualHeal, time);
        }

        ImGui.SameLine();
        if (ImGui.Button("Shield party"))
        {
            _ = party.ShieldParty("Debug Shield", "debug", manualHeal, 30, time);
        }

        if (ImGui.Button("Party mit 20%"))
        {
            party.MitigateParty("Debug Mitigation", "debug", 0.20f, 15, time);
        }

        ImGui.SameLine();
        if (ImGui.Button("Party regen"))
        {
            party.RegenParty("Debug Regen", "debug", manualHeal / 10, 3, 15, time);
        }

        ImGui.SameLine();
        if (ImGui.Button("Raise all"))
        {
            foreach (var member in party.Members.Where(m => !m.IsAlive).ToList())
            {
                party.Raise(member.Slot, "debug", time);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset party"))
        {
            service.ResetPull(time);
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Inject a real action (bypasses observation):");
        var injectable = HealerActionDatabase.All
            .Where(d => d.Kind is HealerActionKind.PartyMitigation or HealerActionKind.Shield)
            .OrderBy(d => d.Name)
            .Take(8)
            .ToList();

        for (var index = 0; index < injectable.Count; index++)
        {
            var definition = injectable[index];
            if (index % 4 != 0)
            {
                ImGui.SameLine();
            }

            if (ImGui.Button($"{definition.Name}##inject{definition.ActionId}"))
            {
                service.Inject(definition.ActionId, definition.Name, time, null);
            }
        }
    }

    private static void DrawActionLog(HealerPracticeService service)
    {
        if (service.Log.Count == 0)
        {
            ImGui.TextDisabled("No actions observed this pull.");
            return;
        }

        foreach (var entry in service.Log.TakeLast(12).Reverse())
        {
            var action = entry.Action;
            ImGui.TextColored(
                entry.Recognised
                    ? new Vector4(0.35f, 0.95f, 0.45f, 1)
                    : new Vector4(1, 0.55f, 0.25f, 1),
                $"{action.SimulationTime,7:0.0}s  {action.ActionName} (id {action.ActionId})");
            ImGui.TextDisabled(
                $"        {action.Method} / {action.Confidence} / "
                + $"{(action.WasCast ? "cast" : "instant")} / key {action.DuplicateKey}");
            ImGui.TextUnformatted($"        {entry.Summary}");
        }
    }

    private static void DrawHealthHistory(HealerPracticeService service)
    {
        var history = service.Party.History;
        if (history.Count == 0)
        {
            ImGui.TextDisabled("No party health events yet.");
            return;
        }

        foreach (var entry in history.TakeLast(14).Reverse())
        {
            var slot = entry.Slot is { } role ? role.Key() : "party";
            ImGui.TextUnformatted($"{entry.Time,7:0.0}s  [{entry.Kind}] {slot}: {entry.Summary}");
        }
    }

    private static string Yes(bool value) => value ? "yes" : "no";

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.86f, 0.72f, 1, 1), title);
        ImGui.Separator();
    }
}
