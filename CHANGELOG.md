Version 2.1.0

- Answers HTTP requests for tools, through a public HandleHttp method that a mod serving HTTP can pass requests on to (without such a mod nothing calls it): list channels and adapters (GET /quantum/channels), create, rename, assign and delete channels, file build-named channels into groups, and see how much each channel moved lately and which receivers placed nothing (GET /quantum/flow). Changes work on the host only, as in the menu.

Version 2.0.0

- Rewritten the transport internals for large bases: capsules move when MoonFramework offers one, rather than every adapter doing work every frame. Quantum adapters are removed from MoonFramework's per-frame Tick, Animate, and power loops when the compatibility patches apply.
- Added per-channel adapter lists and waiting queues. Full receiving belts are skipped, refused transfers stay on the source belt, and a full channel stops being queried until the next hand-off pass.
- Added remembered round-robin selection for receiving adapters, while source belts still compete according to the game's conveyor update order.
- Cached input positions and receiving belt start points instead of repeatedly searching every adapter and port. A belt ending within about a centimetre of a Quantum Input now counts, fixing the old dead-input edge case.
- Optimized indicators: shared materials, state-change updates, timed sending/receiving and belt-full states, and no per-adapter shadow-casting light update.
- Added channel folders with unlimited nesting, inline expand/collapse, Unassigned and Not Grouped sections, scrolling, edge fades, drag-and-drop organization, drag auto-scroll, drop previews, and animated folder/list changes.
- Added Auto Group by name. It is case-insensitive, only affects channels created while enabled, leaves older channels alone, and reuses matching groups when possible.
- Reworked the configuration menu: full-row expansion, focused rename fields, correct creation cancellation, keyboard/input focus protection, E/Escape/right-click closing, help tooltips, improved status text, and a visual refresh matching the in-game Mod Manager.
- Removed the old Move buttons; channels and folders are organized by dragging their rows.
- Treated Unassigned and Not Grouped as non-movable virtual sections so dragging them cannot enter the folder drag-preview path or lock the game.
- Refined E targeting so the adapter gateway is selectable without accidentally opening through nearby machines or objects.
- Added native move-tool support so moved adapters can reconnect and resume their saved channel assignment.
- Added proper build-menu preview images and embedded the model, textures, and previews in the single QuantumConveyors.dll file.
- Saves, building ids, channel names, groups, and crew messages remain compatible with the 1.x data format.

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
