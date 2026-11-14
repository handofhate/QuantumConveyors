# For Tools

Quantum Conveyors has a public `HandleHttp(method, path, query, body)` on its plugin class, for mods that serve HTTP and
pass requests on to plugins, such as [LunarLink](https://swheettaos.github.io/lunarlink/) (`http://127.0.0.1:8765/`). Nothing calls it on its own.
Reads are **GET**; changes are **POST** and work **on the host only**, as in the menu. Every answer is JSON; a refused
change answers `{"ok": false, "reason": "..."}`.

The same list, searchable: **[the API page](https://handofhate.github.io/QuantumConveyors/api.html)**.

```bash
A=127.0.0.1:8765
curl -s $A/quantum                         # the summary
curl -s "$A/quantum/flow?minutes=5"        # which channels moved capsules, and which receivers didn't
```

## Recipes

**Find a stalled line.** `GET /quantum/flow?minutes=5`: a channel with `"moved": 0`, or receivers in
`idleReceivers` (marked "(no belt)" when one has no belt), points at the line to look at.

**Wire a new line.** `POST /quantum/channels/create?name=Iron-Line-3` (filed into Iron > Line), then
`GET /quantum/adapters` for the new adapters' ids, and `POST /quantum/adapters/modify?id=&channel=` for each.

**Tidy an old base.** `POST /quantum/groups/convert` files every ungrouped build-named channel into groups.

## Every request

<!-- include: api -->
