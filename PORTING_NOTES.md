# KefkaP4Trainer porting notes

## Source repository

The cloned Waju simulator is a Godot 4.7 desktop application written in
GDScript. It is not a JavaScript/TypeScript web application. The relevant
project version is `1.8.1` in `waju-sim/project.godot`.

The authoritative Kefka P4 implementation is:

- `waju-sim/scenes/dmu/p4/p4_main.tscn` — scene composition and the exact
  125-second `p4_anim` method-call timeline.
- `waju-sim/scenes/dmu/p4/p4_seq.gd` — assignments, random choices, mechanic
  callbacks, fake/real rules, collision requests, and failure text.
- `waju-sim/scenes/dmu/p4/p4_pos.gd` — solved bot positions.
- `waju-sim/scenes/dmu/p4/p4_kefka.gd`,
  `p4_chaos.gd`, and `enemies/neo_exdeath.gd` — visual orb/model state.
- `waju-sim/scenes/common/controllers/GroundAoEController.gd` and
  `waju-sim/scenes/common/ground_markers/{ground_marker,circle_aoe,donut_aoe,line_aoe,cone_aoe}.gd`
  — shape construction and collision/failure conventions.
- `waju-sim/scenes/common/controllers/party_controller.gd` and
  `waju-sim/scripts/autoload/global.gd` — party slots and initial positions.
- `waju-sim/scenes/common/controllers/waymark_controller.gd` — the DMU
  FFXIV-to-simulator scale conversion.
- `waju-sim/scenes/ui/auras/debuff_icons/dmu/p4/*.tscn`,
  `.../dmu/boa/{entropy,dynamic_fluid}.tscn`, and
  `waju-sim/scenes/ui/auras/debuff_icons/debuff.gd` — status names, ordering,
  referenced icon paths, and whole-second timer presentation.
- `LICENSE` — GNU General Public License version 3.

## Original architecture

`Sequence` creates a party of one controlled player and seven bots.
`EncounterController` starts a child sequence. P4 starts a Godot
`AnimationPlayer`; its method tracks call functions on `p4_seq.gd`. Those
functions directly manipulate model animation, UI cast bars, debuff nodes,
bot movement, ground-marker scenes, physics overlap checks, and the fail list.
There is no data-only timeline, result object, replay seed, or reusable
simulation clock in the original.

The only implemented P4 mode is the full phase, labelled `P4 Kefka Says`.
The P4 configuration panel is stale P3 copy/paste content and is not evidence
for individual-mechanic or strategy modes.

## C# mapping

| Waju source | KefkaP4Trainer |
| --- | --- |
| `AnimationPlayer` method tracks in `p4_main.tscn` | `KefkaP4Timeline` immutable, stably ordered events |
| Godot global clock/tree pause | `SimulationClock` and `SimulationEngine` |
| global `randi`, `randi_range`, `Array.shuffle` | stable plugin-owned `DeterministicRandom` (PCG32 + Fisher-Yates) |
| dictionaries in `instantiate_party` | typed `KefkaP4Assignments` / `GrandCrossAssignment` |
| mechanic callbacks in `p4_seq.gd` | `KefkaP4Encounter.ProcessEvent` and `KefkaP4Mechanics` |
| `P4Pos` dictionaries and bot `move_to` | `KefkaP4Positions` and simulated party snapshots |
| `GroundAoeController` marker scenes | pure `ArenaShape` records |
| Godot physics overlaps | point-based `Geometry` collision functions |
| appended strings in `FailList` | typed `SimulationResult` history |
| party-list debuff nodes | timestamped `SimulatedDebuff` values and fake-status HUD |
| Godot camera/rendering | `ArenaTransform`, `IGameGui.WorldToScreen`, and ImGui draw lists |
| controlled Godot player | read-only Dalamud local-player position/facing adapter |
| `check_if_facing` + `shriek_*_hit` | `KefkaP4Gaze` rules and `GazeDiagnostic` records |
| bot party members carrying Cursed Shriek | `SimulatedGhost` records drawn by `GhostRenderer` |

The pure core is a separate project so it has no dependency on Dalamud,
ImGui, or live game objects.

## Exact timeline

The port uses the `p4_anim` times rather than relying on comments:

```text
  3.3 MM cast             3.6 GC1 cast             6.4 MM dodge
  8.0 MM hit              8.7 Chaos 1 cast        12.6 GC1 debuffs
 18.2 MM cast            18.4 Chaos 1 debuffs     18.5 GC2 cast
 21.5 MM dodge           22.9 MM hit               23.7 Chaos 2 cast
 27.6 GC2 debuffs        33.4 MM cast              33.6 GC3 cast
 34.4 Chaos 2 debuffs    36.3 MM dodge             38.1 MM hit
 44.0 GC3 debuffs        46.0 Neo fade             47.5 Neo relocate
 49.7 Flood cast         52.6 Flood dodge          55.0 Flood hit
 60.1 short positioning  63.8 short resolution     66.7 Thunder cast
 68.8 Thunder dodge      71.4 Thunder hit          72.8 Shriek 1
 76.9 Ultima/center      79.4 Inferno snapshot     81.4 Inferno dodge
 81.6 Ultima finish      83.5 Inferno hit           84.7 Blizzard cast
 85.9 long positioning   88.7 long resolution      89.4 Blizzard hit
 91.4 Shriek 2 position  95.9 Mana Release cast    96.7 Shriek 2
 99.0 center            102.4 Tsunami snapshot    102.8 MR telegraph
104.5 MR dodge          107.5 MR/Tsunami hit      115.6 Ultima
120.3 Ultima finish     125.0 animation end
```

## Assignment and randomization behavior

P4 uses Godot's process-global RNG and has no seed or RNG-state API. A scene
reload does not provide a replayable pull. KefkaP4Trainer therefore introduces
a stable PCG32 seed format; identical plugin seeds reproduce plugin pulls, but
they cannot be entered into Waju for cross-program replay.

The port preserves draw ordering and rules:

1. Neo fake flags 1/2/3; which GC1 water duration is short; Inferno/Tsunami
   order and fake flags; Flood fake; Black-west; Mana Release Thunder/Ice fake.
2. Neo rotation in uniform 45-degree steps and the Shriek pair selector.
3. Independent DPS/support shuffles for GC1.
4. The constrained GC2 remap followed by four independent pair swaps.
5. An all-role shuffle into four Field and four Beyond Death assignments, then
   an independent Black/White Wound draw for every role.
6. Five draws for each of three Mysterious Magic patterns and the later shared
   Thunder/Blizzard/Mana Release pattern.

Grand Cross 2 is not an independent shuffle: its acceleration roles come from
GC1 water/lightning and its water/lightning roles come from GC1 acceleration,
with the four pair swaps applied afterward. Shriek 1 is one GC1 acceleration
pair; Shriek 2 is the complementary GC1 pair.

## Coordinate systems

Waju flattens Godot `Vector3(X,Y,Z)` to `Vector2(X,Z)`. Simulator `+X` is east,
simulator `+Y` is south, and `(0,-1)` is north. Positive Godot `Vector2`
rotation therefore appears clockwise on a north-up arena.

DMU waymark import proves that simulator distances are scaled:

```text
simX = (xivX - 100) * 2.3
simY = (xivZ - 100) * 2.3
```

KefkaP4Trainer keeps source constants in simulator units. `ArenaTransform`
divides by 2.3 for simulator-to-FFXIV conversion and multiplies by 2.3 in the
inverse. It then applies one snapshotted clockwise north rotation:

```text
world = center + Rotate(sim, arenaRotation) / 2.3
sim   = Rotate(world - center, -arenaRotation) * 2.3
```

World north uses zero rotation. Facing north snapshots the local player's
FFXIV facing as the simulator north vector. Manual rotation is clockwise
degrees from FFXIV world north. The arena center, elevation, and rotation are
snapshotted; they never follow the player.

The visible P4 floor radius is 47 simulator units (about 20.435 FFXIV units).
The original deathwall trigger is 43 scaled by 1.1, or about 47.3 simulator
units. The plugin draws/grades the visible 47-unit boundary.

## Collision differences

The original uses Godot physics overlaps against character capsules one or two
frames after marker creation. The player capsule radius is only `0.01`, so the
port uses deterministic point-in-shape tests at the scripted resolution time:

- circle: distance squared `<= radius²`;
- donut: distance squared `> inner² && <= outer²` (the source explicitly makes
  the exact inner edge safe);
- line: forward rectangle from its origin, not a centered rectangle;
- cone: the triangular footprint of Godot's generated `PrismMesh`. Waju assigns
  `tan(configuredAngle / 2) * length` as the full base width, so its configured
  `127.5°` produces an effective collision angle of about `90.79°`, not a
  mathematical `127.5°` circular sector.

This removes engine-frame and capsule-radius variance. Seven bot slots are
still simulated from `P4Pos`, so stack counts and twister snapshots can be
evaluated around the real player's slot. Bot travel is treated as arrival at
the source destination rather than reproducing Godot acceleration and
path-time interpolation.

The source's fake Shriek check requires facing both sources, and this is
preserved. When the local player is themselves a Shriek source, the plugin
excludes the zero-length self-facing vector; Godot's result for facing one's
own exact position is incidental and not portable.

## Gaze evaluation and ghost stand-ins

### Gaze rule, verbatim from the source

`check_if_facing(player, pos)` in `p4_seq.gd` converts the player's model
rotation and the bearing to the target into degrees and tests whether the
absolute angular difference is **strictly** below `GAZE_MIN_ANGLE` (45).
`GAZE_MAX_ANGLE` (315) is only the wrap-around companion of the same
45-degree half-cone; it is not a second threshold. Consequences captured in
`KefkaP4Gaze` and covered by tests:

- The cone half-angle is 45 degrees, so the full cone is 90 degrees.
- Exactly 45 degrees is **not** facing (the comparison is strict `<`).
- Distance never affects the result.
- No positional or dead-zone check is combined with either Shriek resolution.
- Character-model rotation is used. The source never reads a camera direction,
  so neither does the plugin.

`shriek_1_hit()` / `shriek_2_hit()` combine the two carriers asymmetrically:

| Grand Cross | Requirement | Fails when |
| --- | --- | --- |
| real | look away | the player faces **either** carrier |
| fake | look toward | the player fails to face **either** carrier |

The fake branch therefore demands that both carriers sit inside one 45-degree
cone at once. That is source-authoritative and is preserved, including the
consequence that diametrically opposed carriers cannot both be satisfied.

Resolution times come from the `p4_main.tscn` method tracks: Shriek 1 at
`72.8`, Shriek 2 at `96.7`.

### Ghosts

Waju's gaze sources are ordinary bot party members, positioned by
`move_tt_dodge()` (Shriek 1) and `move_shriek_2_dodge()` (Shriek 2). The
plugin has no party, so those two carriers are re-created as `SimulatedGhost`
records and drawn as plugin-owned vector mannequins.

| Aspect | Value | Source |
| --- | --- | --- |
| Shriek 1 positions | `TT_GAZE` nw/sw/ne/se, rotated by `mm_rotation_deg` | `p4_pos.gd`, `move_tt_dodge()` |
| Shriek 2 positions | support `(0, 3)`, dps `(0, -3)`, unrotated | `move_shriek_2_dodge()` |
| Spawn time | the movement event (`68.8` / `91.4`) | method track 3 |
| Despawn time | the resolution event (`72.8` / `96.7`) | method track 1 |
| Facing | **not from source** — cosmetic, points at arena centre | n/a |

Ghost facing is the one invented value. The source never assigns a facing to a
Shriek carrier and never reads one, so the facing arrow is decorative and is
disabled by default. Fade in/out is derived from `SpawnTime`/`DespawnTime`
using simulation time, so it tracks playback speed and pausing correctly.

A carrier whose role equals the configured player role gets no ghost, because
the local player is a real character standing in that slot. This is the same
exclusion applied to the gaze grading itself, and it is the documented
divergence from Godot's degenerate zero-length self-bearing.

Ghosts are drawn with ImGui primitives only: no actor is spawned, no object
table entry is created, no game model is loaded, and no renderer is hooked.

## Source limitations preserved or documented

- `neo_3_fake` is displayed/rolled but never changes the GC3/Flood resolution.
- Allagan Field/Beyond Death influence Flood classification but have no
  independent explosion/death check.
- Ultima callbacks are presentation only; no mitigation grading exists.
- The 125-second animation has no explicit success callback.
- Flood's exact centerline passes in the source.
- Fake Shriek requires facing both source characters for a non-source player;
  the documented zero-length self-vector exception applies when the selected
  player slot is one of those sources.
- Mana Release reuses the Thunder/Blizzard base pattern and flips a component
  only when its new fake flag differs from that component's earlier flag.
- Failure text in Waju includes several typos (for example `Anitlight`); the
  plugin uses readable labels while retaining mechanic meaning.

## Assets and licensing

The repository is GPL-3.0. Relevant files carry Copyright 2025/2026 notices
and GPL-3.0 headers. This source-informed port retains attribution and is
licensed GPL-3.0. If it is conveyed, the GPL's corresponding-source and notice
requirements apply.

The checked-out Git tree contains no `waju-sim/assets` directory even though
scenes reference P4 PNGs, fonts, shaders, arena textures, and model assets.
There is no asset provenance manifest. The plugin therefore does not copy
embedded models or absent game-derived artwork. It renders primitive geometry
and clearly labelled colored placeholder status icons.
