# With LunarLink

Quantum Conveyors is a normal BepInEx plugin and needs nothing else. It's also **hot-compatible**: with
[LunarLink](https://swheettaos.github.io/lunarlink/) it can be loaded, reloaded, turned off and updated while the game runs.

| | On its own | With LunarLink |
|---|---|---|
| **Install it in** | `BepInEx/plugins/QuantumConveyors/` | `BepInEx/hot/QuantumConveyors.dll` (straight in, not in a subfolder) |
| **Updating** | Download, close the game, replace the DLL, start again | F9 → UPDATES → **Update**: installed in place, **no restart** |
| **Turning it off** | Remove the DLL, restart | F9 → HOT PLUGINS → **Disable**: transfers stop, and it's unloaded from memory |
| **Scripts and tools** | - | HTTP requests under `/quantum/` ([[For Tools]]) |

## Running it hot

1. Install [LunarLink](https://github.com/swheettaos/lunarlink/releases/latest).
2. Put `QuantumConveyors.dll` straight in `BepInEx/hot/`, and **remove it from `BepInEx/plugins/`**: a plugin in both
   folders would load twice, so LunarLink refuses the hot copy.
3. Start the game. The adapters are registered once at game start, so they're in the Building Tool as usual. Load a
   moon and turn it on: **F9 → HOT PLUGINS → Enable**.

## What a reload does

`Conveyors.Boot()` runs once at game start and registers the two adapters (MoonFramework only accepts buildings then).
Then LunarLink calls `Start(state)`, `Update()`, `LateUpdate()` and, before a copy is dropped, `Stop()`. A reload swaps
every adapter's machine for the new copy's, from the same saved data, and rebuilds them at once. Disabling makes the
adapters inert, so transfers stop.

Changing the adapters themselves (model, ports, price, names) still needs a game restart.

## Updates

Quantum Conveyors tells LunarLink where its releases are (`github:handofhate/QuantumConveyors`), so **F9 → UPDATES**
lists it: **Check for updates**, then **Update** when a new version is out.
