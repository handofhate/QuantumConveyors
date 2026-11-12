using System;
using System.Collections.Generic;

namespace QuantumConveyors;

// Reads a group path out of a channel name someone typed (for "Auto-group by name"): split at separators, and a trailing
// number becomes the channel's own short name.
//   "Iron-Line-2" -> Iron > Line, "2"      "Copper Smelter 3" -> Copper Smelter, "3"      "base_A/sand 12" -> base > A > sand, "12"
//   "Main" -> nothing to group (one part)    "Furnace 2" -> Furnace, "2"
// Pure string work (no game types), so it can be tested on its own.
internal static class NameSplit
{
    static readonly string[] Seps = { " > ", ">", "/", "\\", "|", ":", "-", "_" };

    internal static bool Split(string name, out List<string> path, out string shortName)
    {
        path = new List<string>(); shortName = null;
        name = (name ?? string.Empty).Trim(); if (name.Length == 0) return false;
        var parts = new List<string> { name };
        foreach (var sep in Seps)
        {
            var next = new List<string>();
            foreach (var p in parts) foreach (var q in p.Split(sep)) { var t = q.Trim(); if (t.Length > 0) next.Add(t); }
            parts = next;
        }
        if (parts.Count == 0) return false;
        // a trailing number on the last part is its own level: "Smelter 3" / "dustC2" -> "Smelter" / "dustC", "3" / "2"
        string last = parts[parts.Count - 1]; int e = last.Length;
        while (e > 0 && char.IsDigit(last[e - 1])) e--;
        if (e > 0 && e < last.Length)
        {
            string head = last.Substring(0, e).TrimEnd(' ', '#', '.'), num = last.Substring(e);
            if (head.Length > 0) { parts[parts.Count - 1] = head; parts.Add(num); }
        }
        if (parts.Count < 2) return false;
        shortName = parts[parts.Count - 1]; parts.RemoveAt(parts.Count - 1); path = parts;
        return true;
    }

    // how deep each name's groups go: a level is kept only while at least two names share it, so a one-off name makes no
    // group of its own; the levels dropped are kept in front of the short name ("Iron > Smelter 1" next to Iron > Mine)
    internal static List<(List<string> path, string shortName)> Plan(IList<string> names)
    {
        var split = new List<(List<string>, string)>(); var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
        {
            if (!Split(n, out var path, out var s)) { split.Add((null, null)); continue; }
            split.Add((path, s));
            for (int i = 1; i <= path.Count; i++) { string key = string.Join("\u0001", path.GetRange(0, i)); count.TryGetValue(key, out var c); count[key] = c + 1; }
        }
        var result = new List<(List<string>, string)>();
        foreach (var (path, s) in split)
        {
            if (path == null) { result.Add((null, null)); continue; }
            int d = 0; while (d < path.Count && count[string.Join("\u0001", path.GetRange(0, d + 1))] >= 2) d++;
            if (d == 0) { result.Add((null, null)); continue; }
            var rest = path.GetRange(d, path.Count - d); rest.Add(s);
            result.Add((path.GetRange(0, d), string.Join(" ", rest)));
        }
        return result;
    }
}
