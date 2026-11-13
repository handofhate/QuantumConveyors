# Quantum Conveyors

Wireless logistics for Moon Sweepers.

Quantum Conveyors adds two small adapters to the Building Tool's Logistics tab. A **Quantum Input** sits on the end of a
belt and takes its capsules; a **Quantum Output** sits on the start of a belt and puts capsules on it. Put adapters on the
same channel and capsules travel between them with no belt in between. A channel can have any number of inputs and
outputs; the outputs take turns.

## Install

Needs BepInEx 6 (IL2CPP) with the Moon Sweepers patcher, and MoonFramework.

1. Close the game.
2. Put `QuantumConveyors.dll` in `BepInEx/plugins/QuantumConveyors/`. That one file is the whole mod: the model,
   textures and pictures are inside it. Updating from 1.x: delete the folder's other files (models, pictures).

Saves from 1.x load unchanged: same building ids, saved data, channel names and crew messages.

## Use

Walk up to an adapter and press **E**. The menu shows channel groups (folders) and channels:

- **New Channel** creates a channel and immediately opens its rename field. **New Group** creates a root-level folder.
- Click anywhere on a folder row to expand or collapse it. Folders can be nested to any depth.
- Drag channel and folder rows to move them. The menu provides a drag preview, drop feedback, edge auto-scroll, and animated expansion/collapse.
- On a channel: **Assign** (or **Unassign**) this adapter, **Rename**, and **Delete**.
- On a group: **Rename** and **Delete**. Deleting a group moves its contents up a level; it does not delete channels.
- **Auto Group** files only channels created while it is enabled. Matching is case-insensitive: `Iron-Line-2` can become
  `Iron > Line` as `2`, and an existing matching folder is reused.
- **Unassigned** lists channels with no adapters; **Not grouped** lists channels outside any group.
- **Unassigned** and **Not grouped** are virtual sections for filtering, not movable folders.

E, Escape, or right-click closes the menu. The adapter's light: green ready or working, yellow blinking when the channel has
no adapter on the other side or the receiving belt is full, red when it's waiting for a channel or its belt is missing.

Settings (`BepInEx/config/tylerun.moonsweepers.quantumconveyors.cfg`): Adapter Price, Diagnostics, Brain Fart Naming
(This swaps the names of the adapters. Proper semantics is now optional.).

In a crew the host moves the capsules; channel changes made by the host reach everyone.

## How it works

2.0 is a rewrite of 1.x's internals for big bases. 1.x got slower with every adapter: each adapter did work every frame,
and every receiving adapter's frame searched every channel and every adapter. 2.0 does nothing per frame per adapter:

| | 1.x | 2.0 |
| --- | --- | --- |
| Moving capsules | every receiver's Tick searched all channels and adapters | done when MoonFramework offers a capsule to a sender; each channel keeps its own adapter lists |
| Full channels | every waiting capsule asked every receiver, every frame | capsules at a Quantum Input wait in their channel's own list; each hand-off pass a channel is offered capsules only until it's full, so a full base costs per channel, not per waiting capsule |
| MoonFramework's input table | rebuilt twice a second by asking the game for every adapter's ports | a Quantum Input's place in it is remembered until the adapter is built or moved; receivers are left out |
| Placing a capsule | port looked up by name and every port point asked for, per capsule | each receiver remembers its port point; the belt is looked up fresh |
| Indicators | own material, a shadow-casting spot light and a halo per adapter, set every frame | a few shared materials; an adapter is redrawn only when its state changes, timed states run out from a queue, belts re-checked only when belts change |
| MoonFramework's per-frame loops | adapters in Tick, Animate and the power step | left out (adapters need none of them) |
| E key | every adapter's bounds and every robot searched every frame | looked up only when E is pressed |
| Belt ends | MoonFramework matches a belt end to an input rounded to the centimetre; a few millimetres off could miss for good | a Quantum Input also takes belts ending in the neighbouring centimetre |

### Measured

Measured with 1,480 adapters, 1.1.2 against 2.0:

| | change from 1.1.2 |
| --- | --- |
| fps | **+43%** |
| whole frame | -30% |
| game and mod scripts | -35% |
| MoonFramework driver (all framework machines) | -54% |
| capsule hand-offs (Intake.Process) | -41% |
| power step | -35% |
| adapters' per-frame Tick | -100% |

The changes to MoonFramework's loops are Harmony patches (`FrameworkPatches.cs`). Each one fails closed: if
MoonFramework changes, that patch is skipped, the log says so, and the adapters still work the slow way.

Source: `src/QuantumConveyors.Plugin/`

| File | What it does |
| --- | --- |
| `Plugin.cs` | settings, the two buildings, the per-frame driver |
| `Assets.cs` | unpacks the embedded model and pictures to `BepInEx/cache/QuantumConveyors/<version>/` |
| `Network.cs` | channels, moving capsules, crew messages, the public API |
| `Groups.cs`, `NameSplit.cs` | channel groups, and reading groups out of names |
| `Machines.cs` | the two adapters |
| `Indicators.cs` | indicator lights |
| `Menu.cs` | the E menu |
| `FrameworkPatches.cs` | MoonFramework patches |

## Reloading in place

A mod loader that reloads plugins while the game runs can load Quantum Conveyors itself: `Conveyors.Boot()` once at game
start (it registers the two adapters, which MoonFramework only accepts then), then `Start(state)`, `Update()`,
`LateUpdate()` and, before a copy is dropped, `Stop()`. A reload swaps every adapter's machine for the new copy's, from the
same saved data. Changing the adapters themselves (model, ports, price, names) still needs a game restart. Installed the
normal way, as a BepInEx plugin, it works as before.

## For tools: HTTP requests

Quantum Conveyors has a public `HandleHttp(method, path, query, body)` on its plugin class, for mods that serve HTTP and
pass requests on to plugins. Nothing calls it on its own. Reads are GET; changes are POST and work on the host only.

| Request | What it does |
|---|---|
| `GET /quantum/channels` | Every channel (id, name, group, outputs, inputs) and every adapter (id, kind, role, position, channel, attached) |
| `POST /quantum/channels/create?name=` | A new channel; a build name such as `Iron-Line-2` is filed into its group |
| `POST /quantum/channels/modify` | `adapter=&channel=` puts an adapter on a channel; `adapter=` alone takes it off; `channel=&name=` renames |
| `POST /quantum/channels/remove?channel=` | Delete a channel |
| `POST /quantum/convert` | File every ungrouped build-named channel into groups |
| `GET /quantum/flow?minutes=5` | Per channel: capsules moved in the last minutes (up to 10), and the receivers that placed none |

## Build

Needs the .NET 6+ SDK and the game with BepInEx (run once, so `BepInEx/interop` exists) and MoonFramework installed;
the project compiles against them.

- Windows: `./build.ps1` (`-Install` copies the DLL into the game; `-MoonSweepersGameRoot` if the game isn't in
  Steam's default folder)
- Linux: `./build.sh` (`--install`; `MOONSWEEPERS_GAME_ROOT` for another folder)

Output: `src/QuantumConveyors.Plugin/bin/Release/net6.0/QuantumConveyors.dll`.
`tools/Package-QuantumConveyors.ps1` makes the release zip; see `RELEASE_AUTOMATION.md`.

## Boundaries

This is a runtime mod, not an official game extension; a game update can change what it relies on. Don't distribute
the game's files or the generated interop assemblies, only the mod.
