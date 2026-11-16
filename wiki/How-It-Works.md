# How It Works

2.0 is a rewrite of 1.x's internals for big bases. 1.x got slower with every adapter: each adapter did work every
frame, and every receiving adapter's frame searched every channel and every adapter. 2.0 does nothing per frame per
adapter.

| | 1.x | 2.0 |
|---|---|---|
| Moving capsules | every receiver's Tick searched all channels and adapters | done when MoonFramework offers a capsule to a sender; each channel keeps its own adapter lists |
| Full channels | every waiting capsule asked every receiver, every frame | capsules at a Quantum Input wait in their channel's own list; each pass a channel is offered capsules only until it's full |
| MoonFramework's input table | rebuilt twice a second by asking the game for every adapter's ports | a Quantum Input's place in it is remembered until the adapter is built or moved |
| Placing a capsule | port looked up by name, per capsule | each receiver remembers its port point |
| Indicators | own material, a shadow-casting light and a halo per adapter, set every frame | a few shared materials; redrawn only when the state changes |
| MoonFramework's per-frame loops | adapters in Tick, Animate and the power step | left out (adapters need none of them) |
| E key | every adapter's bounds searched every frame | looked up only when E is pressed |
| Belt ends | a belt end a few millimetres off could miss for good | a Quantum Input also takes belts ending in the neighbouring centimetre |

## Measured

With 1,480 adapters, 1.1.2 against 2.0:

| | change from 1.1.2 |
|---|---|
| fps | **+43%** |
| whole frame | -30% |
| game and mod scripts | -35% |
| MoonFramework driver (all framework machines) | -54% |
| capsule hand-offs | -41% |
| power step | -35% |
| adapters' per-frame Tick | -100% |

The changes to MoonFramework's loops are Harmony patches (`FrameworkPatches.cs`). Each one **fails closed**: if
MoonFramework changes, that patch is skipped, the log says so, and the adapters still work the slow way.

## The source

`src/QuantumConveyors.Plugin/`:

| File | What it does |
|---|---|
| `Plugin.cs` | settings, the two buildings, the per-frame driver, the hot lifecycle |
| `Assets.cs` | unpacks the embedded model and pictures to `BepInEx/cache/QuantumConveyors/<version>/` |
| `Network.cs` | channels, moving capsules, crew messages |
| `CrewChannels.cs` | crew games: the host's channels sent to crew-mates, and crew-mates' changes (Crew Changes: Off, Ask, Auto) |
| `Groups.cs`, `NameSplit.cs` | channel groups, and reading groups out of names |
| `Machines.cs` | the two adapters |
| `Indicators.cs` | indicator lights |
| `Menu.cs` | the E menu: opening it on the adapter you look at, closing, clicks and scrolling |
| `Menu.List.cs` | the menu's channel list: sections, folders that open inline, channels, and how a rebuild slides |
| `Menu.Drag.cs` | dragging channels and folders in the menu |
| `Menu.Input.cs` | holding the player still while the menu is open |
| `Menu.Parts.cs` | the menu's generated sprites and UI pieces |
| `FrameworkPatches.cs`, `Framework.cs` | MoonFramework patches, and handing machines over on a reload |
| `Http.cs`, `Flow.cs` | the `/quantum/` requests, and how much each channel moved |
