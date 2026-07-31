using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Core.Tests;

/// <summary>
/// Locks the ported timeline against the authoritative Waju animation.
///
/// The table below is transcribed from the <c>p4_anim</c> method tracks in
/// <c>waju-sim/scenes/dmu/p4/p4_main.tscn</c> — the times, the call order and
/// the integer arguments Godot passes to <c>p4_seq.gd</c>. Godot fires an
/// AnimationPlayer method track in track order at equal timestamps, which is
/// why the two events at 76.9 are pinned rather than left to sort.
///
/// If this fails, either the port drifted or Waju was updated. Re-derive from
/// the .tscn; do not adjust the expectation to match the code.
/// </summary>
public sealed class WajuTimelineParityTests
{
    private static readonly (double Time, TimelineEventKind Kind, int Argument)[] WajuAnimation =
    [
        (3.3, TimelineEventKind.CastMysteriousMagic, 0),      // cast_mm
        (3.6, TimelineEventKind.CastGrandCross, 1),           // cast_gc(1)
        (6.4, TimelineEventKind.MoveMysteriousMagic, 0),      // move_mm_dodge
        (8.0, TimelineEventKind.ResolveMysteriousMagic, 0),   // mm_hit
        (8.7, TimelineEventKind.CastChaos, 1),                // cast_chaos(1)
        (12.6, TimelineEventKind.AssignGrandCross, 1),        // neo_debuffs(1)
        (18.2, TimelineEventKind.CastMysteriousMagic, 0),     // cast_mm
        (18.4, TimelineEventKind.AssignChaos, 1),             // chaos_debuffs(1)
        (18.5, TimelineEventKind.CastGrandCross, 2),          // cast_gc(2)
        (21.5, TimelineEventKind.MoveMysteriousMagic, 0),     // move_mm_dodge
        (22.9, TimelineEventKind.ResolveMysteriousMagic, 0),  // mm_hit
        (23.7, TimelineEventKind.CastChaos, 2),               // cast_chaos(2)
        (27.6, TimelineEventKind.AssignGrandCross, 2),        // neo_debuffs(2)
        (33.4, TimelineEventKind.CastMysteriousMagic, 0),     // cast_mm
        (33.6, TimelineEventKind.CastGrandCross, 3),          // cast_gc(3)
        (34.4, TimelineEventKind.AssignChaos, 2),             // chaos_debuffs(2)
        (36.3, TimelineEventKind.MoveMysteriousMagic, 0),     // move_mm_dodge
        (38.1, TimelineEventKind.ResolveMysteriousMagic, 0),  // mm_hit
        (44.0, TimelineEventKind.AssignGrandCrossThree, 0),   // neo_debuffs_3
        (46.0, TimelineEventKind.NeoFade, 0),                 // neo_fade_out
        (47.5, TimelineEventKind.NeoRelocate, 0),             // neo_move_fade_in
        (49.7, TimelineEventKind.CastFlood, 0),               // flood_cast
        (52.6, TimelineEventKind.MoveFlood, 0),               // move_flood_dodge
        (55.0, TimelineEventKind.ResolveFlood, 0),            // flood_hit
        (60.1, TimelineEventKind.MoveShortDebuffs, 0),        // move_short_debuff
        (63.8, TimelineEventKind.ResolveShortDebuffs, 0),     // short_debuff_hit
        (66.7, TimelineEventKind.CastThrummingThunder, 0),    // cast_tt
        (68.8, TimelineEventKind.MoveThrummingThunder, 0),    // move_tt_dodge
        (71.4, TimelineEventKind.ResolveThrummingThunder, 0), // tt_hit
        (72.8, TimelineEventKind.ResolveShriekOne, 0),        // shriek_1_hit
        (76.9, TimelineEventKind.CastUltima, 0),              // cast_ultima
        (76.9, TimelineEventKind.MoveCenter, 0),              // move_center
        (79.4, TimelineEventKind.SnapshotInferno, 0),         // snapshot_inferno
        (81.4, TimelineEventKind.MoveInferno, 0),             // inferno_dodge
        (81.6, TimelineEventKind.UltimaFinish, 0),            // kefka_ultima_finish
        (83.5, TimelineEventKind.ResolveInferno, 0),          // inferno_hit
        (84.7, TimelineEventKind.CastBlizzardBlowout, 0),     // cast_bb
        (85.9, TimelineEventKind.MoveLongDebuffs, 0),         // move_long_debuff
        (88.7, TimelineEventKind.ResolveLongDebuffs, 0),      // long_debuff_hit
        (89.4, TimelineEventKind.ResolveBlizzardBlowout, 0),  // bb_hit
        (91.4, TimelineEventKind.MoveShriekTwo, 0),           // move_shriek_2_dodge
        (95.9, TimelineEventKind.CastManaRelease, 0),         // cast_mr
        (96.7, TimelineEventKind.ResolveShriekTwo, 0),        // shriek_2_hit
        (99.0, TimelineEventKind.MoveCenter, 0),              // move_center
        (102.4, TimelineEventKind.SnapshotTsunami, 0),        // snapshot_tsunami
        (102.8, TimelineEventKind.ShowManaReleaseTelegraph, 0), // show_mr_tele
        (104.5, TimelineEventKind.MoveManaRelease, 0),        // move_mr_dodge
        (107.5, TimelineEventKind.ResolveManaRelease, 0),     // mr_hit
        (115.6, TimelineEventKind.CastUltima, 0),             // cast_ultima
        (120.3, TimelineEventKind.UltimaFinish, 0),           // kefka_ultima_finish
    ];

    [Fact]
    public void PortedTimelineMatchesTheWajuAnimationTrack()
    {
        Assert.Equal(WajuAnimation.Length, KefkaP4Timeline.Events.Count);

        for (var index = 0; index < WajuAnimation.Length; index++)
        {
            var expected = WajuAnimation[index];
            var actual = KefkaP4Timeline.Events[index];
            Assert.Equal(expected.Time, actual.Time, 3);
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.Argument, actual.Argument);
        }
    }

    /// <summary>
    /// The engine drains events by list order, so the list has to already be in
    /// non-decreasing time order for a single Update to fire them correctly.
    /// </summary>
    [Fact]
    public void TimelineIsStoredInNonDecreasingTimeOrder()
    {
        for (var index = 1; index < KefkaP4Timeline.Events.Count; index++)
        {
            Assert.True(
                KefkaP4Timeline.Events[index - 1].Time <= KefkaP4Timeline.Events[index].Time,
                $"Event {index} at {KefkaP4Timeline.Events[index].Time} precedes its predecessor.");
        }
    }

    /// <summary>
    /// Every resolution the timeline fires must be reachable within the clock's
    /// encounter length, or a mechanic would silently never grade.
    /// </summary>
    [Fact]
    public void EveryEventFallsInsideTheEncounterLength()
    {
        foreach (var timelineEvent in KefkaP4Timeline.Events)
        {
            Assert.InRange(timelineEvent.Time, 0, KefkaP4Constants.EncounterLength);
        }
    }
}
