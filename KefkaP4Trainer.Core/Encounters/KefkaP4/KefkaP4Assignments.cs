using System.Text;

namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

public sealed record MagicPattern(
    bool IceFake,
    bool ThunderFake,
    bool NorthSouthIce,
    bool WestLines,
    float RotationDegrees);

public sealed class GrandCrossAssignment
{
    private readonly Dictionary<(AssignmentKind Kind, bool IsDps), PartyRole> roles;

    internal GrandCrossAssignment(
        Dictionary<(AssignmentKind Kind, bool IsDps), PartyRole> roles,
        bool fake,
        PartyRole shriekDps,
        PartyRole shriekSupport)
    {
        this.roles = roles;
        Fake = fake;
        ShriekDps = shriekDps;
        ShriekSupport = shriekSupport;
    }

    public bool Fake { get; }

    public PartyRole ShriekDps { get; }

    public PartyRole ShriekSupport { get; }

    public PartyRole Get(AssignmentKind kind, bool isDps) => roles[(kind, isDps)];

    public IEnumerable<PartyRole> Pair(AssignmentKind kind)
    {
        yield return Get(kind, true);
        yield return Get(kind, false);
    }

    public AssignmentKind? Find(PartyRole role)
    {
        foreach (var pair in roles)
        {
            if (pair.Value == role)
            {
                return pair.Key.Kind;
            }
        }

        return null;
    }

    internal Dictionary<(AssignmentKind Kind, bool IsDps), PartyRole> CopyRoles() => new(roles);

    public string Summary(PartyRole role)
    {
        var assignment = Find(role)?.ToString() ?? "None";
        var shriek = role == ShriekDps || role == ShriekSupport ? " + Shriek" : string.Empty;
        return $"{assignment}{shriek} ({(Fake ? "Fake" : "Real")})";
    }
}

public sealed class KefkaP4Assignments
{
    private KefkaP4Assignments()
    {
    }

    public required long Seed { get; init; }

    public required GrandCrossAssignment GrandCrossOne { get; init; }

    public required GrandCrossAssignment GrandCrossTwo { get; init; }

    public required bool NeoThreeFake { get; init; }

    public required bool ShortWaterInGrandCrossOne { get; init; }

    public required bool InfernoFirst { get; init; }

    public required bool InfernoFake { get; init; }

    public required bool TsunamiFake { get; init; }

    public required bool FloodFake { get; init; }

    public required bool BlackWest { get; init; }

    public required bool ManaReleaseThunderFake { get; init; }

    public required bool ManaReleaseIceFake { get; init; }

    public required float NeoRotationDegrees { get; init; }

    public required IReadOnlySet<PartyRole> FieldRoles { get; init; }

    public required IReadOnlySet<PartyRole> DeathRoles { get; init; }

    public required IReadOnlySet<PartyRole> BlackWoundRoles { get; init; }

    public required IReadOnlySet<PartyRole> WhiteWoundRoles { get; init; }

    public required IReadOnlySet<PartyRole> BlackSafeRoles { get; init; }

    public required IReadOnlySet<PartyRole> WhiteSafeRoles { get; init; }

    public required IReadOnlyList<MagicPattern> MagicPatterns { get; init; }

    public string Signature
    {
        get
        {
            var builder = new StringBuilder();
            builder.Append(Seed).Append('|')
                .Append(GrandCrossOne.Fake).Append('|')
                .Append(GrandCrossTwo.Fake).Append('|')
                .Append(NeoThreeFake).Append('|')
                .Append(ShortWaterInGrandCrossOne).Append('|')
                .Append(InfernoFirst).Append('|')
                .Append(InfernoFake).Append('|')
                .Append(TsunamiFake).Append('|')
                .Append(FloodFake).Append('|')
                .Append(BlackWest).Append('|')
                .Append(NeoRotationDegrees);

            foreach (var role in PartyRoles.All)
            {
                builder.Append('|').Append(role).Append(':')
                    .Append(GrandCrossOne.Find(role)).Append(':')
                    .Append(GrandCrossTwo.Find(role)).Append(':')
                    .Append(FieldRoles.Contains(role) ? 'F' : 'D').Append(':')
                    .Append(BlackWoundRoles.Contains(role) ? 'B' : 'W');
            }

            foreach (var pattern in MagicPatterns)
            {
                builder.Append('|').Append(pattern);
            }

            return builder.ToString();
        }
    }

    public static KefkaP4Assignments Generate(long seed)
    {
        var random = new DeterministicRandom(seed);

        var neoOneFake = random.NextBool();
        var neoTwoFake = random.NextBool();
        var neoThreeFake = random.NextBool();
        var shortWaterOne = random.NextBool();
        var infernoFirst = random.NextBool();
        var infernoFake = random.NextBool();
        var tsunamiFake = random.NextBool();
        var floodFake = random.NextBool();
        var blackWest = random.NextBool();
        var manaThunderFake = random.NextBool();
        var manaIceFake = random.NextBool();
        var neoRotation = random.NextInclusive(0, 7) * 45f;
        var shortAccelerationGetsFirstShriek = random.NextBool();

        var dps = PartyRoles.Dps.ToArray();
        var supports = PartyRoles.Supports.ToArray();
        random.Shuffle(dps);
        random.Shuffle(supports);

        var firstRoles = NewRoleMap(dps, supports);
        var secondRoles = new Dictionary<(AssignmentKind, bool), PartyRole>
        {
            [(AssignmentKind.ShortAcceleration, true)] = dps[2],
            [(AssignmentKind.LongAcceleration, true)] = dps[3],
            [(AssignmentKind.Water, true)] = dps[0],
            [(AssignmentKind.Lightning, true)] = dps[1],
            [(AssignmentKind.ShortAcceleration, false)] = supports[2],
            [(AssignmentKind.LongAcceleration, false)] = supports[3],
            [(AssignmentKind.Water, false)] = supports[0],
            [(AssignmentKind.Lightning, false)] = supports[1],
        };

        if (random.NextBool())
        {
            Swap(secondRoles, (AssignmentKind.ShortAcceleration, true), (AssignmentKind.LongAcceleration, true));
        }

        if (random.NextBool())
        {
            Swap(secondRoles, (AssignmentKind.Water, true), (AssignmentKind.Lightning, true));
        }

        if (random.NextBool())
        {
            Swap(secondRoles, (AssignmentKind.ShortAcceleration, false), (AssignmentKind.LongAcceleration, false));
        }

        if (random.NextBool())
        {
            Swap(secondRoles, (AssignmentKind.Water, false), (AssignmentKind.Lightning, false));
        }

        var firstShriekKind = shortAccelerationGetsFirstShriek
            ? AssignmentKind.ShortAcceleration
            : AssignmentKind.LongAcceleration;
        var secondShriekKind = shortAccelerationGetsFirstShriek
            ? AssignmentKind.LongAcceleration
            : AssignmentKind.ShortAcceleration;

        var first = new GrandCrossAssignment(
            firstRoles,
            neoOneFake,
            firstRoles[(firstShriekKind, true)],
            firstRoles[(firstShriekKind, false)]);
        // Waju stores Shriek 2 in the GC2 dictionary, but selects it directly
        // from the complementary GC1 acceleration pair.
        var second = new GrandCrossAssignment(
            secondRoles,
            neoTwoFake,
            firstRoles[(secondShriekKind, true)],
            firstRoles[(secondShriekKind, false)]);

        var shuffledRoles = PartyRoles.All.ToArray();
        random.Shuffle(shuffledRoles);
        var field = shuffledRoles[..4].ToHashSet();
        var death = shuffledRoles[4..].ToHashSet();
        var blackWound = new HashSet<PartyRole>();
        var whiteWound = new HashSet<PartyRole>();
        var blackSafe = new HashSet<PartyRole>();
        var whiteSafe = new HashSet<PartyRole>();

        for (var index = 0; index < shuffledRoles.Length; index++)
        {
            var role = shuffledRoles[index];
            if (random.NextBool())
            {
                blackWound.Add(role);
                if ((index >= 4) != floodFake)
                {
                    blackSafe.Add(role);
                }
                else
                {
                    whiteSafe.Add(role);
                }
            }
            else
            {
                whiteWound.Add(role);
                if ((index >= 4) == floodFake)
                {
                    blackSafe.Add(role);
                }
                else
                {
                    whiteSafe.Add(role);
                }
            }
        }

        var patterns = new MagicPattern[4];
        for (var index = 0; index < patterns.Length; index++)
        {
            patterns[index] = new MagicPattern(
                random.NextBool(),
                random.NextBool(),
                random.NextBool(),
                random.NextBool(),
                (random.NextInclusive(0, 3) * 90f) + 45f);
        }

        return new KefkaP4Assignments
        {
            Seed = seed,
            GrandCrossOne = first,
            GrandCrossTwo = second,
            NeoThreeFake = neoThreeFake,
            ShortWaterInGrandCrossOne = shortWaterOne,
            InfernoFirst = infernoFirst,
            InfernoFake = infernoFake,
            TsunamiFake = tsunamiFake,
            FloodFake = floodFake,
            BlackWest = blackWest,
            ManaReleaseThunderFake = manaThunderFake,
            ManaReleaseIceFake = manaIceFake,
            NeoRotationDegrees = neoRotation,
            FieldRoles = field,
            DeathRoles = death,
            BlackWoundRoles = blackWound,
            WhiteWoundRoles = whiteWound,
            BlackSafeRoles = blackSafe,
            WhiteSafeRoles = whiteSafe,
            MagicPatterns = patterns,
        };
    }

    private static Dictionary<(AssignmentKind, bool), PartyRole> NewRoleMap(
        IReadOnlyList<PartyRole> dps,
        IReadOnlyList<PartyRole> supports) =>
        new()
        {
            [(AssignmentKind.ShortAcceleration, true)] = dps[0],
            [(AssignmentKind.LongAcceleration, true)] = dps[1],
            [(AssignmentKind.Water, true)] = dps[2],
            [(AssignmentKind.Lightning, true)] = dps[3],
            [(AssignmentKind.ShortAcceleration, false)] = supports[0],
            [(AssignmentKind.LongAcceleration, false)] = supports[1],
            [(AssignmentKind.Water, false)] = supports[2],
            [(AssignmentKind.Lightning, false)] = supports[3],
        };

    private static void Swap(
        IDictionary<(AssignmentKind, bool), PartyRole> dictionary,
        (AssignmentKind, bool) first,
        (AssignmentKind, bool) second) =>
        (dictionary[first], dictionary[second]) = (dictionary[second], dictionary[first]);
}

