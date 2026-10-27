# Quantum Conveyors design brief

## Player experience

The player opens the normal Building Tool, chooses **Quantum Endpoint**, and places a tiny endpoint directly on a compatible machine, depot, or conveyor port. The endpoint is assigned as an input or output during placement or through a small interaction panel.

The player then selects another endpoint to create a wireless link. A subtle line, pulse, or endpoint color can communicate the connection without filling the world with permanent belt geometry.

## Port ownership

Quantum Conveyors does not share a single port with a stock conveyor. A compatible port has one owner:

- Stock conveyor
- Quantum output endpoint
- Quantum input endpoint

Removing an endpoint releases the port so a stock conveyor can be connected again.

## Routing

The initial router treats every ready input and output in a connected Quantum network as a shared pool. It allocates available cargo fairly, using round-robin selection with remembered cursors so one destination does not permanently starve another.

Resource type compatibility must be preserved. An input that cannot accept a cargo type is skipped, and cargo must not be silently destroyed when a destination is full or disconnected.

Priority routing is planned as an opt-in mode. It should not change the default fair behavior and should be configurable per endpoint or per network.

## Multiplayer

The host owns the authoritative endpoint registry, network links, routing decisions, and cargo transfers. Other players receive the resulting endpoint/link state and visual updates. Every player should install the same mod for the first multiplayer release so clients have matching custom objects and UI.

The first multiplayer test must cover:

1. Host places and links endpoints.
2. Client sees endpoint visuals and links.
3. Client places or removes an endpoint.
4. Cargo transferred by the host appears correctly to every player.
5. Save/load preserves the same network.

## Safety rules

- Never patch stock transport behavior unless the compatibility check confirms the expected method and fields.
- If Quantum Conveyors is missing or disabled, stock conveyors must remain usable.
- Preserve all saved state in a separate mod-owned section where possible.
- Back up mod-owned save data before migration.
- Treat a game update as unsupported until a diagnostic run confirms the relevant conveyor and building APIs.
