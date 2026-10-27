# Setup status

## Prepared

- Project created under `C:\Users\Ty\Documents\Projects\Moon Sweepers\Quantum Conveyors`.
- The BepInEx 6 IL2CPP win-x64 build `6.0.0-be.791+b782da4` was downloaded and staged locally.
- A first diagnostic-only plug-in project is present. It does not change gameplay; it will enumerate the generated Moon Sweepers types and methods once the IL2CPP compatibility layer is active.
- The diagnostic plug-in builds successfully with a project-local .NET 6/8 SDK and has been copied into the staged BepInEx plug-in folder.

## Waiting on the game being closed

Installing files into the game directory should happen only after Moon Sweepers is closed. The user is currently preparing a dedicated test save.

## Current external download limitation

Nexus Mods exposes the MoonFramework and MoonSweepers BepInEx Patcher files, but the current browser session is not logged in and Nexus requires an account before downloading. The relevant files are:

- MoonFramework 0.6.0, which provides new Building Tool entries and multiplayer synchronization.
- MoonSweepers BepInEx Patcher 1.0.0, required for this game’s IL2CPP metadata version.

The framework page identifies MoonFramework as the intended way to add new buildings, belt ports, models, save IDs, and multiplayer synchronization. We should use it instead of recreating that infrastructure.

An installation script is ready at `tools\Install-PreviewRuntime.ps1`. It refuses to run while Moon Sweepers is open, moves any existing runtime files into a timestamped project backup, then installs BepInEx, the patcher, MoonFramework, and the diagnostic plug-in together.
