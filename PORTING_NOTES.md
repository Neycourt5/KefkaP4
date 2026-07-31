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

## Parity matrix

Audited 2026-07-31 against the local `Waju-Sims/` reference clone (upstream
`WCGH/Waju-Sims`, at `c1145bc`). Status values:

- **Verified** — traced to the Waju source and pinned by a fixture test.
- **Ported** — traced and implemented, no dedicated fixture yet.
- **Presentation only** — Waju has no grading for it, so neither do we.

| Mechanic | Waju source | C# destination | Time | Status | Fixture |
| --- | --- | --- | --- | --- | --- |
| Whole method timeline | `p4_main.tscn` `p4_anim` tracks | `KefkaP4Timeline.Events` | 3.3–120.3 | Verified | `WajuTimelineParityTests` |
| Mysterious Magic 1/2/3 | `cast_mm` / `move_mm_dodge` / `mm_hit` | `KefkaP4Encounter.CastMysteriousMagic` etc. | 8.0 / 22.9 / 38.1 | Ported | `TimelineTests`, `GeometryTests` |
| Grand Cross 1/2 debuffs | `cast_gc`, `neo_debuffs` | `AssignGrandCross` | 12.6 / 27.6 | Ported | `AssignmentTests` |
| Grand Cross 3 wounds | `neo_debuffs_3` | `AssignGrandCrossThree` | 44.0 | Ported | `FloodOfNaughtsTests` |
| Chaos (Entropy/Dynamic Fluid) | `cast_chaos`, `chaos_debuffs` | `AssignChaos` | 18.4 / 34.4 | **Verified** | `RemainingMechanicParityTests` |
| **Flood of Naughts** | `flood_cast`, `move_flood_dodge`, `flood_hit`, GC3 setup, `neo_exdeath.tscn` | `FloodResolution`, `FloodStage`, `AddFloodHalf`, `MoveFlood`, `ResolveFlood` | 55.0 | **Logic verified; icon and colour NOT proven** | `FloodOfNaughtsTests`, `FloodVisualMappingTests` - see the per-layer table below |
| Short/Long GC debuffs | `move_short_debuff`, `short_debuff_hit`, `long_debuff_hit` | `MoveShortDebuffs`, `ResolveDebuffs` | 63.8 / 88.7 | Ported | `MechanicSemanticsTests` |
| Acceleration Bomb | `short_debuff_hit` velocity check | `PlayerState.IsMoving` | 63.8 / 88.7 | Verified | `MechanicSemanticsTests` (strict 0.1 boundary) |
| Thrumming Thunder III | `cast_tt`, `move_tt_dodge`, `tt_hit` | `*ThrummingThunder` | 71.4 | **Verified** | `RemainingMechanicParityTests` |
| Cursed Shriek 1/2 | `shriek_1_hit`, `shriek_2_hit`, `check_if_facing` | `KefkaP4Gaze` | 72.8 / 96.7 | Verified | `GhostAndGazeTests` |
| Entropy twisters | `snapshot_inferno`, `inferno_dodge`, `inferno_hit` | `*Inferno` | 83.5 | **Verified** | `RemainingMechanicParityTests` |
| Blizzard Blowout III | `cast_bb`, `bb_hit` | `*BlizzardBlowout` | 89.4 | **Verified** | `RemainingMechanicParityTests` |
| Mana Release / Dynamic Fluid | `cast_mr`, `show_mr_tele`, `move_mr_dodge`, `mr_hit`, `tsunami_hit` | `*ManaRelease` | 107.5 | **Verified** | `RemainingMechanicParityTests` |
| Ultima Upsurge | `cast_ultima`, `kefka_ultima_finish` | `CastUltima`, `UltimaFinish` | 76.9 / 115.6 | Presentation only | — |
| Allagan Field / Beyond Death | `neo_debuffs_3` | debuff assignment only | 44.0 | Presentation only | — |
| Neo relocate / fade | `neo_fade_out`, `neo_move_fade_in` | `NeoFade`, `NeoRelocate` | 46.0 / 47.5 | Presentation only | — |

### Flood of Naughts, in full

This is the mechanic most easily mis-read, so the derivation is recorded here.

Assignment, `p4_seq.gd` Grand Cross 3 setup:

```gdscript
keys.shuffle()
field_keys = keys.slice(0, 4)   # Allagan Field
death_keys = keys.slice(4, 8)   # Beyond Death
for i in keys.size():
    if randi() % 2 == 0:
        black_wound_keys.append(keys[i])
        if i >= 4 != flood_fake: black_safe_keys.append(keys[i])
        else:                    white_safe_keys.append(keys[i])
    else:
        white_wound_keys.append(keys[i])
        if i >= 4 == flood_fake: black_safe_keys.append(keys[i])
        else:                    white_safe_keys.append(keys[i])
```

Resolution, `flood_hit`:

```gdscript
var pos = v2(party[key].global_position).rotated(deg_to_rad(-neo_rotation_deg))
if black_west == black_safe_keys.has(key):
    if pos.x > 0.0: fail("hit by wrong Antilight")
elif pos.x < 0.0:   fail("hit by wrong Antilight")
```

Two things decide the reading:

1. GDScript comparison operators are left-associative and **do not chain** the
   way Python's do, so `i >= 4 != flood_fake` is `(i >= 4) != flood_fake`. The
   chaining reading would make `flood_fake` inert for white wounds, which
   contradicts the author's own comment that the branch "takes flood fake into
   account so don't check for this later".
2. `i >= 4` is exactly "this slot is in `death_keys`", i.e. carries Beyond
   Death rather than Allagan Field.

The resulting truth table — note that **the safe colour is not simply your own
wound colour**:

| Flood | Wound | Second icon | Stand in | Relative to wound |
| --- | --- | --- | --- | --- |
| Real | Black | Allagan Field | White | opposite |
| Real | Black | Beyond Death | Black | same |
| Real | White | Allagan Field | Black | opposite |
| Real | White | Beyond Death | White | same |
| Fake | — | — | inverts each row above | — |

Restated: **Allagan Field takes the opposite colour, Beyond Death takes the
same colour, and a fake Flood inverts both.**

Membership of `black_safe_keys` is therefore equivalent to "stands in the black
Antilight" regardless of which compass side black is on that pull; the compass
side is only introduced by `black_west`. `FloodBriefing.StandInBlack` asserts
that equivalence and `FloodOfNaughtsTests` pins it.

The grading, side selection and bot movement were all found to already match
Waju exactly. The defect was that the trainer never stated the conclusion: the
old cue reported only "Flood: Real / Black West", leaving the player to combine
three inputs under a 5.3s cast. `FloodBriefing` now resolves it once and drives
the cue, the failure text and the debug panel from the same value.

### Known API traps

- `KefkaP4Encounter.SimulatedPartyPositions` keeps the **pull-start** position
  for the local player's own slot. `SetPositions` routes that slot to
  `RequiredPosition` instead. The renderer skips the player's key, so this is
  harmless on screen, but any consumer iterating the dictionary must exclude
  `PlayerRole` or it will read a stale point.
- `Waju-Sims/DalamudPlugin/` used to hold a stale 2026-07-30 copy of this
  plugin and was a standing trap for anyone following a `DalamudPlugin/...`
  path. It was deleted on 2026-07-31. The authoritative tree is, and always
  was, the repository root; the layout here is flat.

### Flood of Naughts: parity by layer

The earlier note called Flood "Verified". That was overstated: only the control
flow and the assignment had been checked. Parity is now tracked per layer,
because a correct boolean rule can still be presented unreadably.

| Layer | Status | Evidence |
| --- | --- | --- |
| Control flow | **Verified** | `flood_cast` / `move_flood_dodge` / `flood_hit` traced to `KefkaP4Encounter`; all 50 timeline events pinned |
| Assignment | **Verified** | 16-row truth table, `FloodVisualMappingTests.SemanticTruthTable` |
| Geometry (sides) | **Verified** | `DrawnHalvesAndGradedRegionAgreeForEverySeedAndRole`, all 8 rotations |
| Grading | **Verified** | strict `>0` / `<0` boundary, centre-line case pinned |
| Stage placement | **Verified** | `FloodStage` transcribed from `neo_move_fade_in` + `neo_exdeath.tscn`; banner-side test at all rotations |
| **Icon resources** | **Not proven** | ids now resolved from the Status sheet by name; needs one launch to read the atlas |
| **Visual colour** | **Not proven** | Waju's `flood1.png` / `flood2.png` are absent from the clone; blue/purple assignment is inferred |
| In-game | **Not done** | nothing has run in FFXIV |

### Waju files inspected

- `scenes/dmu/p4/p4_seq.gd` — assignment block, `flood_cast`, `flood_hit`, `move_flood_dodge`
- `scenes/dmu/p4/p4_main.tscn` — `p4_anim` method tracks
- `scenes/dmu/p4/enemies/neo_exdeath.gd` — `show_antilight`, `show_orbs`
- `scenes/dmu/p4/enemies/neo_exdeath.tscn` — Antilight nodes, meshes, materials, textures

### The Antilight stage, from source

`neo_move_fade_in` puts Neo at `NEO_EXDEATH_NORTH = Vector2(0, -47)` rotated by
`neo_rotation_deg` — the arena's north edge at the full 47-unit radius — at
height 3.25, facing `-neo_rotation_deg`.

`neo_exdeath.tscn` parents two 10x10 billboard quads to Neo:

| Node | Local transform | Mesh | Material | Texture |
| --- | --- | --- | --- | --- |
| `WhiteAntilight` | X **-17**, Y 6.5, Z 5 | `QuadMesh_kxawg` | `StandardMaterial3D_qlrld` | `flood1.png` |
| `BlackAntilight` | X **+17**, Y 6.5, Z 5 | `QuadMesh_eslhv` | `StandardMaterial3D_tei8n` | `flood2.png` |

`show_antilight(black_west)` only *repositions* when `black_west` is true, so
the scene defaults above are the **black-east** case: White west, Black east.
That is consistent with the grader and with `move_flood_dodge`.

### Colour: what the source does and does not say

Both materials carry the **same** emission, `Color(0.054, 0, 0.069)` — a purple
tint — and near-identical albedo. The only thing distinguishing them is the
texture, and `waju-sim/assets/` **does not exist in the reference clone**.

So the source cannot tell us which Antilight reads blue and which reads purple.
The plugin's mapping is therefore **inferred**:

- Black Antilight → **purple**
- White Antilight → **blue**

on the reading that "black" is the darker of the pair, matching the colours this
plugin already used for the two halves and the user's report that the encounter
shows a blue and a purple indicator on Exdeath. `FloodColors` is the single
place this is decided, and `Configuration.SwapAntilightColors` flips it if a
look in game shows the opposite — no other code changes.

### Was anything reversed?

Checked and **not** reversed: the wound-to-safe-set rule, the safe-set-to-side
selection, east/west orientation, the bot destinations, and the rendered versus
graded rectangles (asserted equal for 120 seeds x 8 roles x 8 rotations).

**Unproven and the likeliest remaining culprit:** the status icon ids. They were
hand-written (`215782` White Wound, `215783` Black Wound, `215780` Beyond Death,
`215590` Allagan Field) with a comment asserting they were unambiguous. That was
an assertion, not a verification, and the reported screenshot — debug naming
"White Wound, Beyond Death" while the player saw two purple icons — is exactly
what a transposed pair looks like. Ids are now resolved from the Status sheet by
**name**, with the old table demoted to a fallback and every row shown in a
debug atlas.

### The reported case

Flood REAL, Black East, White Wound, Beyond Death.

White wound + Beyond Death + real is `(i>=4) == flood_fake` → `true == false` →
false → white-safe. Required: **White Antilight**, drawn **blue**, on the
**WEST** (black being east). Because the wound is also White, this is a
**same-colour** case.

The player reasoned "same colour, take purple" and went to the purple line. The
*reasoning* was right — the wound was misread. Both readings are pinned:

- `ReportedWhiteWoundBeyondDeathRealBlackEastCase`
- `ReportedCaseAsThePlayerReadItWouldHaveBeenPurpleEast`

Root cause is therefore split in two, and both halves are addressed: the icon
layer was untrustworthy (now resolved by name and auditable), and the cue named
an internal Black/White label the player had to translate under a 5.3s cast (now
leads with colour and side).

### Presentation changes

Cue format: `Flood REAL: WEST - BLUE line (White Antilight)`

Failure: `wrong Antilight: needed BLUE WEST (White Antilight), stood in PURPLE EAST`

Overlay: the two halves are drawn in real blue and purple and labelled
`PURPLE Black Antilight (EAST)`. A Neo Exdeath stand-in is drawn at the arena
edge from 47.5s with a REAL/FAKE ring, and from 49.7s the two Antilight banners
sit beside it at local X ±17 in their own colours, the required one ringed in
white and tagged `<- GO HERE`.


### Preserved source quirks

Two behaviours look like bugs and are reproduced deliberately, with tests that
fail if anyone "fixes" them:

- **Twister polarity is inverted between the two Chaos mechanics.**
  `inferno_hit` draws a donut when Entropy is fake and a circle when real;
  `tsunami_hit` draws a *circle* when Dynamic Fluid is fake and a donut when
  real. Waju additionally labels the Dynamic Fluid donut `(Twister, Fake)` even
  though that branch fires when the mechanic is **real**. The label is carried
  over verbatim. See `DynamicFluidTwisterPolarityIsInvertedRelativeToEntropy`.

- **Mana Release flips against the telegraph, not the earlier resolution.**
  `tt_hit` and `bb_hit` write their flipped geometry to *local* variables, with
  a source comment saying this is so the originals survive for Mana Release.
  `mr_hit` therefore re-flips only when its own fake flag differs from the
  Mysterious Magic one. See
  `ManaReleaseFlipsRelativeToTheTelegraphNotTheEarlierResolution`.

### Co-healer calibration note

The scripted phase damage totals roughly 336k per player against a 148k
reference pool, so **no single healer of either profile can complete the phase
alone** — correctly, since a real duo spends far more than one shield or party
heal per raidwide. The co-healer's assistance levels are therefore validated on
time-to-first-death rather than survivor count, and Standard deliberately covers
only alternate raidwides so the ones in between remain the player's
responsibility. Strong covers everything and can carry the phase; that is what
the setting is for.
