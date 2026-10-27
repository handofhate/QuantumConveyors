# Setup status

## Prepared

- Project created under `C:\Users\Ty\Documents\Projects\Moon Sweepers\Quantum Conveyors`.
- The BepInEx 6 IL2CPP win-x64 build `6.0.0-be.791+b782da4` was downloaded and staged locally.
- The Moon Sweepers BepInEx Patcher archive was downloaded and staged locally. Its SHA256 is `B495D504E97B6B4A9E0B0D08162CC1C63F3B7AEED3677F88C18006504C56781F`.
- MoonFramework 0.6.0 was downloaded and staged locally. Its SHA256 is `96361DCB8157F20F6C3CA12E9295F9CE0A633E7D0012776AB358A563B5D402CA`.
- A first diagnostic-only plug-in project is present. It does not change gameplay; it will enumerate the generated Moon Sweepers types and methods once the IL2CPP compatibility layer is active.
- The diagnostic plug-in builds successfully with a project-local .NET 6/8 SDK and has been copied into the staged BepInEx plug-in folder.

## Waiting on the game being closed

The BepInEx runtime, MoonSweepers patcher, MoonFramework 0.6.0, and the first Quantum Conveyors prototype have now been installed successfully. The game reached the main menu and loaded both plugins without a Quantum Conveyors exception. The first prototype registered `Quantum Output` and `Quantum Input` in the Logistics Building Tool.

The previous prototype build is installed and loaded by the game. The startup log confirms both Quantum buildings register successfully, and no Quantum Conveyors or MoonFramework errors were found. No buildings were placed and no save changes were made during this verification pass.

The current build adds isolated, unbounded channels, a host-authoritative channel-assignment message for multiplayer, an E-to-interact setup window, and a black portal-square endpoint model with a top LED. It passed a clean local compile and was installed after the game was closed. The game has now been launched with this build; in-game behavior testing is pending.

The current working update also applies belt backpressure: the output endpoint tries to send directly into an available destination belt before accepting a capsule. When destination belts are full, the origin capsule stays on its origin belt. Endpoints whose attached belt is deleted remain as disabled objects, preserving their channel assignment until a new belt is connected.

The latest runtime installation is backed up at `work\backups\runtime-install\20261026-222638`.

## External files prepared

Nexus Mods requires an account before downloading. The relevant files are now prepared locally:

- MoonFramework 0.6.0, which provides new Building Tool entries and multiplayer synchronization.
- MoonSweepers BepInEx Patcher 1.0.0, required for this game’s IL2CPP metadata version.

The framework page identifies MoonFramework as the intended way to add new buildings, belt ports, models, save IDs, and multiplayer synchronization. We should use it instead of recreating that infrastructure.

An installation script is ready at `tools\Install-PreviewRuntime.ps1`. It refuses to run while Moon Sweepers is open, moves any existing runtime files into a timestamped project backup, then installs BepInEx, the patcher, MoonFramework, and the diagnostic plug-in together.
