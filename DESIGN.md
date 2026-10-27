# Quantum Conveyors design brief

## Player experience

The player opens the normal Building Tool and chooses a tiny **Quantum Output** or **Quantum Input** endpoint card. Separate cards are the safest first implementation because MoonFramework registers each building's conveyor ports when the build menu is created; it also keeps the interaction obvious for a non-technical player. A later panel can add a mode switch if the framework and placement rules make that useful.

Each endpoint is placed directly on a compatible machine, depot, or conveyor port. The visible model should be only a small port-mounted component, with no unnecessary floor footprint.

The player walks up to an endpoint and presses the normal interact key. An endpoint starts **unassigned**. The small setup menu can create a new channel or add this endpoint to an existing channel. A channel has no fixed size: it can contain any number of outputs and inputs.

The endpoint model is a small black square portal mounted on top of the conveyor, with a tiny color LED on top. Outputs and inputs use different default LED colors. The channel name is shown in the endpoint status panel and in the setup menu.

## Port ownership

Quantum Conveyors does not share a single port with a stock conveyor. A compatible port has one owner:

- Stock conveyor
- Quantum output endpoint
- Quantum input endpoint

Removing an endpoint releases the port so a stock conveyor can be connected again.

If the conveyor attached to an endpoint is deleted, the endpoint remains in place but disables itself. It is removed from routing, stops accepting or emitting resources, and hides its interaction prompt until a conveyor is connected again. This preserves the player's channel assignment without silently deleting a buildable object.

## Routing

Each channel is an isolated network. The router treats every ready output and input in that channel as a shared pool. It allocates available cargo fairly, using round-robin selection with remembered cursors so one destination does not permanently starve another. This means output A can feed input A while output B feeds input B, even though both pairs use the same world.

Resource type compatibility must be preserved. An input that cannot accept a cargo type is skipped, and cargo must not be silently destroyed when a destination is full or disconnected. The output adapter now attempts a direct transfer before accepting a capsule from its origin belt; if every destination belt is full, the origin capsule remains on its belt and the origin belt's normal backpressure stops further transfer.

Priority routing is planned as an opt-in mode. It should not change the default fair behavior and should be configurable per endpoint or per network.

## Multiplayer

The host owns the authoritative endpoint registry, channel assignments, routing decisions, and cargo transfers. Channel assignment messages are sent to other players so their endpoint panels and visuals update during the session. Every player should install the same mod for the first multiplayer release so clients have matching custom objects and UI.

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
