# Auto Group

With **Auto Group** on (a switch in the E menu), a channel you create is filed into groups by its name: the parts of
a build-style name become folders.

| You name a channel | It's filed as |
|---|---|
| `Iron-Line-2` | **Iron > Line**, channel "2" |
| `copper-smelter-1` | **copper > smelter**, channel "1" (an existing **Copper > Smelter** is reused: matching ignores case) |

- It only touches channels **created while it's on**. Older channels are left where they are.
- An existing folder with a matching name is reused rather than duplicated.
- To file older channels too, use `POST /quantum/groups/convert` ([[For Tools]]).
