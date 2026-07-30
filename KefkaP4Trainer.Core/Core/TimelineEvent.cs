namespace KefkaP4Trainer.Core;

public enum TimelineEventKind
{
    CastMysteriousMagic,
    CastGrandCross,
    MoveMysteriousMagic,
    ResolveMysteriousMagic,
    CastChaos,
    AssignGrandCross,
    AssignChaos,
    AssignGrandCrossThree,
    NeoFade,
    NeoRelocate,
    CastFlood,
    MoveFlood,
    ResolveFlood,
    MoveShortDebuffs,
    ResolveShortDebuffs,
    CastThrummingThunder,
    MoveThrummingThunder,
    ResolveThrummingThunder,
    ResolveShriekOne,
    CastUltima,
    MoveCenter,
    SnapshotInferno,
    MoveInferno,
    UltimaFinish,
    ResolveInferno,
    CastBlizzardBlowout,
    MoveLongDebuffs,
    ResolveLongDebuffs,
    ResolveBlizzardBlowout,
    MoveShriekTwo,
    CastManaRelease,
    ResolveShriekTwo,
    SnapshotTsunami,
    ShowManaReleaseTelegraph,
    MoveManaRelease,
    ResolveManaRelease,
}

public readonly record struct TimelineEvent(
    double Time,
    int Order,
    string Name,
    TimelineEventKind Kind,
    int Argument = 0);

