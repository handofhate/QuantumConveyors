# Quantum Conveyors

Wireless conveyor logistics for Moon Sweepers.

Quantum Conveyors adds small wireless adapters to the normal Building Tool. Place a Quantum Input on a conveyor carrying resources away from a source, place a Quantum Output on the destination conveyor, assign both adapters to the same named channel, and resources can transfer without a physical conveyor connecting the two locations.

## Current release

- Mod: Quantum Conveyors 1.1.2
- Target game build: Moon Sweepers 1.0.5
- Status: Stable 1.1 release; single-player behavior is tested, with host-authoritative multiplayer support included

Please back up your save before using any mods.

## Features

- Quantum Output and Quantum Input building-tool entries under Logistics
- Industrial gateway-style adapter model mounted over the conveyor
- Corrected industrial gateway preview images in the Building Tool
- Red, yellow, and green status indicator with flashing warning/activity states
- Soft colored LED halo and directional illumination
- Named wireless channels
- Unlimited channels
- Multiple outputs and multiple inputs per channel
- No wireless range, power, bandwidth, or operating cost
- Destination-belt backpressure: if the receiving belt cannot accept a resource, the source resource remains on its original belt
- Channel assignments and adapter state persist with the save
- Host-authoritative routing intended for multiplayer
- In-game configuration menu using the normal interact key

## Mod Manager settings

- **Adapter Price:** Build price for each adapter, from $1 to $1,000. Default: $5.
- **Diagnostics:** Enables verbose troubleshooting logs. Disabled by default.
- **Brain Fart Naming:** Swaps the adapter names to the historical Output/Input labels. Disabled by default.

## Requirements

Install these prerequisites before installing Quantum Conveyors:

1. BepInEx 6 IL2CPP for Moon Sweepers
2. The Moon Sweepers BepInEx compatibility patcher required by the game
3. MoonFramework 1.0.5.2
4. Moon Sweepers 1.0.5

Every player who joins a multiplayer session should use the same game build, BepInEx setup, MoonFramework version, and Quantum Conveyors version.

## Installation

### Manual installation

1. Close Moon Sweepers completely.
2. Install and verify BepInEx and its Moon Sweepers compatibility patcher.
3. Install MoonFramework.
4. Extract the Quantum Conveyors archive into the Moon Sweepers installation directory.
5. Confirm that the files are located like this:

   ```text
   Moon Sweepers/
   └─ BepInEx/
      └─ plugins/
         └─ QuantumConveyors/
            ├─ QuantumConveyors.dll
            ├─ quantum-input.obj
            ├─ quantum-input.mtl
            ├─ quantum-output.obj
            ├─ quantum-output.mtl
            ├─ quantum-endpoint-collision.obj
            ├─ quantum-input-preview.png
            └─ quantum-output-preview.png
   ```


## First-time setup

1. Open the normal Building Tool.
2. Open the Logistics category.
3. Build a **Quantum Input** on the conveyor that should send resources.
4. Build a **Quantum Output** on the conveyor that should receive resources.
5. Look directly at an adapter and press **E**.
6. Press **New channel**.
7. Select **Rename**, type a useful name such as `Moon Dust`, and press **Save**.
8. Press **Assign** for that channel.
9. Configure the other adapter and assign it to the same channel.

The output and input must be attached to conveyors. The wireless connection replaces the long physical conveyor run; it does not replace the short conveyor connection at each adapter.

## Configuration menu

- **New channel** creates an unassigned channel named `New Channel`.
- **Assign** connects the currently selected adapter to that channel.
- **Unassign** removes the adapter from that channel.
- **Rename** changes the channel name.
- **Delete** removes the channel and unassigns every adapter using it.
- **×** closes the menu.

Channel names are limited to 24 characters. Channel names are labels only; the actual channel identity is stored separately, so renaming a channel does not break its connections.

Only the host has authority to create, rename, delete, or assign channels in multiplayer.

## Channel behavior

Adapters on different channels never share resources. If a channel contains several outputs and several inputs, the channel acts as one shared wireless pool.

At present, multiple destinations are selected using a remembered round-robin cursor where possible. Multiple source belts can still compete according to the game's conveyor update order because source belts offer capsules independently. Fully fair source/input scheduling is planned but is not yet guaranteed.

Resource type compatibility is left to the receiving conveyor and game cargo rules. If the receiving belt is full or unavailable, the source capsule is not intentionally discarded; normal belt backpressure should stop the source belt from feeding the adapter.

## Status indicators

| Indicator | Meaning |
|---|---|
| Solid red | Waiting for setup; the adapter has not been assigned to a channel |
| Solid yellow | No matching adapter is assigned to the same channel |
| Flashing red | Conveyor missing or unusable for the adapter's required connection |
| Solid green | Ready to send or ready to receive resources |
| Flashing green | Sending or receiving |
| Flashing yellow | The destination/output belt is full |

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

Yes. The current router can deliver to multiple attached outputs, but source/input fairness is still being improved. The game may offer capsules from competing source belts in its own update order.

### Can several inputs feed one output?

Yes. The inputs share the channel and compete to send through the shared network according to the current routing behavior.

### Does distance matter?

There is no configured wireless range. An output and input can be placed anywhere in the world, subject to the normal game save and multiplayer limitations.

### Does this eliminate the 10,000-canister limit?

It can avoid the intermediate canister chain required by a very long physical conveyor run, which may reduce entity pressure for that route. It does not remove the game's global limit; ordinary source and destination conveyors still use normal game entities.

### What happens if a receiving belt is full?

The Quantum Input transfers only when the destination Quantum Output can accept the resource. If it cannot, the source capsule remains on its original belt and normal conveyor backpressure should prevent additional transfer.

### What happens if I delete a conveyor under an adapter?

The adapter remains, disables transfer, and keeps its channel assignment. Reconnect a compatible conveyor to restore it. Deleting the adapter itself removes it and releases the port.

### Can I load my main save with the mod enabled?

Yes, but make a backup first. Loading a save with the mod enabled is not expected to change anything unless you build or configure Quantum Conveyors adapters. Never test an unverified game/mod update on your only copy of a save.

## Known issues and limitations

- **Moving adapters:** The game's new move feature does not reliably resnap splitter/merger-style structures to a new conveyor location. If an adapter is moved away from its original conveyor, deleting and rebuilding it is currently the reliable workaround.
- **Underlying splitter/merger clearance:** Although the visible Quantum adapter is an industrial gateway, the game evaluates its placement using the underlying splitter/merger footprint and overlap rules. An adapter may not fit directly against a merger, machine, or another structure if there is not enough clearance. A short conveyor segment or stub may be needed to give the adapter room to place. This can also occur when the conveyor segment being used at a machine is too short.
- **Multiple inputs:** Multiple source belts can compete based on the game's update order. Fair round-robin source scheduling is planned.
- **Game update compatibility:** Moon Sweepers updates can change internal APIs used by BepInEx, MoonFramework, conveyor handling, or save loading. A game update should be treated as untested until the mod is verified.
- **Save compatibility without the mod:** The adapters are stored using vanilla-compatible building identities. Without the mod, they may appear as ordinary splitter/merger structures and will not provide wireless behavior. Back up saves before disabling or uninstalling the mod.
- **No wireless operating limits:** Power use, range, bandwidth, and channel limits are not currently configurable because the network is unrestricted.
- **Terrain cleanup footprint:** The dusty-ground cleanup area currently follows the stock splitter/merger construction body and may be larger than the visible gateway model.
- **No priority routing yet:** All adapters in a channel currently participate in shared routing; per-channel priorities are planned.
- **Global canister limit:** Wireless transfer avoids the long physical conveyor run and its intermediate canisters, but it does not remove Moon Sweepers' global canister/entity limit. Source and destination belts still use normal game entities.

## Troubleshooting

### The Quantum buildings are not in the Building Tool

- Confirm BepInEx, the Moon Sweepers patcher, and MoonFramework are installed.
- Confirm the DLL is under `BepInEx/plugins/QuantumConveyors/`.
- Confirm the `.obj` and `.mtl` files are beside the DLL.
- Confirm the game is the supported 1.0.5 build.
- Close and relaunch the game after changing mod files.

### Pressing E does nothing

- Stand close to the adapter.
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

### The adapter appears as a splitter or merger

This usually means Quantum Conveyors did not load, or the game was launched without the modding runtime. Confirm the BepInEx installation and check that MoonFramework loads before Quantum Conveyors.

## Uninstallation

1. Back up any saves containing Quantum Conveyors adapters.
2. Close Moon Sweepers.
3. Remove the `BepInEx/plugins/QuantumConveyors/` folder.
4. Leave BepInEx and MoonFramework installed if other mods use them.

Do not remove the mod from a save you care about without a backup. Without Quantum Conveyors, its adapters may load as vanilla splitter/merger structures and all wireless channel behavior will be unavailable.

## Development roadmap

- Fair round-robin scheduling across multiple source/output adapters
- More reliable conveyor attachment and missing-conveyor detection
- Adapter movement that resnaps to conveyor ports
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
