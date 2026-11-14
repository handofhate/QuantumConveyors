# Using It

## The adapters

| Adapter | Where it goes | What it does |
|---|---|---|
| **Quantum Input** | On the **end** of a belt | Takes the belt's capsules and sends them into its channel |
| **Quantum Output** | On the **start** of a belt | Puts capsules from its channel onto the belt |

Both are in the Building Tool's **Logistics** tab. A channel can have any number of inputs and outputs; the outputs
take turns, and an output whose belt is full is skipped until it has room. (The **Brain Fart Naming** setting swaps
the two names, if you think of them the other way round: [[Settings]].)

## The E menu

Walk up to an adapter and press **E**. The menu shows channel groups (folders) and channels:

- **New Channel** creates a channel and opens its rename field at once. **New Group** creates a top-level folder.
- Click a folder row to expand or collapse it. Folders nest to any depth.
- **Drag** channel and folder rows to move them, with a preview, drop feedback and auto-scroll at the edges.
- On a channel: **Assign** (or **Unassign**) this adapter, **Rename**, **Delete**.
- On a group: **Rename** and **Delete**. Deleting a group moves its contents up a level; it never deletes channels.
- **Unassigned** lists channels with no adapters; **Not grouped** lists channels outside any group. These two are
  filters, not folders you can move.
- **Auto Group** files new channels by their name: [[Auto Group]].

E, Escape or right-click closes the menu.

## The lights

| Light | Means |
|---|---|
| Green | Ready, or working |
| Yellow, blinking | Nothing on the other side of the channel, or the receiving belt is full |
| Red | Waiting for a channel, or its belt is missing |
