namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

/// <summary>A single cast in flight, from its cast event to the cast completing.</summary>
/// <param name="Tell">
/// The real/fake advertisement for this cast, or empty when it makes none.
/// Every boss in the phase shows orbs, not just Kefka, so Grand Cross, Flood and
/// the Inferno/Tsunami pair each carry one.
/// </param>
/// <param name="AnyFake">Whether any element of <paramref name="Tell"/> is inverted.</param>
public readonly record struct CastBar(
    string Name,
    double StartsAt,
    double EndsAt,
    string Tell,
    bool AnyFake)
{
    public double Duration => EndsAt - StartsAt;

    public bool IsActiveAt(double time) => time >= StartsAt && time < EndsAt;

    /// <summary>Fraction elapsed at <paramref name="time"/>, clamped to 0..1.</summary>
    public float ProgressAt(double time)
    {
        var duration = Duration;
        if (!double.IsFinite(duration) || duration <= 0)
        {
            return 1;
        }

        return (float)Math.Clamp((time - StartsAt) / duration, 0, 1);
    }

    public double RemainingAt(double time) => Math.Max(0, EndsAt - time);
}

/// <summary>
/// Cast bars for the encounter, taking cast start times from
/// <see cref="KefkaP4Timeline"/> and durations from the source simulator.
/// </summary>
/// <remarks>
/// The durations are stated rather than derived from the gap to each resolution
/// event. Those two only agree for the casts that resolve the instant they
/// finish: Grand Cross, Inferno/Tsunami and Mana Release all continue past the
/// end of the bar before their effect lands, so deriving the length would have
/// overstated them by up to five seconds.
/// </remarks>
public static class KefkaP4CastBars
{
    private const double MysteriousMagicCast = 4.7;
    private const double GrandCrossCast = 8.8;
    private const double ChaosCast = 8.7;
    private const double FloodCast = 5.3;
    private const double ThrummingThunderCast = 4.7;
    private const double UltimaCast = 4.7;
    private const double BlizzardBlowoutCast = 4.7;
    private const double ManaReleaseCast = 6.7;

    public static IReadOnlyList<CastBar> Build(KefkaP4Assignments assignments)
    {
        var bars = new List<CastBar>();

        // The three Mysterious Magic casts consume the first three patterns in
        // order; everything after them reads the fourth.
        var magicIndex = 0;
        foreach (var timelineEvent in KefkaP4Timeline.Events)
        {
            if (!TryDescribe(
                    timelineEvent,
                    assignments,
                    ref magicIndex,
                    out var name,
                    out var duration,
                    out var tell,
                    out var anyFake))
            {
                continue;
            }

            bars.Add(new CastBar(
                name, timelineEvent.Time, timelineEvent.Time + duration, tell, anyFake));
        }

        return bars;
    }

    public static void CollectActive(
        IReadOnlyList<CastBar> bars,
        double time,
        List<CastBar> destination)
    {
        destination.Clear();
        for (var index = 0; index < bars.Count; index++)
        {
            if (bars[index].IsActiveAt(time))
            {
                destination.Add(bars[index]);
            }
        }
    }

    private static bool TryDescribe(
        TimelineEvent timelineEvent,
        KefkaP4Assignments assignments,
        ref int magicIndex,
        out string name,
        out double duration,
        out string tell,
        out bool anyFake)
    {
        var final = assignments.MagicPatterns[3];
        switch (timelineEvent.Kind)
        {
            case TimelineEventKind.CastMysteriousMagic:
            {
                var pattern = assignments.MagicPatterns[Math.Min(magicIndex++, 2)];
                name = "Mysterious Magic";
                duration = MysteriousMagicCast;
                tell = PairTell(pattern.ThunderFake, pattern.IceFake);
                anyFake = pattern.ThunderFake || pattern.IceFake;
                return true;
            }

            case TimelineEventKind.CastGrandCross:
            {
                // Neo Exdeath carries a separate flag per Grand Cross.
                anyFake = timelineEvent.Argument switch
                {
                    1 => assignments.GrandCrossOne.Fake,
                    2 => assignments.GrandCrossTwo.Fake,
                    _ => assignments.NeoThreeFake,
                };
                name = "Grand Cross";
                duration = GrandCrossCast;
                tell = FakeWord(anyFake);
                return true;
            }

            case TimelineEventKind.CastChaos:
            {
                // Chaos casts whichever of the pair is scheduled first, so the
                // second cast is always the other one.
                var inferno = (timelineEvent.Argument == 1) == assignments.InfernoFirst;
                name = inferno ? "Inferno" : "Tsunami";
                duration = ChaosCast;
                anyFake = inferno ? assignments.InfernoFake : assignments.TsunamiFake;
                tell = FakeWord(anyFake);
                return true;
            }

            case TimelineEventKind.CastFlood:
                name = "Flood of Naughts";
                duration = FloodCast;
                anyFake = assignments.FloodFake;
                tell = FakeWord(anyFake);
                return true;
            case TimelineEventKind.CastThrummingThunder:
                name = "Thrumming Thunder III";
                duration = ThrummingThunderCast;
                anyFake = final.ThunderFake;
                tell = FakeWord(anyFake);
                return true;
            case TimelineEventKind.CastBlizzardBlowout:
                name = "Blizzard Blowout III";
                duration = BlizzardBlowoutCast;
                anyFake = final.IceFake;
                tell = FakeWord(anyFake);
                return true;
            case TimelineEventKind.CastManaRelease:
                name = "Mana Release";
                duration = ManaReleaseCast;
                tell = PairTell(
                    assignments.ManaReleaseThunderFake, assignments.ManaReleaseIceFake);
                anyFake =
                    assignments.ManaReleaseThunderFake || assignments.ManaReleaseIceFake;
                return true;
            case TimelineEventKind.CastUltima:
                // Ultima advertises nothing; it is raidwide either way.
                name = "Ultima Upsurge";
                duration = UltimaCast;
                tell = string.Empty;
                anyFake = false;
                return true;
            default:
                name = string.Empty;
                duration = 0;
                tell = string.Empty;
                anyFake = false;
                return false;
        }
    }

    private static string FakeWord(bool fake) => fake ? "FAKE" : "REAL";

    private static string PairTell(bool thunderFake, bool iceFake) =>
        $"LN {FakeWord(thunderFake)} · ICE {FakeWord(iceFake)}";
}
