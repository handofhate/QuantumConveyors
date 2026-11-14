# Installing

## What you need

- **Moon Sweepers** with **[BepInEx 6.0.0-be.791](https://builds.bepinex.dev/projects/bepinex_be)** (Unity.IL2CPP, win-x64), run once.
- The **[MoonSweepers BepInEx Patcher](https://www.nexusmods.com/moonsweepers/mods/9)**.
- **MoonFramework**: Quantum Conveyors' adapters are MoonFramework machines.

In a crew game, everyone who builds or uses the adapters needs the mod; the host moves the capsules ([[Crew Games]]).

## Install

1. Close the game.
2. Put `QuantumConveyors.dll` in `BepInEx/plugins/QuantumConveyors/`. That one file is the whole mod: the model,
   textures and pictures are inside it (they're unpacked to `BepInEx/cache/QuantumConveyors/<version>/` at start).
3. Start the game. The adapters are in the Building Tool's **Logistics** tab.

**Updating from 1.x:** delete the folder's other files (models, pictures): 2.x needs only the DLL. Saves from 1.x load
unchanged: same building ids, saved data, channel names and crew messages.

To run it hot with LunarLink instead, see [[With LunarLink]].

## Uninstalling

Close the game and delete `BepInEx/plugins/QuantumConveyors/` (and `BepInEx/cache/QuantumConveyors/`).
