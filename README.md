# Kefka P4 Trainer

`KefkaP4Trainer` is a Dalamud practice plugin that ports the Kefka P4 sequence
from the Waju FFXIV simulator into a deterministic, local-only training
overlay. It reads the local player's position and facing so the simulation can
grade movement. It does not move the character, send actions, select targets,
write game state, inspect packets, install hooks, or automate gameplay.

> **Experimental.** This is a third-party plugin distributed through a custom
> Dalamud repository. It is not reviewed or endorsed by the Dalamud team.

The original simulator is authoritative for encounter timing and assignment
rules. The C# implementation deliberately separates those rules from Dalamud
and ImGui:

- `KefkaP4Trainer.Core` contains the deterministic clock, RNG, assignments,
  timeline, geometry, coordinate conversion, mechanic evaluation, and results.
- `KefkaP4Trainer` contains only Dalamud services, player-state sampling,
  world-to-screen drawing, commands, and UI.
- `KefkaP4Trainer.Core.Tests` exercises determinism, assignments, geometry,
  coordinate transforms, and timeline behavior without launching FFXIV.

See [`PORTING_NOTES.md`](PORTING_NOTES.md) for the source mapping, coordinate
conventions, gaze rules, known source quirks, and design decisions.

## Prerequisites

- Windows with the current [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A current Dalamud development installation
- `DALAMUD_HOME` set to the directory containing `Dalamud.dll`, or the normal
  XIVLauncher development hooks directory at
  `%APPDATA%\XIVLauncher\addon\Hooks\dev`

The plugin project uses `Dalamud.NET.Sdk/15.0.0`, which supplies the current
Windows target framework and x64 platform settings.

## Build and test

From the repository root:

```powershell
dotnet test .\KefkaP4Trainer.Core.Tests\KefkaP4Trainer.Core.Tests.csproj -c Release
dotnet build .\KefkaP4Trainer\KefkaP4Trainer.csproj -c Release -p:Platform=x64
```

On success, the plugin DLL is:

```text
KefkaP4Trainer\bin\x64\Release\KefkaP4Trainer.dll
```

The Dalamud SDK also creates the installable package:

```text
KefkaP4Trainer\bin\x64\Release\KefkaP4Trainer\latest.zip
```

Omitting `-p:Platform=x64` builds to `KefkaP4Trainer\bin\Release\` instead;
both configurations produce the same x64 assembly.

## Install from the custom repository

The plugin is not in the official Dalamud repository. It is distributed as a
third-party repository, which Dalamud treats as experimental.

1. In FFXIV, open **Dalamud Settings** (`/xlsettings`).
2. Go to **Experimental**.
3. Under **Custom Plugin Repositories**, add:

   ```text
   https://raw.githubusercontent.com/Neycourt5/KefkaP4Trainer/main/repo.json
   ```

4. Press the **+** button, then **Save and Close**.
5. Open the plugin installer (`/xlplugins`), search for **Kefka P4 Trainer**,
   and install it.
6. Run `/kefkap4`.

Dalamud will warn that third-party plugins are not vetted by the Dalamud team.
That warning is expected for any custom repository.

## Load as a development plugin

Use this while iterating on the code.

1. Open Dalamud Settings in FFXIV.
2. Enable plugin development/testing support.
3. Add the absolute path to `KefkaP4Trainer.dll` under the development plugin
   locations.
4. Scan for new development plugins and enable **Kefka P4 Trainer**.
5. Run `/kefkap4`.

Rebuild and reload it manually while iterating.

## Releasing

`.github/workflows/release.yml` runs on any `v*` tag. It builds, runs the Core
tests, publishes `latest.zip` as a release asset, and rewrites `repo.json` with
the new version and download URL.

```powershell
git tag v0.1.0.0
git push origin v0.1.0.0
```

## First pull

1. Stand in the intended arena and select an arena-center target, or choose the
   player/manual anchor in Settings.
2. Choose how north should be established:
   world north, the player's current facing, or a manual clockwise rotation.
3. Press **Set arena**. The center and north direction are snapshotted.
4. Choose your explicit party slot (`T1`, `T2`, `H1`, `H2`, `M1`, `M2`,
   `R1`, or `R2`).
5. Choose or randomize a seed, then press **Start**.

The source simulator uses a 47-unit Waju arena radius and a 2.3:1 Waju-to-game
scale. The overlay applies that scale around the snapshotted center, then uses
Dalamud's supported world-to-screen projection. If the arena is misplaced,
stop the pull, correct the anchor/north settings, and press **Set arena** again.

The seven non-player party slots follow the ported Waju solution positions.
Only the selected player slot uses live game position/facing. The role is
chosen explicitly; the plugin does not infer it from the player's current job.

## Commands

```text
/kefkap4
/kefkap4 start
/kefkap4 pause
/kefkap4 resume
/kefkap4 stop
/kefkap4 reset
/kefkap4 restart
/kefkap4 setarena <player|target|manual>
/kefkap4 seed <signed 64-bit integer>
/kefkap4 speed <0.1-4.0>
/kefkap4 config
/kefkap4 debug
/kefkap4 help
```

Running `/kefkap4` with no arguments toggles the main window. Invalid arguments
print concise usage information in chat and do not change the active pull.

## Ghost and gaze practice

Both Cursed Shriek carriers are simulated party members in the source. The
plugin re-creates them as **ghosts**: camera-facing vector mannequins drawn
entirely with ImGui primitives. No actor is spawned, no object-table entry is
created, and no game model is loaded.

Each ghost has a head, shoulders, torso, arms and legs sized from its projected
foot-to-head height, a ground ring marking its exact arena position, and — while
it is an active gaze source — a vector eye above its head. Ghosts fade in when
the carriers move into place and fade out at the resolution timestamp; all
animation is driven by simulation time, so it follows playback speed and
pausing.

Ghosts appear twice per pull:

| Window | Spawns | Resolves | Requirement |
| --- | --- | --- | --- |
| Cursed Shriek 1 | 68.8s | 72.8s | Grand Cross 1 real -> look away; fake -> face both |
| Cursed Shriek 2 | 91.4s | 96.7s | Grand Cross 2 real -> look away; fake -> face both |

The gaze cone is a strict 45-degree half-cone around your character's facing,
taken from the source's `check_if_facing`. Exactly 45 degrees does not count as
facing. Distance is irrelevant. Grading uses character-model rotation, never the
camera.

If a carrier's role is your own configured role, no ghost is drawn for it and it
is excluded from grading — you are standing in that slot yourself.

### Verifying facing and coordinates in game

Ghost visibility is configured under **Settings -> Gaze ghosts**. The explicit
solution aids live in the debug window (`/kefkap4 debug`) and are **off by
default** so ordinary practice reveals no more than the browser simulator:

- player facing arrow
- lines from you to each gaze source
- gaze threshold cone
- numeric per-source angles

The debug window also shows raw rotation, the derived facing vector, arena
position, per-source dot product and angle against the threshold, per-source
pass/fail, the combined result, and the projection failure count. Test ghosts
can be spawned at north/east/south/west or at typed arena coordinates — these
render even while the simulation is stopped, so the coordinate and rotation
conventions can be confirmed against a striking dummy before a pull.

## Determinism and grading

The original Godot scene uses its global RNG and does not expose seeds. This
port therefore defines a stable, plugin-owned PCG32 stream and Fisher-Yates
shuffle. A seed is repeatable within this implementation, but it is not a
Godot/Waju seed format.

Mechanics are evaluated at their scripted resolution timestamps. Player
position is treated as a point on the arena X/Z plane, matching the source's
near-zero player collision radius. Results include pull number, seed, mechanic,
timestamp, pass/fail reason, and arena-space player position. Playback can
continue, pause, or restart after failure.

The sole implemented mode is the complete 125-second P4 sequence. The stale P3
menu data found in the source scene is not presented as a supported P4 mode.

## Implemented encounter behavior

- Three Mysterious Magic line/cone combinations with independent fake/real
  resolution
- Grand Cross 1/2 seeded role assignments, fake statuses, short/long
  Acceleration Bomb checks, Water/Lightning party-count checks, and both
  Cursed Shriek checks
- Grand Cross 3 Field/Death and Black/White Wound assignments followed by
  Flood of Naughts side grading
- Thrumming Thunder III and Blizzard Blowout III using their shared seeded
  line/cone pattern
- Entropy and Dynamic Fluid party-position snapshots with circle/donut
  twister resolution
- Mana Release's source-authoritative fake-component transform combined with
  the Dynamic Fluid resolution
- Vector-mannequin ghosts for both Cursed Shriek carriers, with ground rings,
  gaze eyes, simulation-time fades, and recorded per-source gaze diagnostics
- Ported seven-bot solution positions, arena-boundary checks, result history,
  pause/restart-on-failure behavior, and the full 125-second timeline

## Visuals and known limitations

The cloned source repository does not contain the textures referenced by its
Godot scenes. The fake status HUD consequently uses labeled colored
placeholders rather than copying or redistributing unverified FFXIV assets.

World projection can skip individual vertices that are behind the camera or
outside the viewport, so an AoE may be partially hidden at extreme camera
angles. Ground overlays are visual training aids, not pixel-perfect replicas of
the game renderer. The source also has behaviors intentionally preserved or
documented, including the two-source fake Shriek facing rule, Flood centerline
semantics, and the unused Grand Cross 3 fake flag.

The source gives Ultima presentation callbacks but no damage/mitigation check,
and gives Allagan Field/Beyond Death no independent explosion/death resolution;
the plugin therefore does not invent grading for either. The final 125-second
animation likewise has no source `PASS` callback, so completion is represented
by the clock state while individual resolution results remain in history.

## Attribution and license

This implementation was materially informed by the
[Waju simulator](https://github.com/WCGH/Waju-Sims) source. See
[`NOTICE.md`](NOTICE.md) for attribution and [`LICENSE`](LICENSE) for the GNU
General Public License version 3.

FINAL FANTASY XIV and related names are trademarks of Square Enix Holdings Co.,
Ltd. This project is an unofficial training tool and includes no extracted game
assets.
