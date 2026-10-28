Quantum Conveyors 1.1
=====================

Wireless conveyor adapters for Moon Sweepers.

Requirements
------------

- Moon Sweepers 1.0.5
- BepInEx 6 IL2CPP
- The Moon Sweepers BepInEx compatibility patcher
- MoonFramework, using the version listed on the Quantum Conveyors release page

Installation
------------

1. Close Moon Sweepers.
2. Extract this archive into the Moon Sweepers game folder.
3. Merge the included BepInEx folder with the existing BepInEx folder.
4. Launch the game through your normal modded-game method.

The installed files belong in:

BepInEx/plugins/QuantumConveyors/

Every multiplayer player should use the same game build, BepInEx setup,
MoonFramework version, and Quantum Conveyors version.

Basic use
---------

1. Open the normal Building Tool and place a Quantum Output or Quantum Input.
2. Attach it to a conveyor. Leave enough straight conveyor space for the adapter;
   it occupies the physical port area used by a stock splitter/merger-style part.
3. Aim at the adapter and press E to open its configuration window.
4. Assign both ends to the same named channel. Multiple inputs and outputs can
   share a channel.
5. Use the close button, E, Escape, or right-click to close the window.

Important notes
---------------

- Channels are currently unrestricted: there is no cost, range, power, or bandwidth limit.
- The host controls channel assignments and transfers in multiplayer.
- If the attached conveyor is deleted, the adapter disables transfer and shows
  Conveyor missing. Its channel assignment remains available when a compatible
  conveyor is reconnected.
- Deleting an adapter removes that endpoint from its channel.
- Game updates can change internal APIs used by the mod. Treat a new game build
  as untested until Quantum Conveyors has been verified on it.

Uninstallation
--------------

Close the game, then remove:

BepInEx/plugins/QuantumConveyors/

Leave BepInEx, the patcher, and MoonFramework installed if other mods use them.
