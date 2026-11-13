using System;
using System.Collections.Generic;
using System.Text;
using MoonFramework;

namespace QuantumConveyors;

// A folder the player makes for channels. Groups hold channels and other groups, as deep as wanted.
internal sealed class Group
{
    public readonly string Id; public string Name; public string Parent;            // Parent "" = top level
    public Group(string id, string name, string parent) { Id = id; Name = name; Parent = parent ?? string.Empty; }
}

// Channel groups (user, 2026-11-08: "I'd rather have the plugin just do it and not based on names"). Each channel keeps
// a short name inside its group; its full name is the group path plus that short name ("West > west1 > Chamber dust >
// Floor 2 > 2"). The full name is also saved where Quantum Conveyors keeps channel names, so a save stays readable by
// Quantum Conveyors 1.x and by crew-mates on it. Groups and placements are saved in our own world data:
//   group.<gid> = name   parent.<gid> = parent gid   in.<channel id> = gid   short.<channel id> = short name
public static partial class QuantumNetwork
{
    const string GroupKey = Plugin.PluginGuid + ".groups";
    const string AutoCreatedPrefix = "auto.created.";
    const string GroupsMsg = Plugin.PluginGuid + ".groups";
    const string QuantumFastKey = "swheet.moonsweepers.quantumfast";   // where Quantum Fast (an earlier fork) kept them
    internal const string Sep = " > ";
    static readonly Dictionary<string, Group> groups = new(StringComparer.Ordinal);
    static object loadedWorld;

    static IDictionary<string, string> GroupData() { try { return Moon.WorldData(GroupKey); } catch { return null; } }
    static IDictionary<string, string> NameData() { try { return Moon.WorldData(WorldKey); } catch { return null; } }

    static void MarkAutoGroupCandidate(string channelId)
    {
        var gd = GroupData();
        if (gd != null && AutoGroupOn) gd[AutoCreatedPrefix + channelId] = "1";
    }

    static bool IsAutoGroupCandidate(string channelId)
    {
        var gd = GroupData();
        return gd != null && gd.TryGetValue(AutoCreatedPrefix + channelId, out var value) && value == "1";
    }

    static void ClearAutoGroupCandidate(string channelId) => GroupData()?.Remove(AutoCreatedPrefix + channelId);

    static void ClearAutoGroupCandidates()
    {
        var gd = GroupData(); if (gd == null) return;
        foreach (var key in new List<string>(gd.Keys)) if (key.StartsWith(AutoCreatedPrefix, StringComparison.Ordinal)) gd.Remove(key);
    }

    // a world's data appears after its load: then every saved channel (with adapters or not) and every group is read
    static void EnsureLoaded()
    {
        var wd = NameData();
        if (wd == null || ReferenceEquals(wd, loadedWorld)) return;
        loadedWorld = wd;
        foreach (var id in new List<string>(channels.Keys))                          // left over from another world
            if (channels[id].Senders.Count == 0 && channels[id].Receivers.Count == 0) channels.Remove(id);
        groups.Clear();
        var gd = GroupData();
        FromQuantumFast(gd);
        if (gd != null)
        {
            foreach (var kv in gd)
                if (kv.Key.StartsWith("group.", StringComparison.Ordinal))
                {
                    string gid = kv.Key.Substring(6);
                    groups[gid] = new Group(gid, kv.Value, gd.TryGetValue("parent." + gid, out var p) ? p : string.Empty);
                }
            foreach (var g in groups.Values) if (g.Parent.Length > 0 && (!groups.ContainsKey(g.Parent) || InSubtree(g.Parent, g.Id))) g.Parent = string.Empty;
        }
        foreach (var kv in new List<KeyValuePair<string, string>>(wd))
            if (kv.Key.StartsWith("name.", StringComparison.Ordinal)) Ensure(kv.Key.Substring(5));
        foreach (var c in channels.Values) LoadPlacement(c);
    }

    // a world saved by Quantum Fast: its groups move to this plugin's key (once; the old key is emptied)
    static void FromQuantumFast(IDictionary<string, string> gd)
    {
        if (gd == null) return;
        IDictionary<string, string> old; try { old = Moon.WorldData(QuantumFastKey); } catch { return; }
        if (old == null || old.Count == 0) return;
        if (gd.Count == 0) foreach (var kv in old) gd[kv.Key] = kv.Value;
        old.Clear();
        Log?.LogInfo("channel groups moved from Quantum Fast's save data");
    }

    static void LoadPlacement(Channel c)
    {
        var gd = GroupData();
        c.Group = gd != null && gd.TryGetValue("in." + c.Id, out var g) && groups.ContainsKey(g) ? g : null;
        c.Short = c.Group != null && gd.TryGetValue("short." + c.Id, out var s) && !string.IsNullOrWhiteSpace(s) ? s : c.Name;
        if (c.Group != null) SetName(c, false);
    }

    // ------------------------------------------------------------ paths and names
    internal static string GroupName(string gid) => gid != null && groups.TryGetValue(gid, out var g) ? g.Name : null;
    internal static string GroupParent(string gid) => gid != null && groups.TryGetValue(gid, out var g) ? g.Parent : string.Empty;
    internal static string GroupPath(string gid)
    {
        var parts = new List<string>(); int guard = 0;
        for (string at = gid; !string.IsNullOrEmpty(at) && groups.TryGetValue(at, out var g) && guard++ < 64; at = g.Parent) parts.Insert(0, g.Name);
        return string.Join(Sep, parts);
    }
    static bool InSubtree(string gid, string root)                               // is gid root itself or inside it
    {
        int guard = 0;
        for (string at = gid; !string.IsNullOrEmpty(at) && guard++ < 64; at = groups.TryGetValue(at, out var g) ? g.Parent : null) if (at == root) return true;
        return false;
    }
    static string FullName(Channel c) => c.Group == null ? c.Short : GroupPath(c.Group) + Sep + c.Short;

    static void SetName(Channel c, bool tellCrew = true)
    {
        string full = FullName(c); if (string.IsNullOrWhiteSpace(full) || c.Name == full) return;
        c.Name = full;
        var wd = NameData(); if (wd != null) wd["name." + c.Id] = full;
        if (tellCrew && Moon.IsAuthority && Crew.InCrew) Crew.Send(RenameMsg, Encoding.UTF8.GetBytes(c.Id + "\t" + full));
    }

    static void Place(Channel c, string gid, string shortName, bool tell = true)
    {
        c.Group = string.IsNullOrEmpty(gid) || !groups.ContainsKey(gid) ? null : gid; c.Short = shortName;
        var gd = GroupData();
        if (gd != null)
        {
            if (c.Group == null) { gd.Remove("in." + c.Id); gd.Remove("short." + c.Id); }
            else { gd["in." + c.Id] = c.Group; gd["short." + c.Id] = shortName; ClearAutoGroupCandidate(c.Id); }
        }
        SetName(c, tell);
    }

    static void Rename(string root)                                               // every channel under a group gets its new full name
    {
        foreach (var c in channels.Values) if (c.Group != null && InSubtree(c.Group, root)) SetName(c);
    }

    // ------------------------------------------------------------ what the menu shows
    internal static List<string> ChildGroups(string gid)
    {
        gid ??= string.Empty; var list = new List<string>();
        foreach (var g in groups.Values) if (g.Parent == gid) list.Add(g.Id);
        list.Sort((a, b) => Natural(groups[a].Name, groups[b].Name));
        return list;
    }
    internal static string GroupOf(string channelId) => ChannelOf(channelId)?.Group;
    internal static string ShortName(string channelId) => ChannelOf(channelId)?.Short ?? string.Empty;
    internal static List<ChannelSummary> ChannelsIn(string gid)
    {
        EnsureLoaded(); var list = new List<ChannelSummary>();
        foreach (var c in channels.Values) if ((c.Group ?? string.Empty) == (gid ?? string.Empty)) list.Add(new ChannelSummary(c.Id, c.Name, c.Senders.Count, c.Receivers.Count));
        list.Sort((a, b) => Natural(ShortName(a.Id), ShortName(b.Id)));
        return list;
    }
    // channels and adapters under each group (its subgroups included)
    internal static Dictionary<string, (int channels, int adapters)> GroupCounts()
    {
        EnsureLoaded(); var d = new Dictionary<string, (int, int)>();
        foreach (var c in channels.Values)
        {
            int guard = 0;
            for (string at = c.Group; !string.IsNullOrEmpty(at) && groups.TryGetValue(at, out var g) && guard++ < 64; at = g.Parent)
            { d.TryGetValue(at, out var v); d[at] = (v.Item1 + 1, v.Item2 + c.Senders.Count + c.Receivers.Count); }
        }
        return d;
    }
    internal static int Natural(string a, string b)                               // "2" before "10"
    {
        a ??= string.Empty; b ??= string.Empty; int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j; while (i < a.Length && char.IsDigit(a[i])) i++; while (j < b.Length && char.IsDigit(b[j])) j++;
                string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                int c = string.CompareOrdinal(na, nb); if (c != 0) return c;
            }
            else
            {
                int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j])); if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
    internal static string NextShort(string gid)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in channels.Values) if ((c.Group ?? string.Empty) == (gid ?? string.Empty)) used.Add(c.Short);
        int n = 1; while (used.Contains(n.ToString())) n++;
        return n.ToString();
    }

    // ------------------------------------------------------------ changes (host only; crew-mates get them sent)
    public static bool CreateGroup(string parent, string name, out string gid)
    {
        gid = null; EnsureLoaded();
        if (!Moon.IsAuthority) return false;
        name = Clean(name); if (string.IsNullOrWhiteSpace(name)) return false;
        parent = parent != null && groups.ContainsKey(parent) ? parent : string.Empty;
        int n = 1; do gid = "g" + n++; while (groups.ContainsKey(gid));
        groups[gid] = new Group(gid, name, parent);
        var gd = GroupData(); if (gd != null) { gd["group." + gid] = name; gd["parent." + gid] = parent; }
        SendGroups(); return true;
    }

    public static bool RenameGroup(string gid, string name)
    {
        EnsureLoaded();
        if (!Moon.IsAuthority || gid == null || !groups.TryGetValue(gid, out var g)) return false;
        name = Clean(name); if (string.IsNullOrWhiteSpace(name)) return false;
        g.Name = name; var gd = GroupData(); if (gd != null) gd["group." + gid] = name;
        Rename(gid); SendGroups(); return true;
    }

    // what was inside moves up to the group's parent; no channel is deleted
    public static bool DeleteGroup(string gid)
    {
        EnsureLoaded();
        if (!Moon.IsAuthority || gid == null || !groups.TryGetValue(gid, out var g)) return false;
        var gd = GroupData();
        foreach (var child in groups.Values) if (child.Parent == gid) { child.Parent = g.Parent; if (gd != null) gd["parent." + child.Id] = g.Parent; }
        var inside = new List<Channel>(); foreach (var c in channels.Values) if (c.Group == gid) inside.Add(c);
        groups.Remove(gid); if (gd != null) { gd.Remove("group." + gid); gd.Remove("parent." + gid); }
        foreach (var c in inside)
        {
            string s = c.Short; foreach (var o in channels.Values) if (o != c && (o.Group ?? "") == g.Parent && string.Equals(o.Short, s, StringComparison.OrdinalIgnoreCase)) { s = g.Name + " " + c.Short; break; }
            Place(c, g.Parent, s);
        }
        foreach (var c in channels.Values) if (c.Group != null) SetName(c);
        SendGroups(); return true;
    }

    public static bool MoveGroup(string gid, string parent)
    {
        EnsureLoaded(); parent ??= string.Empty;
        if (!Moon.IsAuthority || gid == null || !groups.TryGetValue(gid, out var g)) return false;
        if (parent.Length > 0 && (!groups.ContainsKey(parent) || InSubtree(parent, gid))) return false;  // not into itself
        g.Parent = parent; var gd = GroupData(); if (gd != null) gd["parent." + gid] = parent;
        Rename(gid); SendGroups(); return true;
    }

    // the channel keeps its short name unless that name is taken in the new group (then the next free number)
    public static bool MoveChannel(string channelId, string gid)
    {
        EnsureLoaded(); var c = ChannelOf(channelId); gid ??= string.Empty;
        if (!Moon.IsAuthority || c == null || (gid.Length > 0 && !groups.ContainsKey(gid))) return false;
        string s = c.Short;
        foreach (var o in channels.Values) if (o != c && (o.Group ?? "") == gid && string.Equals(o.Short, s, StringComparison.OrdinalIgnoreCase)) { s = NextShort(gid); break; }
        Place(c, gid, s); SendGroups(); return true;
    }

    // a new channel inside a group, named with the next free number there
    public static bool CreateChannelIn(string gid, out string channelId)
    {
        channelId = string.Empty; EnsureLoaded();
        if (!Moon.IsAuthority || !CreateChannel(out channelId)) return false;
        if (!string.IsNullOrEmpty(gid) && groups.ContainsKey(gid)) { Place(ChannelOf(channelId), gid, NextShort(gid)); SendGroups(); }
        return true;
    }

    // ------------------------------------------------------------ our build names -> groups
    // The build scripts name channels "<unit>-<code>" (west1-dustC2, west1-sand11, west1-port3-brick). This files such a
    // name as Base > Unit > Type > Floor > number (the floor a channel is sent from: catchers and chambers on 2, presses
    // on 1). Names it doesn't know stay as they are, outside any group.
    static bool ParseCode(string name, out string[] path, out string shortName)
    {
        path = null; shortName = null; name = (name ?? string.Empty).Trim();
        int dash = name.IndexOf('-'); if (dash <= 0) return false;
        string unit = name.Substring(0, dash), rest = name.Substring(dash + 1);
        int k = 0; while (k < unit.Length && char.IsLetter(unit[k])) k++;
        if (k == 0 || k == unit.Length) return false;
        for (int j = k; j < unit.Length; j++) if (!char.IsDigit(unit[j])) return false;
        string bse = char.ToUpperInvariant(unit[0]) + unit.Substring(1, k - 1).ToLowerInvariant();
        string type, floor, num; string r = rest.ToLowerInvariant();
        string Digits(string s, int from) { int e = from; while (e < s.Length && char.IsDigit(s[e])) e++; return e > from && e == s.Length ? s.Substring(from) : null; }
        if (r.StartsWith("dustp") && (num = Digits(r, 5)) != null) { type = "Press dust"; floor = "Floor 2"; }
        else if (r.StartsWith("dustc") && (num = Digits(r, 5)) != null) { type = "Chamber dust"; floor = "Floor 2"; }
        else if (r.StartsWith("cdust") && (num = Digits(r, 5)) != null) { type = "Chamber dust"; floor = "Floor 2"; }
        else if (r.StartsWith("sand") && (num = Digits(r, 4)) != null) { type = "Sand"; floor = "Floor 2"; }
        else if (r.StartsWith("port") && r.EndsWith("-brick") && (num = Digits(r.Substring(0, r.Length - 6), 4)) != null) { type = "Bricks"; floor = "Floor 1"; }
        else return false;
        path = new[] { bse, unit.ToLowerInvariant(), type, floor }; shortName = num.TrimStart('0').Length > 0 ? num.TrimStart('0') : "0";
        return true;
    }

    static string EnsurePath(string[] path)
    {
        string at = string.Empty;
        foreach (var part in path)
        {
            string found = null;
            foreach (var g in groups.Values) if (g.Parent == at && string.Equals(g.Name, part, StringComparison.OrdinalIgnoreCase)) { found = g.Id; break; }
            if (found == null)
            {
                int n = 1; do found = "g" + n++; while (groups.ContainsKey(found));
                groups[found] = new Group(found, part, at);
                var gd = GroupData(); if (gd != null) { gd["group." + found] = part; gd["parent." + found] = at; }
            }
            at = found;
        }
        return at;
    }

    // public, for other mods: name a channel with a build name, filed into its groups when the name is known
    public static bool FileByCode(string channelId, string name)
    {
        EnsureLoaded(); var c = ChannelOf(channelId);
        if (!Moon.IsAuthority || c == null) return false;
        if (!ParseCode(name, out var path, out var s)) return RenameChannel(channelId, name);
        Place(c, EnsurePath(path), s); SendGroups(); return true;
    }

    // one-off: every channel outside a group whose name is a build name is filed into its groups -> how many
    public static int ConvertNames()
    {
        EnsureLoaded(); if (!Moon.IsAuthority) return 0;
        int n = 0;
        foreach (var c in new List<Channel>(channels.Values))
            if (c.Group == null && ParseCode(c.Name, out var path, out var s)) { Place(c, EnsurePath(path), s); n++; }
        SendGroups(); return n;
    }

    // ------------------------------------------------------------ "Auto-group by name" (a tick box in the menu, saved per world)
    // Only channels created while the box is on are candidates. They are checked when renamed; turning the box on does
    // not reorganize existing channels, and turning it off clears any still-pending candidates without undoing groups.
    public static bool AutoGroupOn { get { var gd = GroupData(); return gd != null && gd.TryGetValue("auto", out var v) && v == "1"; } }
    public static int SetAutoGroup(bool on)
    {
        EnsureLoaded(); if (!Moon.IsAuthority) return 0;
        var gd = GroupData(); if (gd != null) { if (on) gd["auto"] = "1"; else gd.Remove("auto"); }
        if (!on) ClearAutoGroupCandidates();
        return 0;
    }
    internal static int AutoGroupIfOn() => AutoGroupOn && Moon.IsAuthority ? AutoGroup() : 0;

    // every eligible channel outside a group whose name shares a group with another -> filed there; returns how many
    public static int AutoGroup()
    {
        EnsureLoaded(); if (!Moon.IsAuthority) return 0;
        var loose = new List<Channel>(); foreach (var c in channels.Values) if (c.Group == null && IsAutoGroupCandidate(c.Id)) loose.Add(c);
        if (loose.Count == 0) return 0;
        var names = new List<string>(); foreach (var c in loose) names.Add(c.Name);
        var plan = NameSplit.Plan(names); int n = 0;
        for (int i = 0; i < loose.Count; i++)
        {
            var (path, s) = plan[i]; if (path == null) continue;
            string gid = EnsurePath(path.ToArray());
            foreach (var o in channels.Values) if (o != loose[i] && o.Group == gid && string.Equals(o.Short, s, StringComparison.OrdinalIgnoreCase)) { s = loose[i].Name; break; }
            Place(loose[i], gid, s); n++;
            ClearAutoGroupCandidate(loose[i].Id);
        }
        if (n > 0) SendGroups();
        return n;
    }
    internal static int UngroupedCount() { EnsureLoaded(); int n = 0; foreach (var c in channels.Values) if (c.Group == null) n++; return n; }

    // ------------------------------------------------------------ crew: the whole group table, after each change
    static void SendGroups()
    {
        if (!Moon.IsAuthority || !Crew.InCrew) return;
        var sb = new StringBuilder();
        foreach (var g in groups.Values) sb.Append("g\t").Append(g.Id).Append('\t').Append(g.Parent).Append('\t').Append(g.Name).Append('\n');
        foreach (var c in channels.Values) if (c.Group != null) sb.Append("c\t").Append(c.Id).Append('\t').Append(c.Group).Append('\t').Append(c.Short).Append('\n');
        Crew.Send(GroupsMsg, Encoding.UTF8.GetBytes(sb.ToString()));
    }
    static void ApplyGroups(byte[] data)
    {
        if (Moon.IsAuthority || data == null) return;
        EnsureLoaded(); groups.Clear();
        var placed = new Dictionary<string, (string g, string s)>();
        foreach (var line in Encoding.UTF8.GetString(data).Split('\n'))
        {
            var p = line.Split('\t', 4); if (p.Length < 4) continue;
            if (p[0] == "g") groups[p[1]] = new Group(p[1], p[3], p[2]);
            else if (p[0] == "c") placed[p[1]] = (p[2], p[3]);
        }
        foreach (var c in channels.Values)
            if (placed.TryGetValue(c.Id, out var v)) { c.Group = v.g; c.Short = v.s; } else { c.Group = null; c.Short = c.Name; }
    }
}
