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

    private unsafe void Play(int soundIndex)
    {
        if (soundIndex <= 0)
        {
            return;
        }

        try
        {
            // PlaySoundEffect lives on an addon rather than anywhere global, so
            // this borrows the chat log: it is loaded whenever the HUD is.
            var addon = (AtkUnitBase*)Services.GameGui.GetAddonByName("_ChatLog", 1);
            if (addon is not null)
            {
                addon->PlaySoundEffect(soundIndex);
            }
        }
        catch (Exception exception)
        {
            WarnOnce(exception);
        }
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
