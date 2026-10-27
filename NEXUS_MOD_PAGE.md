# Quantum Conveyors

Wireless conveyor logistics for Moon Sweepers.

Quantum Conveyors adds small wireless endpoints to the normal Building Tool. Place a Quantum Output on a conveyor carrying resources away from a source, place a Quantum Input on the destination conveyor, assign both endpoints to the same named channel, and resources can transfer without a physical conveyor connecting the two locations.

## Current release

- Mod: Quantum Conveyors 0.1.0 preview
- Target game build: Moon Sweepers Preview 1.0.5
- Platform: Windows x64
- Status: Early public preview; single-player behavior is the primary tested path

Please back up your save before testing any preview mod.

## Features

- Quantum Output and Quantum Input building-tool entries under Logistics
- Small puck-like endpoint model mounted over the conveyor
- Red, yellow, and green status indicator with flashing warning/activity states
- Named wireless channels
- Unlimited channels in the current preview
- Multiple outputs and multiple inputs per channel
- No wireless range, power, bandwidth, or operating cost in the current preview
- Destination-belt backpressure: if the receiving belt cannot accept a resource, the source resource remains on its original belt
- Channel assignments and endpoint state persist with the save
- Host-authoritative routing intended for multiplayer
- In-game configuration menu using the normal interact key

## Requirements

Install these prerequisites before installing Quantum Conveyors:

1. BepInEx 6 IL2CPP for Moon Sweepers
2. The Moon Sweepers BepInEx compatibility patcher required by the game
3. MoonFramework 0.6.0 or the version specified by the release
4. Moon Sweepers Preview 1.0.5 or a tested compatible build

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
            └─ quantum-endpoint-collision.obj
   ```

6. Launch Moon Sweepers normally through Steam. There is no separate modded launcher.

The first launch after installing BepInEx may take longer than a vanilla launch while the modding runtime prepares files. A BepInEx console window may be visible; leave it open while playing.

### Vortex or mod manager installation

If using a mod manager, the archive must preserve the `BepInEx/plugins/QuantumConveyors/` folder structure. If the DLL is placed directly in `BepInEx/plugins` or inside an extra nested folder, the model files may not be found.

## First-time setup

1. Open the normal Building Tool.
2. Open the Logistics category.
3. Build a **Quantum Output** on the conveyor that should send resources.
4. Build a **Quantum Input** on the conveyor that should receive resources.
5. Look directly at an endpoint and press **E**.
6. Press **New channel**.
7. Select **Rename**, type a useful name such as `Iron Ore`, and press **Save**.
8. Press **Assign** for that channel.
9. Configure the other endpoint and assign it to the same channel.

The output and input must be attached to conveyors. The wireless connection replaces the long physical conveyor run; it does not replace the short conveyor connection at each endpoint.

## Configuration menu

- **New channel** creates an unassigned channel named `New Channel`.
- **Assign** connects the currently selected endpoint to that channel.
- **Unassign** removes the endpoint from that channel.
- **Rename** changes the channel name.
- **Delete** removes the channel and unassigns every endpoint using it.
- **×** closes the menu.

Channel names are limited to 24 characters. Channel names are labels only; the actual channel identity is stored separately, so renaming a channel does not break its connections.

Only the host has authority to create, rename, delete, or assign channels in multiplayer.

## Channel behavior

Endpoints on different channels never share resources. If a channel contains several outputs and several inputs, the channel acts as one shared wireless pool.

At present, multiple destinations are selected using a remembered round-robin cursor where possible. Multiple source belts can still compete according to the game's conveyor update order because source belts offer capsules independently. Fully fair source/output scheduling is planned but is not yet guaranteed.

Resource type compatibility is left to the receiving conveyor and game cargo rules. If the receiving belt is full or unavailable, the source capsule is not intentionally discarded; normal belt backpressure should stop the source belt from feeding the endpoint.

## Status indicators

| Indicator | Meaning |
|---|---|
| Solid red | Waiting for setup; the endpoint has not been assigned to a channel |
| Solid yellow | No matching adapter is assigned to the same channel |
| Flashing red | Conveyor missing or unusable for the endpoint's required connection |
| Solid green | Ready to send or ready to receive |
| Flashing green | Sending or receiving; remains visible briefly after activity |
| Flashing yellow | The destination/output belt has remained full for the delay period |

The hover panel also shows the channel and a short operational status. The activity and full-belt indicators intentionally use short delays so they do not flicker during ordinary belt timing changes.

## Deleting and reconnecting conveyors

Deleting an endpoint removes it and releases its conveyor port.

Deleting the conveyor attached to an endpoint does not automatically delete the endpoint. The endpoint remains in the world, disables transfer, and keeps its channel assignment. Reconnect a compatible conveyor to restore operation.

Because the game reports incoming conveyor connections differently from outgoing connections, output-side missing-belt detection is less precise than input-side detection in this preview. The actual transfer still refuses to consume a source capsule unless a destination can accept it.

## Multiplayer

Quantum Conveyors uses host-authoritative routing. The host decides channel assignments and resource transfers, while assignment changes are sent to other players.

For the first multiplayer tests:

- Install the same Quantum Conveyors version for every player.
- Install the same MoonFramework version for every player.
- Have the host create and configure the channels.
- Test one output and one input before adding multiple endpoints.
- Save after building and assigning endpoints, then test loading the save.

Multiplayer support is included, but this is still an early preview and has not received broad public multiplayer testing.

## FAQ

### Does Quantum Conveyors replace normal conveyors?

No. Each endpoint occupies a conveyor port and still needs a short physical conveyor connection. The wireless network replaces the long connection between locations.

### Can I use several outputs and inputs on one channel?

Yes. A channel can contain multiple outputs and inputs. They share resources through that channel. Use separate channel names when networks must remain isolated.

### Can one output feed several inputs?

Yes. The current router can deliver to multiple attached inputs, but source/output fairness is still being improved. The game may offer capsules from competing source belts in its own update order.

### Can I use one input with several outputs?

Yes. The inputs share the channel and are selected using a remembered round-robin cursor where possible.

### Does distance matter?

There is no configured wireless range in this preview. An output and input can be placed anywhere in the world, subject to the normal game save and multiplayer limitations.

### Does this eliminate the 10,000-canister limit?

It can avoid the intermediate canister chain required by a very long physical conveyor run, which may reduce entity pressure for that route. It does not remove the game's global limit; ordinary source and destination conveyors still use normal game entities.

### What happens if a receiving belt is full?

The output tries to transfer directly only when the destination can accept the resource. If it cannot, the source capsule remains on its original belt and normal conveyor backpressure should prevent additional transfer.

### What happens if I delete a conveyor under an adapter?

The adapter remains, disables transfer, and keeps its channel assignment. Reconnect a compatible conveyor to restore it. Deleting the adapter itself removes it and releases the port.

### Can I load my main save with the mod enabled?

Yes, but make a backup first. Loading a save with the mod enabled is not expected to change anything unless you build or configure Quantum Conveyors endpoints. Never test an unverified game/mod update on your only copy of a save.

### Do I need to disable Steam automatic updates?

No. It is generally better to keep normal Steam updates enabled. After a game update, treat the mod as unverified until it has been tested on a backup save.

### Is there a separate modded launcher?

No. Launch Moon Sweepers normally through Steam after installing BepInEx and the required dependencies.

## Known issues and limitations

- **Moving adapters:** The game's new move feature does not reliably resnap splitter/merger-style structures to a new conveyor location. If an adapter is moved away from its original conveyor, deleting and rebuilding it is currently the reliable workaround.
- **Underlying splitter/merger clearance:** Although the visible Quantum endpoint is a small puck, the game evaluates its placement using the underlying splitter/merger footprint and overlap rules. An endpoint may not fit directly against a merger, machine, or another structure if there is not enough clearance. A short conveyor segment or stub may be needed to give the endpoint room to place. This can also occur when the conveyor segment being used at a machine is too short.
- **Multiple outputs:** Multiple source belts can compete based on the game's update order. Fair round-robin source scheduling is planned.
- **Preview-build compatibility:** Moon Sweepers updates can change internal APIs used by BepInEx, MoonFramework, conveyor handling, or save loading. A game update should be treated as untested until the mod is verified.
- **Save compatibility without the mod:** The endpoints are stored using vanilla-compatible building identities. Without the mod, they may appear as ordinary splitter/merger structures and will not provide wireless behavior. Back up saves before disabling or uninstalling the mod.
- **No configuration file yet:** Cost, power use, range, bandwidth, and channel limits are not configurable in this preview.
- **No priority routing yet:** All endpoints in a channel currently participate in shared routing; per-channel priorities are planned.
- **Precise interaction:** The current build requires the endpoint itself to be the first object under the crosshair. A machine or other object in front of a hidden adapter should take priority instead of opening the adapter menu.
- **Game shutdown time:** A slower exit can come from Unity save processing, Steam Cloud, BepInEx, IL2CPP cleanup, or MoonFramework. Quantum Conveyors performs no long shutdown task, but the combination has not been benchmarked across all installations.
- **Global canister limit:** Wireless transfer avoids the long physical conveyor run and its intermediate canisters, but it does not remove Moon Sweepers' global canister/entity limit. Source and destination belts still use normal game entities.

## Troubleshooting

### The Quantum buildings are not in the Building Tool

- Confirm BepInEx, the Moon Sweepers patcher, and MoonFramework are installed.
- Confirm the DLL is under `BepInEx/plugins/QuantumConveyors/`.
- Confirm the `.obj` and `.mtl` files are beside the DLL.
- Confirm the game is the supported Preview 1.0.5 build.
- Close and relaunch the game after changing mod files.

### Pressing E does nothing

- Stand close to the endpoint.
- Aim at the puck itself, not the conveyor or nearby machine.
- Make sure another object is not between the camera and the endpoint.
- Confirm the endpoint's hover panel appears.
- In multiplayer, have the host perform channel changes.

### The menu opens but resources do not transfer

- Confirm both endpoints are on the same channel.
- Confirm the output is attached to the source conveyor.
- Confirm the input is attached to the destination conveyor.
- Confirm the destination belt has room.
- Check the LEDs and hover status for `No matching adapter`, `Conveyor missing`, or `Output belt full`.
- Verify that all multiplayer players have the same mod and framework versions.

### The endpoint appears as a splitter or merger

This usually means Quantum Conveyors did not load, or the game was launched without the modding runtime. Confirm the BepInEx installation and check that MoonFramework loads before Quantum Conveyors.

### The adapter is in the wrong position

The visible puck is intentionally offset toward the conveyor endpoint. Existing saved placements may look correct while newly built placements expose a difference caused by the game's placement and port rules. Delete and rebuild the adapter if it is not aligned correctly.

## Uninstallation

1. Back up any saves containing Quantum Conveyors endpoints.
2. Close Moon Sweepers.
3. Remove the `BepInEx/plugins/QuantumConveyors/` folder.
4. Leave BepInEx and MoonFramework installed if other mods use them.

Do not remove the mod from a save you care about without a backup. Without Quantum Conveyors, its endpoints may load as vanilla splitter/merger structures and all wireless channel behavior will be unavailable.

## Development roadmap

- Fair round-robin scheduling across multiple source/output endpoints
- More reliable conveyor attachment and missing-conveyor detection
- Adapter movement that resnaps to conveyor endpoints
- Optional channel costs, power use, range, bandwidth, and limits
- Optional priority routing
- Broader multiplayer testing
- Compatibility testing after Moon Sweepers updates
- Improved release packaging and diagnostics

## Release checklist

Before publishing a public Nexus file:

- Build from a clean working tree and verify the DLL and model assets are present.
- Test installation into a clean BepInEx/MoonFramework setup.
- Test a new save with one output and one input.
- Test a backup copy of an existing save.
- Test channel creation, rename, assignment, unassignment, and deletion.
- Test a full destination belt and a deleted/reconnected conveyor.
- Test the mod with Quantum Conveyors disabled so the save fallback is understood.
- Test host and client behavior with the same mod versions.
- Capture clear screenshots of the adapters, LEDs, Building Tool entries, and setup menu.
- Label the release as an early preview and list the supported game build prominently.
- Include the required dependencies and installation path in the Nexus description.
- Do not include Moon Sweepers files, generated game assemblies, or unrelated BepInEx files in the upload.

## Bug reports

Please include:

- Moon Sweepers version/build
- Quantum Conveyors version
- MoonFramework version
- Whether the issue occurred in single-player or multiplayer
- Whether you were host or client
- What the endpoint LEDs and hover panel displayed
- A short reproduction sequence
- `BepInEx/LogOutput.log` or the Quantum Conveyors diagnostic log when available

Please do not upload Moon Sweepers game files or generated game assemblies. Only provide the mod files and relevant logs.

## Credits and compatibility note

Quantum Conveyors is a community runtime mod and is not an official Moon Sweepers feature. It depends on BepInEx, the Moon Sweepers compatibility patcher, and MoonFramework. Game updates may require a new mod build.
