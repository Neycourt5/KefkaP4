using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Core;

public sealed class SimulationEngine
{
    private readonly List<SimulationResult> history = [];
    private int nextEventIndex;
    private double countdownSeconds = 3;

    public SimulationEngine(long seed = 1, PartyRole playerRole = PartyRole.M1)
    {
        Seed = seed;
        PlayerRole = playerRole;
        Encounter = new KefkaP4Encounter(seed, playerRole);
    }

    public SimulationClock Clock { get; } = new();

    public KefkaP4Encounter Encounter { get; }

    public long Seed { get; private set; }

    public PartyRole PlayerRole { get; private set; }

    public int PullNumber { get; private set; }

    public FailureBehavior FailureBehavior { get; set; } = FailureBehavior.Continue;

    public long? NextAutomaticRestartSeed { get; set; }

    public IReadOnlyList<SimulationResult> History => history;

    public SimulationResult? LastResult => history.Count == 0 ? null : history[^1];

    public TimelineEvent? NextEvent =>
        nextEventIndex < KefkaP4Timeline.Events.Count
            ? KefkaP4Timeline.Events[nextEventIndex]
            : null;

    public void Start(double countdown)
    {
        countdownSeconds = Math.Clamp(countdown, 0, 30);
        PullNumber++;
        NextAutomaticRestartSeed = null;
        Encounter.Reset(Seed, PlayerRole);
        nextEventIndex = 0;
        Clock.Start(countdownSeconds);
    }

    public void Pause() => Clock.Pause();

    public void Resume() => Clock.Resume();

    public void Stop()
    {
        Clock.Stop();
        Encounter.Reset(Seed, PlayerRole);
        nextEventIndex = 0;
        NextAutomaticRestartSeed = null;
    }

    public void Reset()
    {
        Clock.Stop();
        Encounter.Reset(Seed, PlayerRole);
        nextEventIndex = 0;
        NextAutomaticRestartSeed = null;
    }

    public void Restart() => Start(countdownSeconds);

    public void SetSeed(long seed)
    {
        Seed = seed;
        NextAutomaticRestartSeed = null;
        if (Clock.State is SimulationState.Stopped or SimulationState.Completed)
        {
            Encounter.Reset(Seed, PlayerRole);
            nextEventIndex = 0;
        }
    }

    public void SetPlayerRole(PartyRole role)
    {
        PlayerRole = role;
        Encounter.Reset(Seed, role);
        nextEventIndex = 0;
        Clock.Stop();
    }

    public IReadOnlyList<SimulationResult> Update(double elapsedSeconds, PlayerState player)
    {
        var newResults = new List<SimulationResult>();
        Clock.Advance(elapsedSeconds);
        if (Clock.Time < 0 || Clock.State is SimulationState.Stopped or SimulationState.Paused)
        {
            return newResults;
        }

        var restartRequested = false;
        while (nextEventIndex < KefkaP4Timeline.Events.Count
            && KefkaP4Timeline.Events[nextEventIndex].Time <= Clock.Time + 0.000001)
        {
            var timelineEvent = KefkaP4Timeline.Events[nextEventIndex++];
            var result = Encounter.ProcessEvent(
                timelineEvent,
                player,
                PullNumber,
                evaluate: true);
            if (result is null)
            {
                continue;
            }

            history.Add(result);
            newResults.Add(result);
            if (!result.Passed)
            {
                if (FailureBehavior == FailureBehavior.Pause)
                {
                    Clock.Pause();
                    break;
                }

                restartRequested = FailureBehavior == FailureBehavior.Restart;
                if (restartRequested)
                {
                    break;
                }
            }
        }

        if (restartRequested)
        {
            if (NextAutomaticRestartSeed is { } restartSeed)
            {
                Seed = restartSeed;
                NextAutomaticRestartSeed = null;
            }

            Restart();
        }
        else if (Clock.Time >= KefkaP4Constants.EncounterLength)
        {
            Clock.SetPausedTime(KefkaP4Constants.EncounterLength, KefkaP4Constants.EncounterLength);
            Clock.Complete();
        }

        return newResults;
    }

    public bool Seek(double time, PlayerState player)
    {
        if (Clock.State != SimulationState.Paused)
        {
            return false;
        }

        Encounter.Reset(Seed, PlayerRole);
        nextEventIndex = 0;
        var target = Math.Clamp(time, 0, KefkaP4Constants.EncounterLength);
        while (nextEventIndex < KefkaP4Timeline.Events.Count
            && KefkaP4Timeline.Events[nextEventIndex].Time <= target + 0.000001)
        {
            Encounter.ProcessEvent(
                KefkaP4Timeline.Events[nextEventIndex++],
                player,
                PullNumber,
                evaluate: false);
        }

        Clock.SetPausedTime(target, KefkaP4Constants.EncounterLength);
        return true;
    }

    public void ClearResults() => history.Clear();
}
