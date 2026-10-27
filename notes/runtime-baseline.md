# Runtime baseline

Captured 2026-10-26 from the installed Steam copy.

- Install: `C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers`
- Game branch: preview 1.0.5
- Build: `06788074`
- Unity: `6000.6.0f1`
- Executable architecture: Windows x64
- Runtime: IL2CPP
- Player log source path: `D:/UnityDevelopment/Moon-Cleaners-release/Assets/Scripts/Moon/`

The log names the following useful systems:

- `MoonConveyorNetwork`
- `MoonConveyorPath`
- `MoonConveyorSave`
- `MoonConveyorSnapshot`
- `MoonConveyorTransport`
- `MoonMachineBelt`
- `MoonDepotBuilder`
- `MoonBuildInfo`

The log also shows the stock transport loop entering `MoonWorld.StepConveyors(Single)` and the garage silo diagnostic reporting belt capacity. These are investigation targets, not yet confirmed patch points.

## Modding runtime status

Not installed at the time this baseline was captured. The current community compatibility route is BepInEx 6.0.0-be.791 IL2CPP win-x64 plus the MoonSweepers BepInEx Patcher. The patcher is required because the game uses IL2CPP metadata version 108.
