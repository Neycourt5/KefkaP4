using FFXIVClientStructs.FFXIV.Component.GUI;
using KefkaP4Trainer.Core;

namespace KefkaP4Trainer;

/// <summary>
/// Plays the game's built-in alert sounds as casts start and finish.
/// </summary>
/// <remarks>
/// Dalamud exposes no sound service, and the real ability audio lives in .scd
/// files whose paths are not enumerable from here, so this uses the same
/// numbered alerts as the &lt;se.N&gt; chat macros. Every failure path is
/// swallowed: a missed cue is worth far less than a fault in the update loop.
/// </remarks>
internal sealed class SoundCueService
{
    private double previousTime = double.NegativeInfinity;
    private int previousPull = -1;
    private bool warned;
    private bool warnedNoHost;

    public void Update(SimulationEngine engine, Configuration configuration)
    {
        try
        {
            if (!configuration.PlaySoundCues)
            {
                return;
            }

            var time = engine.Clock.Time;

            // A new pull or any jump backwards (reset, seek) restarts tracking so
            // rewinding does not replay every cue between the two times.
            if (engine.PullNumber != previousPull || time < previousTime)
            {
                previousPull = engine.PullNumber;
                previousTime = time;
                return;
            }

            if (engine.Clock.State is not (SimulationState.Running or SimulationState.Countdown)
                || time <= previousTime)
            {
                previousTime = time;
                return;
            }

            var bars = engine.Encounter.CastBars;
            for (var index = 0; index < bars.Count; index++)
            {
                var bar = bars[index];
                if (Crossed(bar.StartsAt, time))
                {
                    Play(configuration.CastStartSound);
                }

                if (Crossed(bar.EndsAt, time))
                {
                    Play(configuration.CastFinishSound);
                }
            }

            previousTime = time;
        }
        catch (Exception exception)
        {
            WarnOnce(exception);
        }
    }

    /// <summary>Resets tracking so a fresh pull does not replay past cues.</summary>
    public void Reset()
    {
        previousTime = double.NegativeInfinity;
        previousPull = -1;
    }

    private bool Crossed(double moment, double time) =>
        moment > previousTime && moment <= time;

    /// <summary>
    /// Addons to borrow PlaySoundEffect from, tried in order.
    /// </summary>
    /// <remarks>
    /// The method is an addon instance method with nothing global to call it on,
    /// and the exact name of the chat log has moved between underscore-prefixed
    /// and bare over the years. Rather than bet the whole feature on one string,
    /// walk a list of addons that are loaded whenever the HUD is and take the
    /// first that resolves. The winner is cached so this costs one lookup.
    /// </remarks>
    private static readonly string[] HostAddons =
    [
        "_ChatLog",
        "ChatLog",
        "_ActionBar",
        "_PartyList",
        "_ParameterWidget",
        "NamePlate",
    ];

    private string? resolvedHost;

    /// <summary>
    /// Plays a cue, reporting what happened.
    /// </summary>
    /// <returns>
    /// The addon that carried the sound, or null when none resolved. Returned so
    /// the settings test button can say which, rather than leaving a silent
    /// result indistinguishable from a muted game.
    /// </returns>
    public unsafe string? Play(int soundIndex)
    {
        if (soundIndex <= 0)
        {
            return null;
        }

        try
        {
            if (resolvedHost is not null)
            {
                var cached = Services.GameGui.GetAddonByName(resolvedHost, 1);
                if (!cached.IsNull)
                {
                    ((AtkUnitBase*)cached.Address)->PlaySoundEffect(soundIndex);
                    return resolvedHost;
                }

                // The cached addon went away (HUD hidden, zoning); re-resolve.
                resolvedHost = null;
            }

            foreach (var name in HostAddons)
            {
                var wrapper = Services.GameGui.GetAddonByName(name, 1);
                if (wrapper.IsNull)
                {
                    continue;
                }

                ((AtkUnitBase*)wrapper.Address)->PlaySoundEffect(soundIndex);
                resolvedHost = name;
                Services.Log.Information(
                    "KefkaP4Trainer sound cues are using the {Addon} addon.", name);
                return name;
            }

            WarnNoHostOnce();
            return null;
        }
        catch (Exception exception)
        {
            WarnOnce(exception);
            return null;
        }
    }

    private void WarnNoHostOnce()
    {
        if (warnedNoHost)
        {
            return;
        }

        warnedNoHost = true;
        Services.Log.Warning(
            "KefkaP4Trainer found none of these addons to play sound through: {Addons}.",
            string.Join(", ", HostAddons));
    }

    private void WarnOnce(Exception exception)
    {
        if (warned)
        {
            return;
        }

        warned = true;
        Services.Log.Warning(exception, "KefkaP4Trainer sound cues disabled after a failure.");
    }
}
