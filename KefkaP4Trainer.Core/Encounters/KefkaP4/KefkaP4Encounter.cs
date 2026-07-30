using System.Numerics;

namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

public sealed class KefkaP4Encounter
{
    private readonly Dictionary<PartyRole, Vector2> partyPositions = [];
    private readonly List<ArenaShape> shapes = [];
    private readonly List<SimulatedDebuff> debuffs = [];
    private readonly List<SimulatedGhost> ghosts = [];
    private GazeDiagnostic[] lastGazeDiagnostics = [];
    private Vector2[] infernoSnapshots = [];
    private Vector2[] tsunamiSnapshots = [];
    private int nextMagicCast;
    private int nextMagicMove;
    private int nextMagicResolution;
    private double requiredStartsAt;
    private double requiredEndsAt;

    public KefkaP4Encounter(long seed, PartyRole playerRole)
    {
        Reset(seed, playerRole);
    }

    public KefkaP4Assignments Assignments { get; private set; } = null!;

    public PartyRole PlayerRole { get; private set; }

    public Vector2? RequiredPosition { get; private set; }

    public string LastMechanicEvaluation { get; private set; } = "No mechanic evaluated.";

    public IReadOnlyList<ArenaShape> AllShapes => shapes;

    public IReadOnlyList<SimulatedDebuff> AllDebuffs => debuffs;

    public IReadOnlyList<SimulatedGhost> AllGhosts => ghosts;

    /// <summary>
    /// Per-source gaze detail from the most recent Cursed Shriek resolution.
    /// </summary>
    public IReadOnlyList<GazeDiagnostic> LastGazeDiagnostics => lastGazeDiagnostics;

    public IReadOnlyDictionary<PartyRole, Vector2> SimulatedPartyPositions => partyPositions;

    public void Reset(long seed, PartyRole playerRole)
    {
        Assignments = KefkaP4Assignments.Generate(seed);
        PlayerRole = playerRole;
        shapes.Clear();
        debuffs.Clear();
        ghosts.Clear();
        lastGazeDiagnostics = [];
        partyPositions.Clear();
        foreach (var role in PartyRoles.All)
        {
            partyPositions[role] = KefkaP4Positions.Initial(role);
        }

        infernoSnapshots = [];
        tsunamiSnapshots = [];
        nextMagicCast = 0;
        nextMagicMove = 0;
        nextMagicResolution = 0;
        RequiredPosition = null;
        requiredStartsAt = 0;
        requiredEndsAt = 0;
        LastMechanicEvaluation = "No mechanic evaluated.";
    }

    public SimulationResult? ProcessEvent(
        TimelineEvent timelineEvent,
        PlayerState player,
        int pullNumber,
        bool evaluate)
    {
        SimulationResult? result = timelineEvent.Kind switch
        {
            TimelineEventKind.CastMysteriousMagic => CastMysteriousMagic(timelineEvent.Time),
            TimelineEventKind.MoveMysteriousMagic => MoveMysteriousMagic(timelineEvent.Time),
            TimelineEventKind.ResolveMysteriousMagic =>
                ResolveMysteriousMagic(timelineEvent.Time, player, pullNumber, evaluate),
            TimelineEventKind.AssignGrandCross => AssignGrandCross(timelineEvent.Argument, timelineEvent.Time),
            TimelineEventKind.AssignChaos => AssignChaos(timelineEvent.Argument, timelineEvent.Time),
            TimelineEventKind.AssignGrandCrossThree => AssignGrandCrossThree(timelineEvent.Time),
            TimelineEventKind.NeoRelocate => NeoRelocate(),
            TimelineEventKind.CastFlood => CastFlood(timelineEvent.Time),
            TimelineEventKind.MoveFlood => MoveFlood(timelineEvent.Time),
            TimelineEventKind.ResolveFlood => ResolveFlood(timelineEvent.Time, player, pullNumber, evaluate),
            TimelineEventKind.MoveShortDebuffs => MoveShortDebuffs(timelineEvent.Time),
            TimelineEventKind.ResolveShortDebuffs =>
                ResolveDebuffs(true, timelineEvent.Time, player, pullNumber, evaluate),
            TimelineEventKind.CastThrummingThunder => CastThrummingThunder(timelineEvent.Time),
            TimelineEventKind.MoveThrummingThunder => MoveThrummingThunder(timelineEvent.Time),
            TimelineEventKind.ResolveThrummingThunder =>
                ResolveThrummingThunder(timelineEvent.Time, player, pullNumber, evaluate),
            TimelineEventKind.ResolveShriekOne =>
                ResolveShriek(true, timelineEvent.Time, player, pullNumber, evaluate),
            TimelineEventKind.MoveCenter => MoveCenter(timelineEvent.Time),
            TimelineEventKind.SnapshotInferno => SnapshotInferno(player),
            TimelineEventKind.MoveInferno => MoveInferno(timelineEvent.Time),
            TimelineEventKind.ResolveInferno =>
                ResolveInferno(timelineEvent.Time, player, pullNumber, evaluate),
            TimelineEventKind.CastBlizzardBlowout => CastBlizzardBlowout(timelineEvent.Time),
            TimelineEventKind.MoveLongDebuffs => MoveLongDebuffs(timelineEvent.Time),
            TimelineEventKind.ResolveLongDebuffs =>
                ResolveDebuffs(false, timelineEvent.Time, player, pullNumber, evaluate),
            TimelineEventKind.ResolveBlizzardBlowout =>
                ResolveBlizzardBlowout(timelineEvent.Time, player, pullNumber, evaluate),
            TimelineEventKind.MoveShriekTwo => MoveShriekTwo(timelineEvent.Time),
            TimelineEventKind.ResolveShriekTwo =>
                ResolveShriek(false, timelineEvent.Time, player, pullNumber, evaluate),
            TimelineEventKind.SnapshotTsunami => SnapshotTsunami(player),
            TimelineEventKind.ShowManaReleaseTelegraph => ShowManaReleaseTelegraph(timelineEvent.Time),
            TimelineEventKind.MoveManaRelease => MoveManaRelease(timelineEvent.Time),
            TimelineEventKind.ResolveManaRelease =>
                ResolveManaRelease(timelineEvent.Time, player, pullNumber, evaluate),
            _ => null,
        };

        if (result is not null)
        {
            LastMechanicEvaluation = result.Passed ? "PASS" : $"FAIL: {result.Reason}";
        }
        else if (IsResolution(timelineEvent.Kind) && evaluate && !player.IsValid)
        {
            LastMechanicEvaluation = "Evaluation paused: local player unavailable.";
        }

        return result;
    }

    public IReadOnlyList<ArenaShape> ActiveShapes(double time, double telegraphLeadTime)
    {
        var active = new List<ArenaShape>();
        foreach (var shape in shapes)
        {
            var startsAt = shape.Phase == ShapePhase.Upcoming
                ? Math.Max(shape.StartsAt, shape.EndsAt - Math.Max(0, telegraphLeadTime))
                : shape.StartsAt;
            if (time >= startsAt && time < shape.EndsAt)
            {
                active.Add(shape);
            }
        }

        return active;
    }

    public ArenaShape? RequiredPositionShape(double time)
    {
        if (RequiredPosition is not { } position
            || time < requiredStartsAt
            || time >= requiredEndsAt)
        {
            return null;
        }

        return new ArenaShape
        {
            Kind = ShapeKind.RequiredPosition,
            Label = $"Suggested {PlayerRole.Key()} position",
            Phase = ShapePhase.Required,
            StartsAt = requiredStartsAt,
            EndsAt = requiredEndsAt,
            Origin = position,
            Radius = 2.5f,
        };
    }

    public IReadOnlyList<SimulatedGhost> ActiveGhosts(double time)
    {
        var active = new List<SimulatedGhost>();
        foreach (var ghost in ghosts)
        {
            if (ghost.IsActiveAt(time))
            {
                active.Add(ghost);
            }
        }

        return active;
    }

    public IReadOnlyList<SimulatedDebuff> ActiveDebuffs(double time, PartyRole? role = null) =>
        debuffs
            .Where(debuff => (role is null || debuff.Role == role) && debuff.IsActiveAt(time))
            .OrderBy(debuff => KefkaP4Constants.DebuffOrder[debuff.Kind])
            .ThenBy(debuff => debuff.ExpiresAt)
            .ToArray();

    public string AssignmentSummary(double time)
    {
        var grandCross =
            $"GC1 {Assignments.GrandCrossOne.Summary(PlayerRole)}; "
            + $"GC2 {Assignments.GrandCrossTwo.Summary(PlayerRole)}";
        if (time < 76.9)
        {
            return grandCross;
        }

        if (time < 84.7)
        {
            var entropy =
                Assignments.InfernoFake ? "Entropy: fake (stay)" : "Entropy: real (move)";
            return $"{entropy}; Shriek 2 {Assignments.GrandCrossTwo.Summary(PlayerRole)}";
        }

        if (time < 96.8)
        {
            return $"Long debuffs / Shriek 2: {grandCross}";
        }

        return Assignments.TsunamiFake
            ? "Dynamic Fluid: fake (move from snapshots)"
            : "Dynamic Fluid: real (stay near snapshots)";
    }

    public string CurrentCue(double time)
    {
        if (time is >= 3.3 and < 8.0)
        {
            return MagicCue(Assignments.MagicPatterns[0], "Mysterious Magic");
        }

        if (time is >= 18.2 and < 22.9)
        {
            return MagicCue(Assignments.MagicPatterns[1], "Mysterious Magic");
        }

        if (time is >= 33.4 and < 38.1)
        {
            return MagicCue(Assignments.MagicPatterns[2], "Mysterious Magic");
        }

        if (time is >= 49.7 and < 55)
        {
            return $"Flood: {(Assignments.FloodFake ? "Fake" : "Real")} / "
                + $"Black {(Assignments.BlackWest ? "West" : "East")}";
        }

        var final = Assignments.MagicPatterns[3];
        if (time is >= 66.7 and < 71.4)
        {
            return $"Thunder: {(final.ThunderFake ? "Fake" : "Real")}";
        }

        if (time is >= 84.7 and < 89.4)
        {
            return $"Ice: {(final.IceFake ? "Fake" : "Real")}";
        }

        if (time is >= 95.9 and < 107.5)
        {
            return $"Mana Release — Thunder {(Assignments.ManaReleaseThunderFake ? "Fake" : "Real")}, "
                + $"Ice {(Assignments.ManaReleaseIceFake ? "Fake" : "Real")}";
        }

        return string.Empty;
    }

    private static bool IsResolution(TimelineEventKind kind) => kind is
        TimelineEventKind.ResolveMysteriousMagic
        or TimelineEventKind.ResolveFlood
        or TimelineEventKind.ResolveShortDebuffs
        or TimelineEventKind.ResolveThrummingThunder
        or TimelineEventKind.ResolveShriekOne
        or TimelineEventKind.ResolveInferno
        or TimelineEventKind.ResolveLongDebuffs
        or TimelineEventKind.ResolveBlizzardBlowout
        or TimelineEventKind.ResolveShriekTwo
        or TimelineEventKind.ResolveManaRelease;

    private SimulationResult? CastMysteriousMagic(double time)
    {
        var pattern = Assignments.MagicPatterns[nextMagicCast++];
        shapes.AddRange(KefkaP4Mechanics.CreateMagicShapes(
            pattern,
            false,
            true,
            true,
            time,
            time + 4.7,
            "Mysterious Magic"));
        return null;
    }

    private SimulationResult? MoveMysteriousMagic(double time)
    {
        var pattern = Assignments.MagicPatterns[nextMagicMove++];
        var resolvedNorthSouth = pattern.NorthSouthIce != pattern.IceFake;
        var westSafe = pattern.WestLines != pattern.ThunderFake;
        SetPositions(
            role => KefkaP4Positions.Magic(
                role,
                resolvedNorthSouth,
                westSafe,
                false,
                pattern.RotationDegrees),
            time,
            time + 1.6);
        return null;
    }

    private SimulationResult? ResolveMysteriousMagic(
        double time,
        PlayerState player,
        int pullNumber,
        bool evaluate)
    {
        var mechanicNumber = nextMagicResolution + 1;
        var pattern = Assignments.MagicPatterns[nextMagicResolution++];
        var hazards = KefkaP4Mechanics.CreateMagicShapes(
            pattern,
            true,
            true,
            true,
            time,
            time + KefkaP4Constants.MagicHitLifetime,
            "Mysterious Magic");
        shapes.AddRange(hazards);
        return EvaluateHazards(
            $"Mysterious Magic {mechanicNumber}",
            time,
            hazards,
            player,
            pullNumber,
            evaluate);
    }

    private SimulationResult? AssignGrandCross(int number, double time)
    {
        var assignment = number == 1 ? Assignments.GrandCrossOne : Assignments.GrandCrossTwo;
        var shortDuration = number == 1 ? 50d : 35d;
        var longDuration = number == 1 ? 75d : 60d;
        var waterIsShort = number == 1
            ? Assignments.ShortWaterInGrandCrossOne
            : !Assignments.ShortWaterInGrandCrossOne;
        var waterDuration = waterIsShort ? shortDuration : longDuration;
        var shriekDuration = number == 1 ? 60d : 70d;

        AddPair(assignment, AssignmentKind.ShortAcceleration, DebuffKind.AccelerationBomb, "Acceleration Bomb", "AB", time, shortDuration);
        AddPair(assignment, AssignmentKind.LongAcceleration, DebuffKind.AccelerationBomb, "Acceleration Bomb", "AB", time, longDuration);
        AddPair(assignment, AssignmentKind.Water, DebuffKind.CompressedWater, "Compressed Water", "W", time, waterDuration);
        AddPair(assignment, AssignmentKind.Lightning, DebuffKind.ForkedLightning, "Forked Lightning", "L", time, waterDuration);
        AddDebuff(assignment.ShriekDps, DebuffKind.CursedShriek, "Cursed Shriek", "G", time, shriekDuration);
        AddDebuff(assignment.ShriekSupport, DebuffKind.CursedShriek, "Cursed Shriek", "G", time, shriekDuration);
        return null;
    }

    private SimulationResult? AssignChaos(int number, double time)
    {
        var isInferno = (Assignments.InfernoFirst && number == 1)
            || (!Assignments.InfernoFirst && number == 2);
        var duration = isInferno
            ? (number == 1 ? 61d : 45d)
            : (number == 1 ? 84d : 68d);
        foreach (var role in PartyRoles.All)
        {
            AddDebuff(
                role,
                isInferno ? DebuffKind.Entropy : DebuffKind.DynamicFluid,
                isInferno ? "Entropy" : "Dynamic Fluid",
                isInferno ? "E" : "F",
                time,
                duration);
        }

        return null;
    }

    private SimulationResult? AssignGrandCrossThree(double time)
    {
        foreach (var role in PartyRoles.All)
        {
            AddDebuff(
                role,
                Assignments.BlackWoundRoles.Contains(role) ? DebuffKind.BlackWound : DebuffKind.WhiteWound,
                Assignments.BlackWoundRoles.Contains(role) ? "Black Wound" : "White Wound",
                Assignments.BlackWoundRoles.Contains(role) ? "B" : "W",
                time,
                55 - time);
            AddDebuff(
                role,
                Assignments.DeathRoles.Contains(role) ? DebuffKind.BeyondDeath : DebuffKind.AllaganField,
                Assignments.DeathRoles.Contains(role) ? "Beyond Death" : "Allagan Field",
                Assignments.DeathRoles.Contains(role) ? "D" : "A",
                time,
                14);
        }

        return null;
    }

    private static SimulationResult? NeoRelocate() => null;

    private SimulationResult? CastFlood(double time)
    {
        var rotation = Assignments.NeoRotationDegrees;
        AddFloodHalf(true, time, rotation);
        AddFloodHalf(false, time, rotation);
        return null;
    }

    private void AddFloodHalf(bool west, double time, float rotation)
    {
        var isBlack = west == Assignments.BlackWest;
        shapes.Add(new ArenaShape
        {
            Kind = ShapeKind.Rectangle,
            Label = isBlack ? "Black Antilight" : "White Antilight",
            Phase = ShapePhase.Information,
            StartsAt = time,
            EndsAt = 55,
            Origin = Geometry.RotateDegrees(new Vector2(west ? -23.5f : 23.5f, -47), rotation),
            Direction = Geometry.RotateDegrees(new Vector2(0, 1), rotation),
            Width = 47,
            Length = 94,
        });
    }

    private SimulationResult? MoveFlood(double time)
    {
        SetPositions(
            role =>
            {
                var west = Assignments.BlackWest == Assignments.BlackSafeRoles.Contains(role);
                return KefkaP4Positions.Flood(role, west, Assignments.NeoRotationDegrees);
            },
            time,
            55);
        return null;
    }

    private SimulationResult? ResolveFlood(
        double time,
        PlayerState player,
        int pullNumber,
        bool evaluate)
    {
        if (!evaluate || !player.IsValid)
        {
            return null;
        }

        string? reason = BoundaryReason(player);
        var local = Geometry.RotateDegrees(player.ArenaPosition, -Assignments.NeoRotationDegrees);
        var expectedWest = Assignments.BlackWest == Assignments.BlackSafeRoles.Contains(PlayerRole);
        if (reason is null
            && ((expectedWest && local.X > Geometry.Epsilon)
                || (!expectedWest && local.X < -Geometry.Epsilon)))
        {
            reason = "wrong Antilight side";
        }

        return Result("Flood of Naughts", time, reason, player, pullNumber);
    }

    private SimulationResult? MoveShortDebuffs(double time)
    {
        var assignment = Assignments.ShortWaterInGrandCrossOne
            ? Assignments.GrandCrossOne
            : Assignments.GrandCrossTwo;
        SetPositions(
            role =>
            {
                var kind = assignment.Find(role) ?? AssignmentKind.ShortAcceleration;
                return KefkaP4Positions.ShortDebuff(kind, role.IsDps(), assignment.Fake);
            },
            time,
            63.8);
        return null;
    }

    private SimulationResult? ResolveDebuffs(
        bool shortSet,
        double time,
        PlayerState player,
        int pullNumber,
        bool evaluate)
    {
        var assignment = shortSet == Assignments.ShortWaterInGrandCrossOne
            ? Assignments.GrandCrossOne
            : Assignments.GrandCrossTwo;
        var phaseName = shortSet ? "Short Grand Cross Debuffs" : "Long Grand Cross Debuffs";
        var markerData = CreateWaterLightningMarkers(assignment, time, player);
        shapes.AddRange(markerData.Select(data => data.Shape));

        if (!evaluate || !player.IsValid)
        {
            return null;
        }

        string? reason = BoundaryReason(player);
        var positions = PartyRoles.All.ToDictionary(role => role, role => PositionOf(role, player));
        foreach (var marker in markerData)
        {
            var count = positions.Count(pair => marker.Shape.Contains(pair.Value));
            if (count != marker.ExpectedCount && reason is null)
            {
                reason = $"{marker.Shape.Label} resolved with {count} target(s), expected {marker.ExpectedCount}";
            }
        }

        if (reason is null)
        {
            var acceleration = FindAcceleration(shortSet);
            if (acceleration is { } accelerationAssignment)
            {
                var speedSquared = player.Speed * player.Speed;
                if (accelerationAssignment.Fake && speedSquared < 0.1f)
                {
                    reason = "failed to move during Acceleration Bomb (Fake)";
                }
                else if (!accelerationAssignment.Fake && speedSquared > 0.1f)
                {
                    reason = "moved during Acceleration Bomb";
                }
            }
        }

        return Result(phaseName, time, reason, player, pullNumber);
    }

    private IReadOnlyList<(ArenaShape Shape, int ExpectedCount)> CreateWaterLightningMarkers(
        GrandCrossAssignment assignment,
        double time,
        PlayerState player)
    {
        var values = new List<(ArenaShape, int)>(4);
        var waterCount = assignment.Fake ? 1 : 3;
        var lightningCount = assignment.Fake ? 3 : 1;
        foreach (var role in assignment.Pair(AssignmentKind.Water))
        {
            values.Add((Circle(
                PositionOf(role, player),
                $"Compressed Water{(assignment.Fake ? " (Fake)" : string.Empty)}",
                time,
                KefkaP4Constants.WaterLifetime),
                waterCount));
        }

        foreach (var role in assignment.Pair(AssignmentKind.Lightning))
        {
            values.Add((Circle(
                PositionOf(role, player),
                $"Forked Lightning{(assignment.Fake ? " (Fake)" : string.Empty)}",
                time,
                KefkaP4Constants.WaterLifetime),
                lightningCount));
        }

        return values;
    }

    private GrandCrossAssignment? FindAcceleration(bool shortSet)
    {
        var kind = shortSet ? AssignmentKind.ShortAcceleration : AssignmentKind.LongAcceleration;
        if (Assignments.GrandCrossOne.Find(PlayerRole) == kind)
        {
            return Assignments.GrandCrossOne;
        }

        return Assignments.GrandCrossTwo.Find(PlayerRole) == kind
            ? Assignments.GrandCrossTwo
            : null;
    }

    private SimulationResult? CastThrummingThunder(double time)
    {
        var pattern = Assignments.MagicPatterns[3];
        shapes.AddRange(KefkaP4Mechanics.CreateMagicShapes(
            pattern,
            false,
            true,
            false,
            time,
            71.4,
            "Thrumming Thunder"));
        return null;
    }

    private SimulationResult? MoveThrummingThunder(double time)
    {
        var pattern = Assignments.MagicPatterns[3];
        var westSafe = pattern.WestLines != pattern.ThunderFake;
        SetPositions(
            role => KefkaP4Positions.ThrummingThunder(role, westSafe, pattern.RotationDegrees),
            time,
            72.8);

        SetBotPosition(
            Assignments.GrandCrossOne.ShriekSupport,
            KefkaP4Positions.ShriekOne(true, westSafe, pattern.RotationDegrees));
        SetBotPosition(
            Assignments.GrandCrossOne.ShriekDps,
            KefkaP4Positions.ShriekOne(false, westSafe, pattern.RotationDegrees));
        AddShriekMarkers(Assignments.GrandCrossOne, time, 72.8);
        return null;
    }

    private SimulationResult? ResolveThrummingThunder(
        double time,
        PlayerState player,
        int pullNumber,
        bool evaluate)
    {
        var hazards = KefkaP4Mechanics.CreateMagicShapes(
            Assignments.MagicPatterns[3],
            true,
            true,
            false,
            time,
            time + KefkaP4Constants.MagicHitLifetime,
            "Thrumming Thunder");
        shapes.AddRange(hazards);
        return EvaluateHazards(
            "Thrumming Thunder III",
            time,
            hazards,
            player,
            pullNumber,
            evaluate);
    }

    private SimulationResult? ResolveShriek(
        bool first,
        double time,
        PlayerState player,
        int pullNumber,
        bool evaluate)
    {
        if (!evaluate || !player.IsValid)
        {
            return null;
        }

        var assignment = first ? Assignments.GrandCrossOne : Assignments.GrandCrossTwo;

        // Waju checks both carriers unconditionally, but a carrier standing on
        // itself yields a degenerate zero-length bearing in the source. The
        // player's own marker is therefore excluded rather than graded against
        // a meaningless angle.
        lastGazeDiagnostics = new[] { assignment.ShriekDps, assignment.ShriekSupport }
            .Where(role => role != PlayerRole)
            .Select(role => KefkaP4Gaze.Evaluate(
                role.Key(),
                player.ArenaPosition,
                player.FacingDirection,
                partyPositions[role],
                assignment.Fake))
            .ToArray();

        var reason = BoundaryReason(player)
            ?? KefkaP4Gaze.Combine(lastGazeDiagnostics, assignment.Fake);

        return Result(
            first ? "Cursed Shriek 1" : "Cursed Shriek 2",
            time,
            reason,
            player,
            pullNumber,
            lastGazeDiagnostics);
    }

    private SimulationResult? MoveCenter(double time)
    {
        var until = time < 90 ? 83.5 : 107.5;
        SetPositions(KefkaP4Positions.Center, time, until);
        return null;
    }

    private SimulationResult? SnapshotInferno(PlayerState player)
    {
        infernoSnapshots = SnapshotParty(player);
        return null;
    }

    private SimulationResult? MoveInferno(double time)
    {
        if (!Assignments.InfernoFake)
        {
            SetPositions(KefkaP4Positions.InfernoDodge, time, 83.5);
        }

        return null;
    }

    private SimulationResult? ResolveInferno(
        double time,
        PlayerState player,
        int pullNumber,
        bool evaluate)
    {
        var hazards = new List<ArenaShape>(infernoSnapshots.Length);
        foreach (var snapshot in infernoSnapshots)
        {
            hazards.Add(Assignments.InfernoFake
                ? Donut(snapshot, "Entropy Donut (Twister, Fake)", time)
                : Circle(snapshot, "Entropy AoE (Twister)", time, KefkaP4Constants.TwisterLifetime, KefkaP4Constants.TwisterInnerRadius));
        }

        shapes.AddRange(hazards);
        return EvaluateHazards("Entropy Twisters", time, hazards, player, pullNumber, evaluate);
    }

    private SimulationResult? CastBlizzardBlowout(double time)
    {
        shapes.AddRange(KefkaP4Mechanics.CreateMagicShapes(
            Assignments.MagicPatterns[3],
            false,
            false,
            true,
            time,
            89.4,
            "Blizzard Blowout"));
        return null;
    }

    private SimulationResult? MoveLongDebuffs(double time)
    {
        var assignment = Assignments.ShortWaterInGrandCrossOne
            ? Assignments.GrandCrossTwo
            : Assignments.GrandCrossOne;
        var pattern = Assignments.MagicPatterns[3];
        var resolvedNorthSouth = pattern.NorthSouthIce != pattern.IceFake;
        var northEastSouthWest = resolvedNorthSouth
            ? pattern.RotationDegrees is 135f or 315f
            : pattern.RotationDegrees is 45f or 225f;
        SetPositions(
            role =>
            {
                var kind = assignment.Find(role) ?? AssignmentKind.LongAcceleration;
                return KefkaP4Positions.LongDebuff(
                    kind,
                    role.IsDps(),
                    assignment.Fake,
                    northEastSouthWest);
            },
            time,
            89.4);
        return null;
    }

    private SimulationResult? ResolveBlizzardBlowout(
        double time,
        PlayerState player,
        int pullNumber,
        bool evaluate)
    {
        var hazards = KefkaP4Mechanics.CreateMagicShapes(
            Assignments.MagicPatterns[3],
            true,
            false,
            true,
            time,
            time + KefkaP4Constants.MagicHitLifetime,
            "Blizzard Blowout");
        shapes.AddRange(hazards);
        return EvaluateHazards(
            "Blizzard Blowout III",
            time,
            hazards,
            player,
            pullNumber,
            evaluate);
    }

    private SimulationResult? MoveShriekTwo(double time)
    {
        SetPositions(KefkaP4Positions.ShriekTwo, time, 96.7);
        SetBotPosition(Assignments.GrandCrossTwo.ShriekSupport, new Vector2(0, 3));
        SetBotPosition(Assignments.GrandCrossTwo.ShriekDps, new Vector2(0, -3));
        AddShriekMarkers(Assignments.GrandCrossTwo, time, 96.7);
        return null;
    }

    private SimulationResult? SnapshotTsunami(PlayerState player)
    {
        tsunamiSnapshots = SnapshotParty(player);
        return null;
    }

    private SimulationResult? ShowManaReleaseTelegraph(double time)
    {
        shapes.AddRange(KefkaP4Mechanics.CreateMagicShapes(
            Assignments.MagicPatterns[3],
            false,
            true,
            true,
            time,
            107.5,
            "Mana Release"));
        return null;
    }

    private SimulationResult? MoveManaRelease(double time)
    {
        var pattern = Assignments.MagicPatterns[3];
        var northSouth = pattern.NorthSouthIce
            != (pattern.IceFake != Assignments.ManaReleaseIceFake);
        var west = pattern.WestLines
            != (pattern.ThunderFake != Assignments.ManaReleaseThunderFake);
        SetPositions(
            role => KefkaP4Positions.Magic(
                role,
                northSouth,
                west,
                Assignments.TsunamiFake,
                pattern.RotationDegrees),
            time,
            107.5);
        return null;
    }

    private SimulationResult? ResolveManaRelease(
        double time,
        PlayerState player,
        int pullNumber,
        bool evaluate)
    {
        var hazards = KefkaP4Mechanics.CreateManaReleaseShapes(
            Assignments.MagicPatterns[3],
            Assignments.ManaReleaseThunderFake,
            Assignments.ManaReleaseIceFake,
            time,
            time + KefkaP4Constants.MagicHitLifetime).ToList();

        foreach (var snapshot in tsunamiSnapshots)
        {
            hazards.Add(Assignments.TsunamiFake
                ? Circle(snapshot, "Dynamic Fluid AoE (Twister)", time, KefkaP4Constants.TwisterLifetime, KefkaP4Constants.TwisterInnerRadius)
                : Donut(snapshot, "Dynamic Fluid Donut (Twister, Fake)", time));
        }

        shapes.AddRange(hazards);
        return EvaluateHazards(
            "Mana Release / Dynamic Fluid",
            time,
            hazards,
            player,
            pullNumber,
            evaluate);
    }

    private SimulationResult? EvaluateHazards(
        string mechanic,
        double time,
        IEnumerable<ArenaShape> hazards,
        PlayerState player,
        int pullNumber,
        bool evaluate)
    {
        if (!evaluate || !player.IsValid)
        {
            return null;
        }

        var reason = BoundaryReason(player);
        if (reason is null)
        {
            var hit = hazards.FirstOrDefault(shape => shape.Contains(player.ArenaPosition));
            if (hit is not null)
            {
                reason = $"clipped by {hit.Label}";
            }
        }

        return Result(mechanic, time, reason, player, pullNumber);
    }

    private static string? BoundaryReason(PlayerState player) =>
        KefkaP4Mechanics.IsInsideArena(player.ArenaPosition) ? null : "outside arena boundary";

    private SimulationResult Result(
        string mechanic,
        double time,
        string? reason,
        PlayerState player,
        int pullNumber,
        IReadOnlyList<GazeDiagnostic>? gazeDiagnostics = null) =>
        new(
            pullNumber,
            Assignments.Seed,
            mechanic,
            time,
            reason is null,
            reason ?? "PASS",
            player.ArenaPosition)
        {
            PlayerFacing = player.FacingDirection,
            GazeDiagnostics = gazeDiagnostics ?? [],
        };

    private void SetPositions(Func<PartyRole, Vector2> selector, double startsAt, double endsAt)
    {
        foreach (var role in PartyRoles.All)
        {
            var position = selector(role);
            if (role == PlayerRole)
            {
                RequiredPosition = position;
                requiredStartsAt = startsAt;
                requiredEndsAt = endsAt;
            }
            else
            {
                partyPositions[role] = position;
            }
        }
    }

    private void SetBotPosition(PartyRole role, Vector2 position)
    {
        if (role == PlayerRole)
        {
            RequiredPosition = position;
        }
        else
        {
            partyPositions[role] = position;
        }
    }

    private Vector2 PositionOf(PartyRole role, PlayerState player) =>
        role == PlayerRole && player.IsValid ? player.ArenaPosition : partyPositions[role];

    private Vector2[] SnapshotParty(PlayerState player) =>
        PartyRoles.All.Select(role => PositionOf(role, player)).ToArray();

    private void AddShriekMarkers(GrandCrossAssignment assignment, double startsAt, double endsAt)
    {
        foreach (var role in new[] { assignment.ShriekDps, assignment.ShriekSupport })
        {
            if (role == PlayerRole)
            {
                // The local player is the real character; no stand-in is drawn.
                continue;
            }

            var position = partyPositions[role];
            shapes.Add(new ArenaShape
            {
                Kind = ShapeKind.PlayerMarker,
                Label = $"{role.Key()} Cursed Shriek",
                Phase = ShapePhase.Information,
                StartsAt = startsAt,
                EndsAt = endsAt,
                Origin = position,
                Radius = 1.5f,
            });

            ghosts.Add(new SimulatedGhost(
                role.Key(),
                position,
                // The source never assigns a facing to a Shriek carrier and
                // never reads one, so this is presentation only: the stand-in
                // looks at the arena centre.
                FacingTowardCenter(position),
                startsAt,
                endsAt,
                IsGazeSource: true,
                GhostVisualState.GazeSource));
        }
    }

    /// <summary>
    /// Arena-space facing angle, in radians, pointing from a ghost toward the
    /// arena centre. Cosmetic only.
    /// </summary>
    private static float FacingTowardCenter(Vector2 position) =>
        position.LengthSquared() <= Geometry.Epsilon
            ? 0
            : Geometry.AngleFromDirection(-position);

    private static ArenaShape Circle(
        Vector2 center,
        string label,
        double time,
        double lifetime,
        float radius = KefkaP4Constants.WaterRadius) =>
        new()
        {
            Kind = ShapeKind.Circle,
            Label = label,
            Phase = ShapePhase.Dangerous,
            StartsAt = time,
            EndsAt = time + lifetime,
            Origin = center,
            Radius = radius,
        };

    private static ArenaShape Donut(Vector2 center, string label, double time) =>
        new()
        {
            Kind = ShapeKind.Donut,
            Label = label,
            Phase = ShapePhase.Dangerous,
            StartsAt = time,
            EndsAt = time + KefkaP4Constants.TwisterLifetime,
            Origin = center,
            InnerRadius = KefkaP4Constants.TwisterInnerRadius,
            Radius = KefkaP4Constants.TwisterOuterRadius,
        };

    private void AddPair(
        GrandCrossAssignment assignment,
        AssignmentKind assignmentKind,
        DebuffKind debuffKind,
        string name,
        string shortLabel,
        double time,
        double duration)
    {
        foreach (var role in assignment.Pair(assignmentKind))
        {
            AddDebuff(role, debuffKind, name, shortLabel, time, duration);
        }
    }

    private void AddDebuff(
        PartyRole role,
        DebuffKind kind,
        string name,
        string shortLabel,
        double time,
        double duration) =>
        debuffs.Add(new SimulatedDebuff(
            role,
            kind,
            name,
            shortLabel,
            time,
            duration <= 0 ? double.PositiveInfinity : time + duration));

    private static string MagicCue(MagicPattern pattern, string name) =>
        $"{name} — Thunder {(pattern.ThunderFake ? "Fake" : "Real")}, "
        + $"Ice {(pattern.IceFake ? "Fake" : "Real")}";
}
