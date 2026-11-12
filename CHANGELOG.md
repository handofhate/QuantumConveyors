Version 2.0.0

- Rewritten for speed: nothing runs per adapter per frame. Capsules move when MoonFramework offers one; adapters are left out of MoonFramework's per-frame Tick, Animate and power loops. Measured on a base with about 1,500 adapters: 20.7 fps with 1.1.2.
- Each channel keeps its own list of adapters; a channel whose receiving adapters are all full stops being asked until the next capsule hand-off pass, and refused capsules are turned away before MoonFramework reads them out of the game.
- Receiving adapters remember where their belt starts instead of looking up every port point for every capsule.
- Indicators share a few materials and change only when an adapter's state changes. The spot light and halo sprite are gone (each adapter had a shadow-casting light updated every frame).
- The E key looks for an adapter only when pressed, among adapters within 6 m with nothing in front of them.
- Channel groups: folders for channels, as deep as you like (New Group, Open, Rename, Move, Delete; Move for channels). An "Auto-group by name" switch files channels by the names they already have ("Iron-Line-2" goes into Iron > Line as "2"). "Unassigned" lists channels with no adapters.
- One file: the model, textures and build-menu pictures are inside QuantumConveyors.dll. Delete the old QuantumConveyors folder's other files when updating (the DLL alone is enough).
- Saves, building ids, channel names and crew messages are unchanged: 1.x worlds load as they are, and groups saved by Quantum Fast are moved over.

Version 1.1.2

- Added proper build-menu previews.
- Adjusted Mod Manager settings and added settings for adapter price, diagnostics, and a name-swapping Brain Fart mode.
- Modified configuration UI to prevent background game animation pausing and mini teleports when exiting the menu.
- Improved styling of the configuration UI.

Version 1.1.1

- Refined adapter E key targeting to prevent overlap interaction issues.

Version 1.1

- Replaced the temporary puck-style adapter appearance with the industrial gateway model.
- Added a directional LED light and a soft colored halo effect.
- Improved the channel configuration window, including scrolling, channel creation/deletion, renaming, assignment/unassignment, and input focus handling.
- Added E, Escape, and right-click menu closing, while preserving E for channel names when the rename field is focused.
- Improved interaction targeting around the full gateway model.
- Native in-game move tool is now supported.
