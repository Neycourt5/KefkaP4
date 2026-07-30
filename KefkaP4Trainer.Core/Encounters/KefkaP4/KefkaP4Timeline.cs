namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

public static class KefkaP4Timeline
{
    public static readonly IReadOnlyList<TimelineEvent> Events =
    [
        E(3.3, 0, "Mysterious Magic cast", TimelineEventKind.CastMysteriousMagic),
        E(3.6, 0, "Grand Cross 1 cast", TimelineEventKind.CastGrandCross, 1),
        E(6.4, 0, "Mysterious Magic dodge", TimelineEventKind.MoveMysteriousMagic),
        E(8.0, 0, "Mysterious Magic 1", TimelineEventKind.ResolveMysteriousMagic),
        E(8.7, 0, "Inferno/Tsunami 1 cast", TimelineEventKind.CastChaos, 1),
        E(12.6, 0, "Grand Cross 1 debuffs", TimelineEventKind.AssignGrandCross, 1),
        E(18.2, 0, "Mysterious Magic cast", TimelineEventKind.CastMysteriousMagic),
        E(18.4, 0, "Chaos debuffs 1", TimelineEventKind.AssignChaos, 1),
        E(18.5, 0, "Grand Cross 2 cast", TimelineEventKind.CastGrandCross, 2),
        E(21.5, 0, "Mysterious Magic dodge", TimelineEventKind.MoveMysteriousMagic),
        E(22.9, 0, "Mysterious Magic 2", TimelineEventKind.ResolveMysteriousMagic),
        E(23.7, 0, "Inferno/Tsunami 2 cast", TimelineEventKind.CastChaos, 2),
        E(27.6, 0, "Grand Cross 2 debuffs", TimelineEventKind.AssignGrandCross, 2),
        E(33.4, 0, "Mysterious Magic cast", TimelineEventKind.CastMysteriousMagic),
        E(33.6, 0, "Grand Cross 3 cast", TimelineEventKind.CastGrandCross, 3),
        E(34.4, 0, "Chaos debuffs 2", TimelineEventKind.AssignChaos, 2),
        E(36.3, 0, "Mysterious Magic dodge", TimelineEventKind.MoveMysteriousMagic),
        E(38.1, 0, "Mysterious Magic 3", TimelineEventKind.ResolveMysteriousMagic),
        E(44.0, 0, "Grand Cross 3 debuffs", TimelineEventKind.AssignGrandCrossThree),
        E(46.0, 0, "Neo Exdeath fade", TimelineEventKind.NeoFade),
        E(47.5, 0, "Neo Exdeath relocate", TimelineEventKind.NeoRelocate),
        E(49.7, 0, "Flood of Naughts cast", TimelineEventKind.CastFlood),
        E(52.6, 0, "Flood positioning", TimelineEventKind.MoveFlood),
        E(55.0, 0, "Flood of Naughts", TimelineEventKind.ResolveFlood),
        E(60.1, 0, "Short debuff positioning", TimelineEventKind.MoveShortDebuffs),
        E(63.8, 0, "Short Grand Cross debuffs", TimelineEventKind.ResolveShortDebuffs),
        E(66.7, 0, "Thrumming Thunder III cast", TimelineEventKind.CastThrummingThunder),
        E(68.8, 0, "Thrumming Thunder positioning", TimelineEventKind.MoveThrummingThunder),
        E(71.4, 0, "Thrumming Thunder III", TimelineEventKind.ResolveThrummingThunder),
        E(72.8, 0, "Cursed Shriek 1", TimelineEventKind.ResolveShriekOne),
        E(76.9, 0, "Ultima Upsurge cast", TimelineEventKind.CastUltima),
        E(76.9, 1, "Center positioning", TimelineEventKind.MoveCenter),
        E(79.4, 0, "Entropy snapshot", TimelineEventKind.SnapshotInferno),
        E(81.4, 0, "Entropy dodge", TimelineEventKind.MoveInferno),
        E(81.6, 0, "Ultima Upsurge finish", TimelineEventKind.UltimaFinish),
        E(83.5, 0, "Entropy twisters", TimelineEventKind.ResolveInferno),
        E(84.7, 0, "Blizzard Blowout III cast", TimelineEventKind.CastBlizzardBlowout),
        E(85.9, 0, "Long debuff positioning", TimelineEventKind.MoveLongDebuffs),
        E(88.7, 0, "Long Grand Cross debuffs", TimelineEventKind.ResolveLongDebuffs),
        E(89.4, 0, "Blizzard Blowout III", TimelineEventKind.ResolveBlizzardBlowout),
        E(91.4, 0, "Cursed Shriek 2 positioning", TimelineEventKind.MoveShriekTwo),
        E(95.9, 0, "Mana Release cast", TimelineEventKind.CastManaRelease),
        E(96.7, 0, "Cursed Shriek 2", TimelineEventKind.ResolveShriekTwo),
        E(99.0, 0, "Center positioning", TimelineEventKind.MoveCenter),
        E(102.4, 0, "Dynamic Fluid snapshot", TimelineEventKind.SnapshotTsunami),
        E(102.8, 0, "Mana Release telegraph", TimelineEventKind.ShowManaReleaseTelegraph),
        E(104.5, 0, "Mana Release positioning", TimelineEventKind.MoveManaRelease),
        E(107.5, 0, "Mana Release / Dynamic Fluid", TimelineEventKind.ResolveManaRelease),
        E(115.6, 0, "Ultima Upsurge cast", TimelineEventKind.CastUltima),
        E(120.3, 0, "Ultima Upsurge finish", TimelineEventKind.UltimaFinish),
    ];

    public static readonly IReadOnlyList<(string Name, double Time)> Checkpoints =
    [
        ("Pull", 0),
        ("Grand Cross debuffs", 12.6),
        ("Flood", 49.7),
        ("Short debuffs", 60.1),
        ("Thunder / Shriek", 66.7),
        ("Entropy", 76.9),
        ("Long debuffs / Blizzard", 84.7),
        ("Mana Release", 95.9),
    ];

    private static TimelineEvent E(
        double time,
        int order,
        string name,
        TimelineEventKind kind,
        int argument = 0) =>
        new(time, order, name, kind, argument);
}

