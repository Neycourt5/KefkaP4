using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Windows;

internal sealed class MainWindow : Window
{
    private readonly ITrainerWindowHost host;
    private string seedText;
    private long displayedSeed;
    private string? seedError;
    private string? arenaMessage;

    public MainWindow(ITrainerWindowHost host)
        : base("Kefka P4 Trainer###KefkaP4TrainerMain")
    {
        this.host = host;
        displayedSeed = host.Engine.Seed;
        seedText = displayedSeed.ToString();

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(470, 410),
            MaximumSize = new Vector2(760, 900),
        };
    }

    public override void Draw()
    {
        DrawSessionStatus();
        Section("Arena");
        DrawArenaControls();
        Section("Simulation");
        DrawSimulationControls();
        Section("Current assignment");
        DrawCurrentAssignment();
        Section("Most recent result");
        DrawLastResult();
        Section("Windows");
        DrawWindowButtons();
    }

    private void DrawSessionStatus()
    {
        var engine = host.Engine;
        var clock = engine.Clock;
        var stateColor = clock.State switch
        {
            SimulationState.Running => new Vector4(0.35f, 0.95f, 0.45f, 1),
            SimulationState.Countdown => new Vector4(1, 0.78f, 0.25f, 1),
            SimulationState.Paused => new Vector4(1, 0.72f, 0.25f, 1),
            SimulationState.Completed => new Vector4(0.4f, 0.8f, 1, 1),
            _ => new Vector4(0.72f, 0.72f, 0.72f, 1),
        };

        ImGui.TextColored(stateColor, clock.State.ToString().ToUpperInvariant());
        ImGui.SameLine();
        ImGui.TextUnformatted($"Pull {engine.PullNumber}  |  Seed {engine.Seed}");

        if (clock.State == SimulationState.Countdown)
        {
            ImGui.TextUnformatted($"Pull begins in {Math.Abs(clock.Time):0.0}s");
        }
        else
        {
            ImGui.TextUnformatted(
                $"Timeline {FormatTime(clock.Time)} / {FormatTime(KefkaP4Constants.EncounterLength)}"
                + $"  ({clock.PlaybackSpeed:0.0}x)");
        }

        var progress = (float)Math.Clamp(
            clock.Time / KefkaP4Constants.EncounterLength,
            0,
            1);
        ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{progress * 100:0}%");

        if (engine.NextEvent is { } next)
        {
            ImGui.TextDisabled($"Next: {next.Name} at {FormatTime(next.Time)}");
        }
        else
        {
            ImGui.TextDisabled("Next: none");
        }
    }

    private void DrawArenaControls()
    {
        if (ImGui.Button("Set from target"))
        {
            arenaMessage = host.SetArena(ArenaAnchorMode.Target)
                ? "Arena snapped to the current target."
                : "No valid target was available.";
        }

        ImGui.SameLine();
        if (ImGui.Button("Set from player"))
        {
            arenaMessage = host.SetArena(ArenaAnchorMode.Player)
                ? "Arena snapped to the local player."
                : "No valid local player was available.";
        }

        ImGui.SameLine();
        if (ImGui.Button("Set manual"))
        {
            arenaMessage = host.SetArena(ArenaAnchorMode.Manual)
                ? "Arena snapped to the configured manual center."
                : "The manual arena configuration was invalid.";
        }

        if (host.Transform.IsInitialized)
        {
            var center = host.Transform.Center;
            ImGui.TextDisabled(
                $"Center X {center.X:0.000}, Y {center.Y:0.000}, Z {center.Z:0.000}"
                + $"  |  North {host.Transform.RotationDegrees:0.0} deg");
        }
        else
        {
            ImGui.TextColored(
                new Vector4(1, 0.55f, 0.25f, 1),
                "Arena is not initialized. Set it before starting.");
        }

        if (!string.IsNullOrWhiteSpace(arenaMessage))
        {
            ImGui.TextWrapped(arenaMessage);
        }
    }

    private void DrawSimulationControls()
    {
        var engine = host.Engine;
        var state = engine.Clock.State;
        var active = state is SimulationState.Running or SimulationState.Countdown;
        var canStart = state is SimulationState.Stopped or SimulationState.Completed;

        ImGui.BeginDisabled(!canStart);
        if (ImGui.Button("Start"))
        {
            if (host.Start() && !host.Configuration.MainWindowVisibleAfterStart)
            {
                host.MainWindowVisible = false;
            }
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (state == SimulationState.Paused)
        {
            if (ImGui.Button("Resume"))
            {
                host.Resume();
            }
        }
        else
        {
            ImGui.BeginDisabled(!active);
            if (ImGui.Button("Pause"))
            {
                host.Pause();
            }
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(state == SimulationState.Stopped);
        if (ImGui.Button("Stop"))
        {
            host.Stop();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Reset"))
        {
            host.Reset();
        }

        ImGui.SameLine();
        if (ImGui.Button("Restart"))
        {
            host.Reset();
            if (host.Start() && !host.Configuration.MainWindowVisibleAfterStart)
            {
                host.MainWindowVisible = false;
            }
        }

        DrawSeedControls();

        var speed = host.Configuration.PlaybackSpeed;
        ImGui.SetNextItemWidth(210);
        if (ImGui.SliderFloat("Playback speed", ref speed, 0.1f, 4f, "%.1fx"))
        {
            host.Configuration.PlaybackSpeed = speed;
            engine.Clock.PlaybackSpeed = speed;
            host.SaveConfiguration();
        }

        var countdown = host.Configuration.CountdownSeconds;
        ImGui.SetNextItemWidth(210);
        if (ImGui.SliderFloat("Countdown", ref countdown, 0, 15, "%.1f s"))
        {
            host.Configuration.CountdownSeconds = countdown;
            host.SaveConfiguration();
        }

        var role = host.Configuration.PlayerRole;
        if (RoleCombo("Player role", ref role))
        {
            host.Configuration.PlayerRole = role;
            engine.SetPlayerRole(role);
            host.SaveConfiguration();
        }

        var failureBehavior = host.Configuration.FailureBehavior;
        if (EnumCombo("On failure", ref failureBehavior))
        {
            host.Configuration.FailureBehavior = failureBehavior;
            engine.FailureBehavior = failureBehavior;
            host.SaveConfiguration();
        }

        var newSeed = host.Configuration.NewSeedEveryPull;
        if (ImGui.Checkbox("New seed every pull", ref newSeed))
        {
            host.Configuration.NewSeedEveryPull = newSeed;
            host.SaveConfiguration();
        }

        ImGui.TextUnformatted("Practice mode: Full Kefka P4 sequence");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "The source simulator implements one complete P4 timeline;"
                + " it does not define faithful individual-mechanic modes.");
        }
    }

    private void DrawSeedControls()
    {
        var currentSeed = host.Engine.Seed;
        if (currentSeed != displayedSeed)
        {
            displayedSeed = currentSeed;
            seedText = currentSeed.ToString();
            seedError = null;
        }

        ImGui.SetNextItemWidth(210);
        ImGui.InputText("Seed", ref seedText, 32);
        ImGui.SameLine();
        if (ImGui.Button("Apply##Seed"))
        {
            if (long.TryParse(seedText, out var parsed))
            {
                host.SetSeed(parsed);
                displayedSeed = parsed;
                seedText = parsed.ToString();
                seedError = null;
            }
            else
            {
                seedError = "Seed must be a signed 64-bit integer.";
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Randomize##Seed"))
        {
            host.RandomizeSeed();
            displayedSeed = host.Engine.Seed;
            seedText = displayedSeed.ToString();
            seedError = null;
        }

        ImGui.SameLine();
        if (ImGui.Button("Copy##Seed"))
        {
            ImGui.SetClipboardText(host.Engine.Seed.ToString());
        }

        if (seedError is not null)
        {
            ImGui.TextColored(new Vector4(1, 0.35f, 0.35f, 1), seedError);
        }
    }

    private void DrawCurrentAssignment()
    {
        var engine = host.Engine;
        var time = Math.Max(0, engine.Clock.Time);
        var cue = engine.Encounter.CurrentCue(time);
        var assignment = engine.Encounter.AssignmentSummary(time);

        if (!string.IsNullOrWhiteSpace(cue))
        {
            ImGui.TextColored(new Vector4(0.85f, 0.75f, 1, 1), cue);
        }

        ImGui.TextWrapped(assignment);
        if (engine.Encounter.RequiredPosition is { } required)
        {
            ImGui.TextDisabled($"Suggested source position: ({required.X:0.00}, {required.Y:0.00})");
        }
    }

    private void DrawLastResult()
    {
        var result = host.Engine.LastResult;
        if (result is null)
        {
            ImGui.TextDisabled("No mechanic has been evaluated this session.");
        }
        else
        {
            var color = result.Passed
                ? new Vector4(0.3f, 1, 0.4f, 1)
                : new Vector4(1, 0.3f, 0.3f, 1);
            ImGui.TextColored(color, result.Passed ? "PASS" : $"FAIL: {result.Reason}");
            ImGui.TextDisabled(
                $"Pull {result.PullNumber}, {result.Mechanic} at {FormatTime(result.Timestamp)}, "
                + $"position ({result.PlayerArenaPosition.X:0.00}, {result.PlayerArenaPosition.Y:0.00})");
        }

        ImGui.TextUnformatted($"Results recorded: {host.Engine.History.Count}");
        ImGui.SameLine();
        ImGui.BeginDisabled(host.Engine.History.Count == 0);
        if (ImGui.Button("Clear results"))
        {
            host.ClearHistory();
        }
        ImGui.EndDisabled();
    }

    private void DrawWindowButtons()
    {
        if (ImGui.Button("Settings"))
        {
            host.ConfigWindowVisible = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Debug"))
        {
            host.DebugWindowVisible = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Hide controls"))
        {
            host.MainWindowVisible = false;
        }
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

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.86f, 0.72f, 1, 1), title);
        ImGui.Separator();
    }

    private static string FormatTime(double time)
    {
        var safeTime = Math.Max(0, time);
        var minutes = (int)(safeTime / 60);
        var seconds = safeTime - (minutes * 60);
        return $"{minutes:00}:{seconds:00.0}";
    }
}
