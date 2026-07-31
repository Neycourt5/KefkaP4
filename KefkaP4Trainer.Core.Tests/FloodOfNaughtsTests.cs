using System.Numerics;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Core.Tests;

/// <summary>
/// Parity fixtures for Flood of Naughts against <c>p4_seq.gd</c>.
///
/// Assignment (Grand Cross 3 setup):
/// <code>
/// if randi() % 2 == 0:
///     black_wound_keys.append(keys[i])
///     if i >= 4 != flood_fake: black_safe_keys.append(keys[i])
///     else:                    white_safe_keys.append(keys[i])
/// else:
///     white_wound_keys.append(keys[i])
///     if i >= 4 == flood_fake: black_safe_keys.append(keys[i])
///     else:                    white_safe_keys.append(keys[i])
/// </code>
///
/// Resolution (<c>flood_hit</c>):
/// <code>
/// var pos = v2(global_position).rotated(deg_to_rad(-neo_rotation_deg))
/// if black_west == black_safe_keys.has(key):
///     if pos.x > 0.0: fail
/// elif pos.x &lt; 0.0: fail
/// </code>
///
/// GDScript comparisons are left-associative and do not chain, so
/// <c>i &gt;= 4 != flood_fake</c> is <c>(i &gt;= 4) != flood_fake</c>. Godot's
/// <c>keys.slice(4, 8)</c> is the Beyond Death half, making <c>i &gt;= 4</c>
/// exactly "this slot has Beyond Death".
/// </summary>
public sealed class FloodOfNaughtsTests
{
    private const float WestLocalX = -20f;
    private const float EastLocalX = 20f;

    /// <summary>
    /// Waju's set is named "black safe", and the grader sends that set to the
    /// side holding the black Antilight. Membership therefore means "stands in
    /// black", independent of which compass side black happens to be on.
    /// </summary>
    [Fact]
    public void BlackSafeMembershipMeansStandingInTheBlackAntilight()
    {
        for (long seed = 0; seed < 400; seed++)
        {
            var encounter = new KefkaP4Encounter(seed, PartyRole.M1);
            foreach (var role in PartyRoles.All)
            {
                var briefing = encounter.FloodBriefingFor(role);
                Assert.Equal(
                    encounter.Assignments.BlackSafeRoles.Contains(role),
                    briefing.StandInBlack);
            }
        }
    }

    /// <summary>Every slot lands in exactly one of the two safe sets.</summary>
    [Fact]
    public void SafeSetsPartitionTheParty()
    {
        for (long seed = 0; seed < 400; seed++)
        {
            var assignments = KefkaP4Assignments.Generate(seed);
            foreach (var role in PartyRoles.All)
            {
                Assert.NotEqual(
                    assignments.BlackSafeRoles.Contains(role),
                    assignments.WhiteSafeRoles.Contains(role));
                Assert.NotEqual(
                    assignments.BlackWoundRoles.Contains(role),
                    assignments.WhiteWoundRoles.Contains(role));
                Assert.NotEqual(
                    assignments.FieldRoles.Contains(role),
                    assignments.DeathRoles.Contains(role));
            }
        }
    }

    /// <summary>
    /// The complete Waju truth table. Real flood plus Allagan Field takes the
    /// opposite colour to the wound; Beyond Death swaps the side; a fake flood
    /// inverts both.
    /// </summary>
    [Theory]
    // floodFake, blackWound, beyondDeath, expected stand-in-black
    [InlineData(false, true, false, false)]  // black wound + field, real -> white
    [InlineData(false, true, true, true)]    // black wound + death, real -> black
    [InlineData(false, false, false, true)]  // white wound + field, real -> black
    [InlineData(false, false, true, false)]  // white wound + death, real -> white
    [InlineData(true, true, false, true)]    // fake inverts each of the above
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, true)]
    public void AssignmentMatchesWajuTruthTable(
        bool floodFake,
        bool blackWound,
        bool beyondDeath,
        bool expectedStandInBlack)
    {
        var (seed, role) = FindCase(floodFake, blackWound, beyondDeath);
        var encounter = new KefkaP4Encounter(seed, role);
        var briefing = encounter.FloodBriefingFor(role);

        Assert.Equal(floodFake, briefing.FloodFake);
        Assert.Equal(blackWound, briefing.HasBlackWound);
        Assert.Equal(beyondDeath, briefing.HasBeyondDeath);
        Assert.Equal(expectedStandInBlack, briefing.StandInBlack);
        Assert.Equal(expectedStandInBlack == blackWound, briefing.MatchesWoundColour);
    }

    /// <summary>
    /// The exact combination reported from a live test: purple (Black) Wound
    /// plus the blue Allagan Field icon on a real Flood. "Take your own colour"
    /// is wrong here; the answer is the White Antilight.
    /// </summary>
    [Fact]
    public void BlackWoundWithAllaganFieldOnRealFloodRequiresWhiteAntilight()
    {
        var (seed, role) = FindCase(floodFake: false, blackWound: true, beyondDeath: false);
        var encounter = new KefkaP4Encounter(seed, role);
        var briefing = encounter.FloodBriefingFor(role);

        Assert.False(briefing.StandInBlack);
        Assert.False(briefing.MatchesWoundColour);
        Assert.Equal("White", briefing.AntilightText);

        // Standing in the black (purple) half is the failure that was observed.
        var blackSide = briefing.BlackWest ? WestLocalX : EastLocalX;
        Assert.False(Resolve(encounter, blackSide).Passed);
        Assert.True(Resolve(encounter, -blackSide).Passed);
    }

    /// <summary>
    /// The briefing is generated from the same sets the grader reads, so acting
    /// on it must always pass and inverting it must always fail. Covers every
    /// slot, both fake states and all eight Neo rotations.
    /// </summary>
    [Fact]
    public void FollowingTheBriefingAlwaysPassesAndInvertingItAlwaysFails()
    {
        var rotationsSeen = new HashSet<float>();
        var fakeStatesSeen = new HashSet<bool>();

        for (long seed = 0; seed < 250; seed++)
        {
            foreach (var role in PartyRoles.All)
            {
                var encounter = new KefkaP4Encounter(seed, role);
                var briefing = encounter.FloodBriefingFor(role);
                rotationsSeen.Add(encounter.Assignments.NeoRotationDegrees);
                fakeStatesSeen.Add(briefing.FloodFake);

                var correct = briefing.StandWest ? WestLocalX : EastLocalX;
                Assert.True(Resolve(encounter, correct).Passed, briefing.Explanation);

                var wrong = Resolve(encounter, -correct);
                Assert.False(wrong.Passed, briefing.Explanation);
                Assert.Contains(briefing.AntilightText, wrong.Reason, StringComparison.Ordinal);
            }
        }

        Assert.Equal(8, rotationsSeen.Count);
        Assert.Equal(2, fakeStatesSeen.Count);
    }

    /// <summary>
    /// Waju compares strictly against 0.0, so the dividing line itself is not a
    /// failure on either side.
    /// </summary>
    [Fact]
    public void ArenaCentreIsNotAFailureForEitherSide()
    {
        for (long seed = 0; seed < 120; seed++)
        {
            foreach (var role in PartyRoles.All)
            {
                var encounter = new KefkaP4Encounter(seed, role);
                var result = ResolveAt(encounter, Vector2.Zero);
                Assert.True(result.Passed, result.Reason);
            }
        }
    }

    /// <summary>Leaving the arena outranks the side check, as in every other mechanic.</summary>
    [Fact]
    public void OutsideTheArenaFailsOnTheBoundaryReason()
    {
        var encounter = new KefkaP4Encounter(8675309, PartyRole.M1);
        var briefing = encounter.FloodBriefingFor(PartyRole.M1);
        var correctSign = briefing.StandWest ? -1f : 1f;
        var result = ResolveAt(
            encounter,
            Geometry.RotateDegrees(
                new Vector2(correctSign * (KefkaP4Constants.ArenaRadius + 5f), 0),
                encounter.Assignments.NeoRotationDegrees));

        Assert.False(result.Passed);
        Assert.Equal("outside arena boundary", result.Reason);
    }

    /// <summary>
    /// The bots Waju moves during <c>move_flood_dodge</c> must end up on the
    /// side their own briefing names, or the ghosts would contradict the cue.
    /// </summary>
    [Fact]
    public void SimulatedPartyMovesToTheSideItsBriefingNames()
    {
        for (long seed = 0; seed < 60; seed++)
        {
            var encounter = new KefkaP4Encounter(seed, PartyRole.M1);
            _ = encounter.ProcessEvent(
                Event(52.6, TimelineEventKind.MoveFlood),
                PlayerState.Unavailable,
                1,
                evaluate: false);

            foreach (var pair in encounter.SimulatedPartyPositions)
            {
                // The player's own slot is steered through RequiredPosition, so
                // its SimulatedPartyPositions entry stays at the pull-start spot.
                if (pair.Key == encounter.PlayerRole)
                {
                    continue;
                }

                var briefing = encounter.FloodBriefingFor(pair.Key);
                var local = Geometry.RotateDegrees(
                    pair.Value,
                    -encounter.Assignments.NeoRotationDegrees);
                Assert.Equal(briefing.StandWest, local.X < 0);
            }

            // ...and the player's own destination still has to agree.
            var playerBriefing = encounter.FloodBriefingFor(encounter.PlayerRole);
            var playerLocal = Geometry.RotateDegrees(
                encounter.RequiredPosition!.Value,
                -encounter.Assignments.NeoRotationDegrees);
            Assert.Equal(playerBriefing.StandWest, playerLocal.X < 0);
        }
    }

    private static SimulationResult Resolve(KefkaP4Encounter encounter, float localX) =>
        ResolveAt(
            encounter,
            Geometry.RotateDegrees(
                new Vector2(localX, 0),
                encounter.Assignments.NeoRotationDegrees));

    private static SimulationResult ResolveAt(KefkaP4Encounter encounter, Vector2 arenaPosition) =>
        encounter.ProcessEvent(
            Event(55.0, TimelineEventKind.ResolveFlood),
            new PlayerState(true, arenaPosition, new Vector2(0, -1), 0),
            1,
            evaluate: true)!;

    private static (long Seed, PartyRole Role) FindCase(
        bool floodFake,
        bool blackWound,
        bool beyondDeath)
    {
        for (long seed = 0; seed < 20_000; seed++)
        {
            var assignments = KefkaP4Assignments.Generate(seed);
            if (assignments.FloodFake != floodFake)
            {
                continue;
            }

            foreach (var role in PartyRoles.All)
            {
                if (assignments.BlackWoundRoles.Contains(role) == blackWound
                    && assignments.DeathRoles.Contains(role) == beyondDeath)
                {
                    return (seed, role);
                }
            }
        }

        throw new InvalidOperationException(
            $"No case for fake={floodFake}, blackWound={blackWound}, death={beyondDeath}.");
    }

    private static TimelineEvent Event(double time, TimelineEventKind kind) =>
        new(time, 0, kind.ToString(), kind);
}
