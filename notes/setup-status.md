# Setup status

## Prepared

- Project created under `C:\Users\Ty\Documents\Projects\Moon Sweepers\Quantum Conveyors`.
- The BepInEx 6 IL2CPP win-x64 build `6.0.0-be.791+b782da4` was downloaded and staged locally.
- The Moon Sweepers BepInEx Patcher archive was downloaded and staged locally. Its SHA256 is `B495D504E97B6B4A9E0B0D08162CC1C63F3B7AEED3677F88C18006504C56781F`.
- MoonFramework 0.6.0 was downloaded and staged locally. Its SHA256 is `96361DCB8157F20F6C3CA12E9295F9CE0A633E7D0012776AB358A563B5D402CA`.
- A first diagnostic-only plug-in project is present. It does not change gameplay; it will enumerate the generated Moon Sweepers types and methods once the IL2CPP compatibility layer is active.
- The diagnostic plug-in builds successfully with a project-local .NET 6/8 SDK and has been copied into the staged BepInEx plug-in folder.

## Waiting on the game being closed

Installing files into the game directory should happen only after Moon Sweepers is closed. The user is currently preparing a dedicated test save.

## External files prepared

Nexus Mods requires an account before downloading. The relevant files are now prepared locally:

- MoonFramework 0.6.0, which provides new Building Tool entries and multiplayer synchronization.
- MoonSweepers BepInEx Patcher 1.0.0, required for this game’s IL2CPP metadata version.

The framework page identifies MoonFramework as the intended way to add new buildings, belt ports, models, save IDs, and multiplayer synchronization. We should use it instead of recreating that infrastructure.

An installation script is ready at `tools\Install-PreviewRuntime.ps1`. It refuses to run while Moon Sweepers is open, moves any existing runtime files into a timestamped project backup, then installs BepInEx, the patcher, MoonFramework, and the diagnostic plug-in together.
