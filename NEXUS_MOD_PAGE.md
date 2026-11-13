# Quantum Conveyors

Wireless conveyor logistics for Moon Sweepers.

Quantum Conveyors adds small wireless adapters to the normal Building Tool. Place a Quantum Input on a conveyor carrying resources away from a source, place a Quantum Output on the destination conveyor, assign both adapters to the same named channel, and resources can transfer without a physical conveyor connecting the two locations.

## Current release

- Mod: Quantum Conveyors 2.0
- Target game build: Moon Sweepers 1.0.5
- Status: 2.0 is a rewrite of the internals for big bases (see Performance). Single-player behavior is tested on bases with hundreds of adapters; host-authoritative multiplayer support is included.

Please back up your save before using any mods.

## What's new in 2.0

- **Much faster on big bases:** +43% fps on a 1,480-adapter base (see Performance).
- **Channel groups in the E menu:** put channels in folders, as deep as you like. Folders expand inline, and channels or folders can be dragged between groups.
- **Auto Group:** a switch that files only newly created channels by their names. Matching is case-insensitive; `Iron-Line-2` can become `Iron > Line` as `2`, and an existing matching folder is reused.
- **Unassigned** lists channels with no adapters on them, and **Not grouped** lists channels outside any group.
- **A polished configuration menu:** scrolling, edge fades, drag auto-scroll, drop previews, animated expansion/collapse, focused rename fields, creation cancellation, help tooltips, and keyboard input protection.
- **Drag organization:** the old Move buttons are gone; drag a channel or folder row to move it.
- **E picks the adapter you're looking at:** within a few metres, with nothing in front of it, and not through a robot.
- **Native move-tool support:** moved adapters can reconnect and resume their saved channel assignment.
- **Simpler lights:** one steady or flashing light per adapter (see Status indicators). The halo and the spot light are gone; they cost a lot on big bases.
- **One file:** the model, textures and preview images are inside QuantumConveyors.dll.
- **No more dead inputs:** a Quantum Input whose conveyor ends a few millimetres from its port now takes capsules (in 1.x it could stay dead).

Your 1.x saves and channels load unchanged.

## Features

- Quantum Output and Quantum Input building-tool entries under Logistics
- Industrial gateway-style adapter model mounted over the conveyor
- Build-menu preview images
- Red, yellow, and green status indicator with flashing warning states
- Named wireless channels, unlimited channels
- Multiple outputs and multiple inputs per channel
- Channel groups: folders for channels, as deep as you like, and an "Auto-group by name" switch
- No wireless range, power, bandwidth, or operating cost
- Destination-belt backpressure: if the receiving belt cannot accept a resource, the source resource remains on its original belt
- Channel assignments, groups and adapter state persist with the save
- Host-authoritative routing for multiplayer
- In-game configuration menu using the normal interact key
- Built for big bases: no work per adapter per frame (see Performance)
- One file: the model, textures and preview images are inside QuantumConveyors.dll

## Performance

2.0 rewrote how the mod does its work. 1.x got slower with every adapter: each adapter did work every frame, and each receiving adapter searched every channel and every adapter. 2.0 does nothing per adapter per frame: capsules move only when the game offers one to an adapter, each channel keeps its own list of adapters, a full channel refuses at once, and indicators change only when an adapter's state changes.

Measured with 1,480 adapters, 1.1.2 against 2.0:

| | change from 1.1.2 |
|---|---|
| Frames per second | **+43%** |
| Whole frame | -30% |
| MoonFramework's per-frame work | -54% |
| Capsule hand-offs | -41% |
| Adapters' own per-frame work | -100% |

## Mod Manager settings

- **Adapter Price:** Build price for each adapter, from $1 to $1,000. Default: $5.
- **Diagnostics:** Enables verbose troubleshooting logs. Disabled by default.
- **Brain Fart Naming:** This swaps the names of the adapters. Proper semantics is now optional. Disabled by default.

## Requirements

Install these prerequisites before installing Quantum Conveyors:

1. BepInEx 6 IL2CPP for Moon Sweepers
2. The Moon Sweepers BepInEx compatibility patcher required by the game
3. MoonFramework
4. Moon Sweepers 1.0.5

Every player who joins a multiplayer session should use the same game build, BepInEx setup, MoonFramework version, and Quantum Conveyors version.

## Installation

### Manual installation

1. Close Moon Sweepers completely.
2. Install and verify BepInEx and its Moon Sweepers compatibility patcher.
3. Install MoonFramework.
4. Extract the Quantum Conveyors archive into the Moon Sweepers installation directory.
5. Confirm that the file is located like this:

   ```text
   Moon Sweepers/
   └─ BepInEx/
      └─ plugins/
         └─ QuantumConveyors/
            └─ QuantumConveyors.dll
   ```

### Updating from 1.x

Close the game, replace `QuantumConveyors.dll`, and delete the other files in the `QuantumConveyors` folder (the `.obj`, `.mtl` and `.png` files); 2.0 carries them inside the DLL. Your channels are stored in your save, not in this folder, so they are kept: saves load unchanged, with the same building ids, channel names and multiplayer messages.

## First-time setup

1. Open the normal Building Tool.
2. Open the Logistics category.
3. Build a **Quantum Input** on the conveyor that should send resources.
4. Build a **Quantum Output** on the conveyor that should receive resources.
5. Look directly at an adapter and press **E**.
6. Press **New Channel**.
7. Press **Rename**, type a useful name such as `Moon Dust`, and save it.
8. Press **Assign** for that channel.
9. Configure the other adapter and assign it to the same channel.

The output and input must be attached to conveyors. The wireless connection replaces the long physical conveyor run; it does not replace the short conveyor connection at each adapter.

## Configuration menu

The menu shows channel groups (folders) and channels:

- **New Channel** creates a channel and immediately opens its rename field. **New Group** creates a root-level folder.
- Click anywhere on a folder row or its arrow to expand/collapse it. Folders can be nested to any depth.
- On a channel: **Assign** (or **Unassign**) this adapter, **Rename**, and **Delete**.
- On a group: **Rename** and **Delete**. Deleting a group moves its contents up a level; no channel is deleted.
- **Auto Group** files only channels created while enabled. Matching is case-insensitive, existing matching folders are reused, and older channels are not reorganized when the setting is enabled later.
- **Unassigned** lists channels with no adapters; **Not grouped** lists channels outside any group.
- Drag channels and folders to organize them. **E**, **Escape**, or right-click closes the menu.

Channel names are limited to 24 characters. Channel names are labels only; the actual channel identity is stored separately, so renaming a channel or moving it between groups does not break its connections.

Only the host has authority to create, rename, delete, or assign channels in multiplayer.

## Channel behavior

Adapters on different channels never share resources. If a channel contains several outputs and several inputs, the channel acts as one shared wireless pool.

The receiving adapters on a channel take turns, using a remembered round-robin cursor. A receiver whose belt is full is skipped until the next hand-off, and when every receiver on a channel is full the channel refuses at once, so the source capsule stays on its belt. Multiple source belts can still compete according to the game's conveyor update order because source belts offer capsules independently.

Resource type compatibility is left to the receiving conveyor and game cargo rules. If the receiving belt is full or unavailable, the source capsule is not discarded; normal belt backpressure stops the source belt from feeding the adapter.

## Status indicators

| Indicator | Meaning |
|---|---|
| Solid red | Waiting for setup; the adapter has not been assigned to a channel |
| Flashing red | Conveyor missing or unusable for the adapter's required connection |
| Flashing yellow | No matching adapter is assigned to the same channel, or the destination/output belt is full |
| Solid green | Ready to send or ready to receive |
| Flashing green | Sending or receiving |

The hover panel also shows the channel and a short operational status.

## Deleting and reconnecting conveyors

Deleting an adapter removes it and releases its conveyor port.

Deleting the conveyor attached to an adapter does not automatically delete the adapter. The adapter remains in the world, disables transfer, and keeps its channel assignment. Reconnect a compatible conveyor to restore operation.

## Multiplayer

Quantum Conveyors uses host-authoritative routing. The host decides channel assignments and resource transfers, while assignment changes are sent to other players.

For the first multiplayer tests:

- Install the same Quantum Conveyors version for every player.
- Install the same MoonFramework version for every player.
- Have the host create and configure the channels.
- Test one output and one input before adding multiple adapters.
- Save after building and assigning adapters, then test loading the save.

Multiplayer support is included, but hasn't been tested.

## FAQ

### Does Quantum Conveyors replace normal conveyors?

No. Each adapter occupies a conveyor port and still needs a short physical conveyor connection. The wireless network replaces the long connection between locations.

### Can I use several outputs and inputs on one channel?

Yes. A channel can contain multiple outputs and inputs. They share resources through that channel. Use separate channel names when networks must remain isolated.

### Can one input feed several outputs?

Yes. The receiving adapters take turns.

### Can several inputs feed one output?

Yes. The inputs share the channel and compete to send through the shared network according to the game's conveyor update order.

### Does distance matter?

There is no configured wireless range. An output and input can be placed anywhere in the world, subject to the normal game save and multiplayer limitations.

### Does this eliminate the 10,000-canister limit?

It can avoid the intermediate canister chain required by a very long physical conveyor run, which may reduce entity pressure for that route. It does not remove the game's global limit; ordinary source and destination conveyors still use normal game entities.

### What happens if a receiving belt is full?

The Quantum Input transfers only when the destination Quantum Output can accept the resource. If it cannot, the source capsule remains on its original belt and normal conveyor backpressure prevents additional transfer.

### What happens if I delete a conveyor under an adapter?

The adapter remains, disables transfer, and keeps its channel assignment. Reconnect a compatible conveyor to restore it. Deleting the adapter itself removes it and releases the port.

### Can I load my main save with the mod enabled?

Yes, but make a backup first. Loading a save with the mod enabled is not expected to change anything unless you build or configure Quantum Conveyors adapters. Never test an unverified game/mod update on your only copy of a save.

### Will my 1.x save and channels still work?

Yes. 2.0 uses the same building ids, saved data and channel names.

## Known issues and limitations

- **Underlying splitter/merger clearance:** Although the visible Quantum adapter is an industrial gateway, the game evaluates its placement using the underlying splitter/merger footprint and overlap rules. An adapter may not fit directly against a merger, machine, or another structure if there is not enough clearance. A short conveyor segment or stub may be needed to give the adapter room to place. This can also occur when the conveyor segment being used at a machine is too short.
- **Multiple inputs:** Multiple source belts can compete based on the game's update order. Fair round-robin source scheduling is planned.
- **Game update compatibility:** Moon Sweepers updates can change internal APIs used by BepInEx, MoonFramework, conveyor handling, or save loading. A game update should be treated as untested until the mod is verified. 2.0's speed-ups are patches to MoonFramework; if MoonFramework changes, a patch that no longer fits is skipped (the log says so) and the adapters still work, more slowly.
- **Save compatibility without the mod:** The adapters are stored using vanilla-compatible building identities. Without the mod, they may appear as ordinary splitter/merger structures and will not provide wireless behavior. Back up saves before disabling or uninstalling the mod.
- **No wireless operating limits:** Power use, range, bandwidth, and channel limits are not currently configurable because the network is unrestricted.
- **Terrain cleanup footprint:** The dusty-ground cleanup area currently follows the stock splitter/merger construction body and may be larger than the visible gateway model.
- **No priority routing yet:** All adapters in a channel currently participate in shared routing; per-channel priorities are planned.
- **Global canister limit:** Wireless transfer avoids the long physical conveyor run and its intermediate canisters, but it does not remove Moon Sweepers' global canister/entity limit. Source and destination belts still use normal game entities.

## Troubleshooting

### The Quantum buildings are not in the Building Tool

- Confirm BepInEx, the Moon Sweepers patcher, and MoonFramework are installed.
- Confirm the DLL is under `BepInEx/plugins/QuantumConveyors/`.
- Confirm the game is the supported 1.0.5 build.
- Close and relaunch the game after changing mod files.

### Pressing E does nothing

- Stand within a few metres of the adapter.
- Aim at the adapter gateway itself, not the conveyor or nearby machine.
- Make sure another object is not between the camera and the adapter.
- Confirm the adapter's hover panel appears.
- In multiplayer, have the host perform channel changes.

### The menu opens but resources do not transfer

- Confirm both adapters are on the same channel.
- Confirm the Quantum Input is attached to the source conveyor.
- Confirm the Quantum Output is attached to the destination conveyor.
- Confirm the destination belt has room.
- Check the LEDs and hover status for `No matching adapter`, `Conveyor missing`, or `Output belt full`.
- Verify that all multiplayer players have the same mod and framework versions.

A Quantum Input takes a conveyor ending within about a centimetre of its port. (In 1.x, one a few millimetres off could stay dead.)

### The adapter appears as a splitter or merger

This usually means Quantum Conveyors did not load, or the game was launched without the modding runtime. Confirm the BepInEx installation and check that MoonFramework loads before Quantum Conveyors.

## Uninstallation

1. Back up any saves containing Quantum Conveyors adapters.
2. Close Moon Sweepers.
3. Remove the `BepInEx/plugins/QuantumConveyors/` folder.
4. Leave BepInEx and MoonFramework installed if other mods use them.

Do not remove the mod from a save you care about without a backup. Without Quantum Conveyors, its adapters may load as vanilla splitter/merger structures and all wireless channel behavior will be unavailable.

## Development roadmap

- Fair round-robin scheduling across multiple source adapters
- Optional channel costs, power use, range, bandwidth, and limits
- Optional priority routing
- Broader multiplayer testing

## Bug reports

Please include:

- Moon Sweepers version/build
- Quantum Conveyors version
- MoonFramework version
- Whether the issue occurred in single-player or multiplayer
- Whether you were host or client
- What the adapter LEDs and hover panel displayed
- A short reproduction sequence
- `BepInEx/LogOutput.log` or the Quantum Conveyors diagnostic log when available

Please do not upload Moon Sweepers game files or generated game assemblies. Only provide the mod files and relevant logs.

## Credits and compatibility note

Quantum Conveyors is a community runtime mod and is not an official Moon Sweepers feature. It depends on BepInEx, the Moon Sweepers compatibility patcher, and MoonFramework. Game updates may require a new mod build.
