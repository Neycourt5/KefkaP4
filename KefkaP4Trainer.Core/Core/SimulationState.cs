namespace KefkaP4Trainer.Core;

public enum SimulationState
{
    Stopped,
    Countdown,
    Running,
    Paused,
    Completed,
}

public enum FailureBehavior
{
    Continue,
    Pause,
    Restart,
}

