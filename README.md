# Quantum Conveyors

Wireless logistics for Moon Sweepers.

Quantum Conveyors adds small, placeable endpoints to the normal Building Tool. A player assigns endpoints to existing conveyor ports, then links outputs to inputs. Resources move through the wireless network without requiring a physical belt between the endpoints.

## Current target

- Moon Sweepers preview 1.0.5
- Windows x64 Steam build
- Unity 6000.6 / IL2CPP
- BepInEx 6 IL2CPP with the Moon Sweepers compatibility patcher
- Multiplayer support with the host authoritative over transfers

## Design commitments

- A port is either connected to a stock conveyor or occupied by a Quantum Conveyor endpoint; both cannot use the same port.
- Initial wireless networks are unrestricted: no cost, range, power, or bandwidth limit.
- Multiple sources and destinations share resources fairly.
- Priority routing is a later optional feature, not required for the first working version.
- Endpoint hardware is deliberately small and visible, using a simple glow/ring until a final art direction is chosen.
- Links and endpoint placement should persist with the world save.

## Development stages

1. Install and verify the IL2CPP modding runtime.
2. Generate and inspect current 1.0.5 interop assemblies.
3. Add a diagnostic plugin that identifies build-tool entries, compatible ports, conveyor networks, and cargo transfer methods.
4. Add the Quantum Endpoint building-tool item and its visual marker.
5. Implement one output to one input transfer.
6. Add fair multi-source/multi-destination routing.
7. Add save/load and multiplayer host synchronization.
8. Add optional routing priorities and configurable limits.

## Important boundaries

This is a runtime mod, not an official game extension. Game updates can change internal class names or method signatures. The mod should fail closed and leave stock conveyors working when compatibility checks fail.

Do not distribute copies of Moon Sweepers game files or generated game assemblies. The project should distribute only the mod source, build output, and installation instructions.
