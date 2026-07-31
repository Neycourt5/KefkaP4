using System.Globalization;
using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Healing;
using KefkaP4Trainer.Rendering;
using KefkaP4Trainer.Windows;

namespace KefkaP4Trainer;

public sealed class Plugin : IDalamudPlugin, ITrainerWindowHost
{
    private const string CommandName = "/kefkap4";

    private readonly WindowSystem windowSystem = new("KefkaP4Trainer");
    private readonly GameStateReader gameStateReader = new();
    private readonly List<SimulatedGhost> testGhosts = [];
    private int testGhostCounter;
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly DebugWindow debugWindow;
    private readonly HealerWindow healerWindow;
    private readonly HealerActionObserver healerObserver = new();
    private readonly ArenaOverlayRenderer arenaOverlayRenderer;
    private readonly StatusHudRenderer statusHudRenderer;
    private readonly ResultOverlayRenderer resultOverlayRenderer;
    private readonly CastBarRenderer castBarRenderer;
    private readonly CountdownRenderer countdownRenderer;
    private readonly SoundCueService soundCues = new();
    private readonly MitigationRenderer mitigationRenderer;
    private bool disposed;
    private bool pausedForMissingPlayer;
    private bool updateFaulted;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        // Dalamud injects constructor parameters, not static members. This call
        // populates the [PluginService] statics on Services; without it every one
        // of them stays null and the first access below throws.
        pluginInterface.Create<Services>();

        Configuration =
            Services.PluginInterface.GetPluginConfig() as Configuration
            ?? new Configuration();
        NormalizeConfiguration();

        Transform = new ArenaTransform();
        Engine = new SimulationEngine(Configuration.Seed, Configuration.PlayerRole)
        {
            FailureBehavior = Configuration.FailureBehavior,
        };
        Engine.Clock.PlaybackSpeed = Configuration.PlaybackSpeed;

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        debugWindow = new DebugWindow(this);
        HealerPractice = new HealerPracticeService(healerObserver);
        HealerPractice.Party.SetMaximumHp(Configuration.HealerSimulatedMaximumHp);
        healerObserver.VerifyAgainstGameData();
        healerWindow = new HealerWindow(this);
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(debugWindow);
        windowSystem.AddWindow(healerWindow);
        arenaOverlayRenderer = new ArenaOverlayRenderer(Services.GameGui, Services.Log);
        statusHudRenderer = new StatusHudRenderer(Services.Log, Services.TextureProvider);
        resultOverlayRenderer = new ResultOverlayRenderer(Services.Log);
        castBarRenderer = new CastBarRenderer(Services.Log);
        countdownRenderer = new CountdownRenderer(Services.Log);
        mitigationRenderer = new MitigationRenderer(Services.Log);

        Services.CommandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage = "Open the local Kefka P4 practice trainer.",
                ShowInHelp = true,
            });
        Services.Framework.Update += OnFrameworkUpdate;
        Services.PluginInterface.UiBuilder.Draw += Draw;
        Services.PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        Services.PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
    }

    public Configuration Configuration { get; }

    public SimulationEngine Engine { get; }

    public ArenaTransform Transform { get; }

    public PlayerState LastPlayer { get; private set; } = PlayerState.Unavailable;

    public Vector3? LastPlayerWorldPosition => gameStateReader.LastWorldPosition;

    public float? LastPlayerGameRotation => gameStateReader.LastGameRotation;

    public IReadOnlyList<SimulatedGhost> TestGhosts => testGhosts;

    public int LastProjectionFailureCount => arenaOverlayRenderer.LastProjectionFailureCount;

    public bool MainWindowVisible
    {
        get => mainWindow.IsOpen;
        set => mainWindow.IsOpen = value;
    }

    public bool ConfigWindowVisible
    {
        get => configWindow.IsOpen;
        set => configWindow.IsOpen = value;
    }

    public bool DebugWindowVisible
    {
        get => debugWindow.IsOpen;
        set => debugWindow.IsOpen = value;
    }

    public bool HealerWindowVisible
    {
        get => healerWindow.IsOpen;
        set => healerWindow.IsOpen = value;
    }

    public HealerPracticeService HealerPractice { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Services.Framework.Update -= OnFrameworkUpdate;
        Services.PluginInterface.UiBuilder.Draw -= Draw;
        Services.PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        Services.CommandManager.RemoveHandler(CommandName);
        windowSystem.RemoveAllWindows();

        try
        {
            Configuration.Save();
        }
        catch (Exception exception)
        {
            Services.Log.Error(exception, "Failed to save KefkaP4Trainer configuration during disposal.");
        }
    }

    public bool Start()
    {
        if (!SetArena(Configuration.AnchorMode))
        {
            Services.Chat.PrintError(
                $"[KefkaP4Trainer] Could not snapshot the {Configuration.AnchorMode.ToString().ToLowerInvariant()} "
                + "arena anchor. Select a valid target/player or configure a finite manual center.");
            return false;
        }

        if (Configuration.NewSeedEveryPull)
        {
            SetSeed(Random.Shared.NextInt64());
        }

        if (Engine.PlayerRole != Configuration.PlayerRole)
        {
            Engine.SetPlayerRole(Configuration.PlayerRole);
        }

        if (Engine.Seed != Configuration.Seed)
        {
            Engine.SetSeed(Configuration.Seed);
        }

        Engine.FailureBehavior = Configuration.FailureBehavior;
        Engine.Clock.PlaybackSpeed = Configuration.PlaybackSpeed;
        gameStateReader.ResetVelocitySample();
        pausedForMissingPlayer = false;
        updateFaulted = false;
        soundCues.Reset();
        Engine.Start(Configuration.CountdownSeconds);
        SynchronizeAutomaticRestartSeed();
        return true;
    }

    public void Pause()
    {
        pausedForMissingPlayer = false;
        Engine.Pause();
    }

    public void Resume()
    {
        pausedForMissingPlayer = false;
        updateFaulted = false;
        Engine.Resume();
    }

    public void Stop()
    {
        Engine.Stop();
        gameStateReader.ResetVelocitySample();
        pausedForMissingPlayer = false;
    }

    public void Reset()
    {
        Engine.Reset();
        gameStateReader.ResetVelocitySample();
        pausedForMissingPlayer = false;
        updateFaulted = false;
    }

    public bool SetArena(ArenaAnchorMode anchorMode)
    {
        try
        {
            Vector3 center;
            switch (anchorMode)
            {
                case ArenaAnchorMode.Target:
                {
                    var target = Services.TargetManager.Target;
                    if (target is null)
                    {
                        return false;
                    }

                    center = target.Position;
                    break;
                }

                case ArenaAnchorMode.Player:
                {
                    var player = Services.ObjectTable.LocalPlayer;
                    if (player is null)
                    {
                        return false;
                    }

                    center = player.Position;
                    break;
                }

                case ArenaAnchorMode.Manual:
                    center = new Vector3(
                        Configuration.ManualCenterX,
                        Configuration.ManualCenterY,
                        Configuration.ManualCenterZ);
                    break;

                default:
                    return false;
            }

            if (!IsFinite(center))
            {
                return false;
            }

            var clockwiseRotation = Configuration.NorthMode switch
            {
                ArenaNorthMode.WorldNorth => 0,
                ArenaNorthMode.Manual =>
                    Configuration.ManualRotationDegrees * (MathF.PI / 180f),
                ArenaNorthMode.PlayerFacing => GetPlayerFacingRotation(),
                _ => float.NaN,
            };
            if (!float.IsFinite(clockwiseRotation))
            {
                return false;
            }

            Configuration.AnchorMode = anchorMode;
            Transform.Set(center, clockwiseRotation);
            gameStateReader.ResetVelocitySample();
            SaveConfiguration();
            return true;
        }
        catch (Exception exception)
        {
            Services.Log.Error(exception, "Failed to snapshot the KefkaP4Trainer arena.");
            return false;
        }
    }

    public void SetSeed(long seed)
    {
        if (Engine.Clock.State is not (SimulationState.Stopped or SimulationState.Completed))
        {
            Stop();
        }

        Configuration.Seed = seed;
        Engine.SetSeed(seed);
        SaveConfiguration();
    }

    public void RandomizeSeed() => SetSeed(Random.Shared.NextInt64());

    public void ClearHistory() => Engine.ClearResults();

    public void AddTestGhost(Vector2 arenaPosition, bool gazeSource)
    {
        if (!float.IsFinite(arenaPosition.X) || !float.IsFinite(arenaPosition.Y))
        {
            Services.Chat.PrintError("[KefkaP4Trainer] Test ghost coordinates must be finite.");
            return;
        }

        testGhostCounter++;
        testGhosts.Add(new SimulatedGhost(
            $"test{testGhostCounter}",
            arenaPosition,
            Geometry.AngleFromDirection(-arenaPosition),
            double.NegativeInfinity,
            double.PositiveInfinity,
            gazeSource,
            gazeSource ? GhostVisualState.GazeSource : GhostVisualState.Bystander));
    }

    public void ClearTestGhosts()
    {
        testGhosts.Clear();
        testGhostCounter = 0;
    }

    public void SaveConfiguration()
    {
        NormalizeConfiguration();
        Engine.Clock.PlaybackSpeed = Configuration.PlaybackSpeed;
        Engine.FailureBehavior = Configuration.FailureBehavior;
        Configuration.Save();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            var elapsedSeconds = framework.UpdateDelta.TotalSeconds;
            LastPlayer = gameStateReader.Read(Transform, elapsedSeconds);
            Engine.Clock.PlaybackSpeed = Configuration.PlaybackSpeed;
            Engine.FailureBehavior = Configuration.FailureBehavior;
            Engine.Encounter.SwapAntilightColors = Configuration.SwapAntilightColors;
            if (!LastPlayer.IsValid)
            {
                if (Engine.Clock.State is SimulationState.Running or SimulationState.Countdown)
                {
                    Engine.Pause();
                    pausedForMissingPlayer = true;
                }

                return;
            }

            if (pausedForMissingPlayer)
            {
                pausedForMissingPlayer = false;
                Engine.Resume();
            }

            SynchronizeAutomaticRestartSeed();
            soundCues.Update(Engine, Configuration);
            var pullBeforeUpdate = Engine.PullNumber;
            Engine.Update(elapsedSeconds, LastPlayer);
            HealerPractice.Update(Engine, Configuration);
            if (Engine.PullNumber != pullBeforeUpdate)
            {
                Configuration.Seed = Engine.Seed;
                SaveConfiguration();
                SynchronizeAutomaticRestartSeed();
            }
        }
        catch (Exception exception)
        {
            if (!updateFaulted)
            {
                updateFaulted = true;
                Services.Log.Error(exception, "KefkaP4Trainer simulation update failed; the clock was paused.");
                Services.Chat.PrintError(
                    "[KefkaP4Trainer] The simulation update failed and was paused. See the Dalamud log.");
            }

            Engine.Pause();
        }
    }

    private void Draw()
    {
        try
        {
            arenaOverlayRenderer.Draw(Transform, Engine, LastPlayer, Configuration, testGhosts);
            statusHudRenderer.Draw(Engine, Configuration);
            castBarRenderer.Draw(Engine, Configuration);
            countdownRenderer.Draw(Engine, Configuration);
            mitigationRenderer.Draw(Engine, Configuration, MitSlotResolver.Resolve(Configuration));
            resultOverlayRenderer.Draw(Engine);
            windowSystem.Draw();
        }
        catch (Exception exception)
        {
            Services.Log.Error(exception, "KefkaP4Trainer window draw failed.");
        }
    }

    private void OnCommand(string command, string arguments)
    {
        try
        {
            var parts = arguments.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                MainWindowVisible = !MainWindowVisible;
                return;
            }

            switch (parts[0].ToLowerInvariant())
            {
                case "start" when parts.Length == 1:
                    Start();
                    break;
                case "stop" when parts.Length == 1:
                    Stop();
                    break;
                case "pause" when parts.Length == 1:
                    Pause();
                    break;
                case "resume" when parts.Length == 1:
                    Resume();
                    break;
                case "reset" when parts.Length == 1:
                    Reset();
                    break;
                case "restart" when parts.Length == 1:
                    Reset();
                    Start();
                    break;
                case "config" when parts.Length == 1:
                    ConfigWindowVisible = !ConfigWindowVisible;
                    break;
                case "debug" when parts.Length == 1:
                    DebugWindowVisible = !DebugWindowVisible;
                    break;
                case "healer" when parts.Length == 1:
                    HealerWindowVisible = !HealerWindowVisible;
                    break;
                case "setarena" when parts.Length == 2:
                    SetArenaCommand(parts[1]);
                    break;
                case "seed" when parts.Length == 2:
                    SetSeedCommand(parts[1]);
                    break;
                case "speed" when parts.Length == 2:
                    SetSpeedCommand(parts[1]);
                    break;
                case "help" when parts.Length == 1:
                    PrintHelp();
                    break;
                default:
                    Services.Chat.PrintError(
                        "[KefkaP4Trainer] Invalid command. Use /kefkap4 help for supported arguments.");
                    break;
            }
        }
        catch (Exception exception)
        {
            Services.Log.Error(exception, "Unhandled KefkaP4Trainer command failure: {Arguments}", arguments);
            Services.Chat.PrintError(
                "[KefkaP4Trainer] The command failed safely. See the Dalamud log for details.");
        }
    }

    private void SetArenaCommand(string argument)
    {
        var mode = argument.ToLowerInvariant() switch
        {
            "player" => ArenaAnchorMode.Player,
            "target" => ArenaAnchorMode.Target,
            "manual" => ArenaAnchorMode.Manual,
            _ => (ArenaAnchorMode?)null,
        };
        if (mode is null)
        {
            Services.Chat.PrintError(
                "[KefkaP4Trainer] Usage: /kefkap4 setarena <player|target|manual>");
            return;
        }

        if (!SetArena(mode.Value))
        {
            Services.Chat.PrintError(
                $"[KefkaP4Trainer] No valid {argument.ToLowerInvariant()} arena anchor is available.");
            return;
        }

        var center = Transform.Center;
        Services.Chat.Print(
            $"[KefkaP4Trainer] Arena set to X {center.X:0.000}, Z {center.Z:0.000}, "
            + $"clockwise north rotation {Transform.RotationDegrees:0.0} degrees.");
    }

    private void SetSeedCommand(string argument)
    {
        if (!long.TryParse(
                argument,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var seed))
        {
            Services.Chat.PrintError(
                "[KefkaP4Trainer] Seed must be a signed 64-bit integer.");
            return;
        }

        SetSeed(seed);
        Services.Chat.Print($"[KefkaP4Trainer] Seed set to {seed}.");
    }

    private void SetSpeedCommand(string argument)
    {
        if (!float.TryParse(
                argument,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var speed)
            || !float.IsFinite(speed)
            || speed < 0.1f
            || speed > 4)
        {
            Services.Chat.PrintError(
                "[KefkaP4Trainer] Speed must be a number from 0.1 through 4.0.");
            return;
        }

        Configuration.PlaybackSpeed = speed;
        SaveConfiguration();
        Services.Chat.Print($"[KefkaP4Trainer] Playback speed set to {speed:0.0}x.");
    }

    private static void PrintHelp() =>
        Services.Chat.Print(
            "[KefkaP4Trainer] /kefkap4 [start|stop|pause|resume|reset|restart|config|debug]\n"
            + "  /kefkap4 setarena <player|target|manual>\n"
            + "  /kefkap4 seed <integer>\n"
            + "  /kefkap4 speed <0.1-4.0>");

    private float GetPlayerFacingRotation()
    {
        var player = Services.ObjectTable.LocalPlayer;
        return player is null
            ? float.NaN
            : ArenaTransform.RotationFromFfxivFacing(player.Rotation);
    }

    private void OpenMainUi() => MainWindowVisible = true;

    private void OpenConfigUi() => ConfigWindowVisible = true;

    private void SynchronizeAutomaticRestartSeed()
    {
        if (Configuration.NewSeedEveryPull
            && Configuration.FailureBehavior == FailureBehavior.Restart
            && Engine.Clock.State is SimulationState.Running or SimulationState.Countdown)
        {
            Engine.NextAutomaticRestartSeed ??= Random.Shared.NextInt64();
        }
        else
        {
            Engine.NextAutomaticRestartSeed = null;
        }
    }

    private void NormalizeConfiguration()
    {
        Configuration.PlaybackSpeed = Math.Clamp(
            float.IsFinite(Configuration.PlaybackSpeed)
                ? Configuration.PlaybackSpeed
                : 1,
            0.1f,
            4);
        Configuration.CountdownSeconds = Math.Clamp(
            float.IsFinite(Configuration.CountdownSeconds)
                ? Configuration.CountdownSeconds
                : 3,
            0,
            30);
        Configuration.FillOpacity = FiniteClamp(Configuration.FillOpacity, 0.24f, 0, 1);
        Configuration.OutlineOpacity = FiniteClamp(Configuration.OutlineOpacity, 0.9f, 0, 1);
        Configuration.LineThickness = FiniteClamp(Configuration.LineThickness, 2, 0.5f, 8);
        Configuration.CurveSegments = Math.Clamp(Configuration.CurveSegments, 12, 128);
        Configuration.TelegraphLeadTime = FiniteClamp(
            Configuration.TelegraphLeadTime,
            4.7f,
            0,
            10);
        Configuration.GroundHeightOffset = FiniteClamp(
            Configuration.GroundHeightOffset,
            0.05f,
            -2,
            2);
        Configuration.GhostOpacity = FiniteClamp(Configuration.GhostOpacity, 0.9f, 0.1f, 1);
        Configuration.GhostSizeMultiplier = FiniteClamp(
            Configuration.GhostSizeMultiplier,
            1,
            0.25f,
            4);
        Configuration.GhostFadeDuration = FiniteClamp(
            Configuration.GhostFadeDuration,
            0.5f,
            0,
            3);
        Configuration.StatusHudScale = FiniteClamp(
            Configuration.StatusHudScale,
            1,
            0.5f,
            2.5f);
        Configuration.StatusIconSpacing = FiniteClamp(
            Configuration.StatusIconSpacing,
            4,
            0,
            24);
        if (!float.IsFinite(Configuration.StatusHudX))
        {
            Configuration.StatusHudX = 560;
        }

        if (!float.IsFinite(Configuration.StatusHudY))
        {
            Configuration.StatusHudY = 240;
        }

        if (!float.IsFinite(Configuration.ManualCenterX))
        {
            Configuration.ManualCenterX = 100;
        }

        if (!float.IsFinite(Configuration.ManualCenterY))
        {
            Configuration.ManualCenterY = 0;
        }

        if (!float.IsFinite(Configuration.ManualCenterZ))
        {
            Configuration.ManualCenterZ = 100;
        }

        if (!float.IsFinite(Configuration.ManualRotationDegrees))
        {
            Configuration.ManualRotationDegrees = 0;
        }
        else
        {
            Configuration.ManualRotationDegrees =
                MathF.IEEERemainder(Configuration.ManualRotationDegrees, 360);
        }

        if (!Enum.IsDefined(Configuration.PlayerRole))
        {
            Configuration.PlayerRole = PartyRole.M1;
        }

        if (!Enum.IsDefined(Configuration.FailureBehavior))
        {
            Configuration.FailureBehavior = FailureBehavior.Continue;
        }

        if (!Enum.IsDefined(Configuration.AnchorMode))
        {
            Configuration.AnchorMode = ArenaAnchorMode.Target;
        }

        if (!Enum.IsDefined(Configuration.NorthMode))
        {
            Configuration.NorthMode = ArenaNorthMode.WorldNorth;
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static float FiniteClamp(float value, float fallback, float minimum, float maximum) =>
        Math.Clamp(float.IsFinite(value) ? value : fallback, minimum, maximum);
}
