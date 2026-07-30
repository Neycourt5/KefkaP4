namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

/// <summary>A single cast in flight, from its cast event to the cast completing.</summary>
public readonly record struct CastBar(string Name, double StartsAt, double EndsAt)
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

    public static IReadOnlyList<CastBar> Build(bool infernoFirst)
    {
        var bars = new List<CastBar>();
        foreach (var timelineEvent in KefkaP4Timeline.Events)
        {
            if (TryDescribe(timelineEvent, infernoFirst, out var name, out var duration))
            {
                bars.Add(new CastBar(name, timelineEvent.Time, timelineEvent.Time + duration));
            }
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
        bool infernoFirst,
        out string name,
        out double duration)
    {
        switch (timelineEvent.Kind)
        {
            case TimelineEventKind.CastMysteriousMagic:
                name = "Mysterious Magic";
                duration = MysteriousMagicCast;
                return true;
            case TimelineEventKind.CastGrandCross:
                name = "Grand Cross";
                duration = GrandCrossCast;
                return true;
            case TimelineEventKind.CastChaos:
                // Chaos casts whichever of the pair is scheduled first, so the
                // second cast is always the other one.
                name = (timelineEvent.Argument == 1) == infernoFirst ? "Inferno" : "Tsunami";
                duration = ChaosCast;
                return true;
            case TimelineEventKind.CastFlood:
                name = "Flood of Naughts";
                duration = FloodCast;
                return true;
            case TimelineEventKind.CastThrummingThunder:
                name = "Thrumming Thunder III";
                duration = ThrummingThunderCast;
                return true;
            case TimelineEventKind.CastUltima:
                name = "Ultima Upsurge";
                duration = UltimaCast;
                return true;
            case TimelineEventKind.CastBlizzardBlowout:
                name = "Blizzard Blowout III";
                duration = BlizzardBlowoutCast;
                return true;
            case TimelineEventKind.CastManaRelease:
                name = "Mana Release";
                duration = ManaReleaseCast;
                return true;
            default:
                name = string.Empty;
                duration = 0;
                return false;
        }
    }
}
