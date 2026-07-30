namespace KefkaP4Trainer.Core;

public sealed class SimulationClock
{
    public SimulationState State { get; private set; } = SimulationState.Stopped;

    public double Time { get; private set; }

    public double PlaybackSpeed { get; set; } = 1;

    public void Start(double countdownSeconds)
    {
        Time = -Math.Max(0, countdownSeconds);
        State = Time < 0 ? SimulationState.Countdown : SimulationState.Running;
    }

    public void Pause()
    {
        if (State is SimulationState.Running or SimulationState.Countdown)
        {
            State = SimulationState.Paused;
        }
    }

    public void Resume()
    {
        if (State == SimulationState.Paused)
        {
            State = Time < 0 ? SimulationState.Countdown : SimulationState.Running;
        }
    }

    public void Stop()
    {
        Time = 0;
        State = SimulationState.Stopped;
    }

    public void Complete()
    {
        State = SimulationState.Completed;
    }

    public void SetPausedTime(double time, double encounterLength)
    {
        Time = Math.Clamp(time, 0, encounterLength);
        State = SimulationState.Paused;
    }

    public double Advance(double realDeltaSeconds)
    {
        if (State is not (SimulationState.Running or SimulationState.Countdown))
        {
            return Time;
        }

        if (!double.IsFinite(realDeltaSeconds) || realDeltaSeconds <= 0)
        {
            return Time;
        }

        PlaybackSpeed = Math.Clamp(PlaybackSpeed, 0.1, 4);
        // The engine drains every event crossed by this advance. Silently
        // truncating a large delta would desynchronize real and simulation time.
        Time += realDeltaSeconds * PlaybackSpeed;
        if (State == SimulationState.Countdown && Time >= 0)
        {
            State = SimulationState.Running;
        }

        return Time;
    }
}
