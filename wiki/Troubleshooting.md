# Troubleshooting

**Capsules don't move on a channel.** Look at the adapters' lights ([[Using It]]):
- **Red**: the adapter has no channel, or no belt. Assign it in the E menu, or rebuild the belt.
- **Yellow, blinking**: there's no adapter on the other side of the channel, or the receiving belt is full.
- With LunarLink, `GET /quantum/flow` lists the receivers that placed nothing ([[For Tools]]).

**A Quantum Input doesn't take capsules.** The belt has to **end** on the input. A belt ending a few millimetres off
still counts (the neighbouring centimetre is accepted); further off, rebuild the belt's end onto the input.

**The adapters aren't in the Building Tool.** Check the log: Quantum Conveyors' start line should be there, and
MoonFramework must be installed and loaded first.

**Running it with LunarLink and nothing happens.** A new hot plugin starts **off**: F9 → HOT PLUGINS → **Enable**. If F9
says it was refused, a second copy is still in `BepInEx/plugins/`: remove one.

**Low fps with many adapters.** 2.0 should cost very little per adapter. If a MoonFramework update broke one of its
patches, the log says which was skipped: report it with the log lines.

**More detail.** Turn on `Diagnostics` ([[Settings]]), make it happen again, and [open an issue](https://github.com/handofhate/QuantumConveyors/issues/new/choose)
with the log lines and your base's size (adapters and channels).
