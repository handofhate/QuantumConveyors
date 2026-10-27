# MoonFramework 0.6.0 findings

The downloaded MoonFramework archive exposes the pieces Quantum Conveyors needs:

- `BuildingSpec` registers a new Building Tool card with a stable mod-owned id, price, category, footprint, height, conveyor ports, model, and optional machine behaviour.
- `PortSpec.In` and `PortSpec.Out` define the direction and position of a port. A registered building can therefore occupy a port in the same conveyor graph instead of patching stock conveyor code first.
- A building with ports and no machine is a framework router. A machine can be added when Quantum Conveyors needs custom endpoint state or routing decisions.
- `Machine.Data` and `Moon.WorldData` provide mod-owned save data.
- Machine logic runs only on `Moon.IsAuthority`, which gives us a host-authoritative multiplayer boundary. `Crew.Send`/`Crew.On` are available for small mod-specific messages; framework buildings and machine panels are already synchronized.
- `ModelSpec` supports an OBJ model with named parts, so the endpoint can remain a small visible port attachment rather than a full-size building.

This supports the intended staged approach: first register and place a harmless endpoint, then validate one wireless output/input pair, and only then add multi-endpoint fair routing and priority settings.

The framework archive is kept under `work\downloads` and is not committed to Git. The game still needs to be closed before the runtime and framework can be installed.
