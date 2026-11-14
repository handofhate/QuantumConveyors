using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Logging;
using MoonCleaners;
using MoonFramework;
using UnityEngine;

namespace QuantumConveyors;

// Channels and the adapters on them. Saves, crew messages and the public methods (other mods call QuantumNetwork by
// name) are 1.x's; the work is organised per channel instead of searching every adapter. Channel groups: Groups.cs.

public readonly record struct ChannelSummary(string Id, string Name, int Outputs, int Inputs);
public readonly record struct QuantumEndpointView(int Id, bool IsOutput, GameObject View);
internal readonly record struct PendingCapsule(MoonCargo Cargo, double Kilograms, double Capacity, string Owner);
internal enum QuantumTransferResult { Sent, NoMatchingAdapter, DestinationUnavailable }

internal sealed class Channel
{
    public readonly string Id; public string Name;
    public readonly List<QuantumOutputMachine> Senders = new();     // take capsules off a belt
    public readonly List<QuantumInputMachine> Receivers = new();    // put capsules on a belt
    public int Cursor;                                              // round robin over the receivers
    public int FullPass;                                            // every receiver refused in this hand-off pass
    public string Short, Group;                                     // name inside its group, the group (null = none) - Groups.cs
    public Channel(string id, string name) { Id = id; Name = name; Short = name; }
}

public static partial class QuantumNetwork
{
    const string WorldKey = Plugin.PluginGuid;
    const string AssignMsg = Plugin.PluginGuid + ".assignment";
    const string RenameMsg = Plugin.PluginGuid + ".rename";
    const string DeleteMsg = Plugin.PluginGuid + ".delete";

    static readonly Dictionary<string, Channel> channels = new(StringComparer.Ordinal);
    static readonly Dictionary<int, QuantumOutputMachine> senders = new();
    static readonly Dictionary<int, QuantumInputMachine> receivers = new();
    static readonly Dictionary<int, string> pendingRemote = new();
    internal static readonly List<QuantumOutputMachine> WithPending = new();
    internal static ManualLogSource Log;

    public static void Initialize(ManualLogSource log)
    {
        Log = log;
        Crew.On(AssignMsg, (_, d) => ApplyAssign(d));
        Crew.On(RenameMsg, (_, d) => ApplyRename(d));
        Crew.On(DeleteMsg, (_, d) => ApplyDelete(d));
        Crew.On(GroupsMsg, (_, d) => ApplyGroups(d));
    }

    internal static void Diagnostic(string message)
    {
        if (Plugin.DiagnosticsEnabled) Log?.LogInfo("[diagnostic] " + message);
    }

    // ---------------------------------------------------------------- membership
    internal static void Add(QuantumOutputMachine m)
    {
        Diagnostic($"sender {m.Id} started on '{m.ChannelId}', {m.PendingCount} pending");
        senders[m.Id] = m; Join(m, m.ChannelId);
        if (pendingRemote.Remove(m.Id, out var ch)) m.SetChannel(ch);
    }
    internal static void Add(QuantumInputMachine m)
    {
        Diagnostic($"receiver {m.Id} started on '{m.ChannelId}', belt {(m.IsAttached ? "attached" : "missing")}");
        receivers[m.Id] = m; Join(m, m.ChannelId);
        if (pendingRemote.Remove(m.Id, out var ch)) m.SetChannel(ch);
    }
    internal static void Remove(QuantumOutputMachine m) { senders.Remove(m.Id); Leave(m, m.ChannelId); WithPending.Remove(m); }
    internal static void Remove(QuantumInputMachine m) { receivers.Remove(m.Id); Leave(m, m.ChannelId); }

    internal static void Join(object m, string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return;
        var c = Ensure(channelId);
        if (m is QuantumOutputMachine o && !c.Senders.Contains(o)) c.Senders.Add(o);
        if (m is QuantumInputMachine i && !c.Receivers.Contains(i)) c.Receivers.Add(i);
        Indicators.Touch(c);
    }
    internal static void Leave(object m, string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId) || !channels.TryGetValue(channelId, out var c)) return;
        if (m is QuantumOutputMachine o) c.Senders.Remove(o);
        if (m is QuantumInputMachine i) c.Receivers.Remove(i);
        Indicators.Touch(c);
    }
    internal static Channel ChannelOf(string id) => !string.IsNullOrWhiteSpace(id) && channels.TryGetValue(id, out var c) ? c : null;

    // ---------------------------------------------------------------- moving capsules
    // A receiver that refused in this hand-off pass is skipped for the rest of it (belts don't move during a pass), and
    // once all have refused the channel refuses at once: the same answers as asking every receiver every time.
    internal static QuantumTransferResult TryTransmit(QuantumOutputMachine source, PendingCapsule capsule)
    {
        if (!Moon.IsAuthority) return QuantumTransferResult.NoMatchingAdapter;
        var c = ChannelOf(source.ChannelId);
        if (c == null || c.Receivers.Count == 0) return QuantumTransferResult.NoMatchingAdapter;
        bool marks = FrameworkPatches.InPass; int pass = FrameworkPatches.Pass;
        if (marks && c.FullPass == pass) return QuantumTransferResult.DestinationUnavailable;
        int n = c.Receivers.Count; if (c.Cursor >= n) c.Cursor = 0;
        for (int k = 0; k < n; k++)
        {
            int idx = (c.Cursor + k) % n; var r = c.Receivers[idx];
            if (marks && r.FullPass == pass) continue;
            if (r.TryReceive(capsule)) { c.Cursor = (idx + 1) % n; return QuantumTransferResult.Sent; }
            if (marks) r.FullPass = pass;
        }
        if (marks) c.FullPass = pass;
        return QuantumTransferResult.DestinationUnavailable;
    }

    // without asking any receiver: what TryTransmit would answer now, if that is already known
    internal static QuantumTransferResult? KnownRefusal(string channelId)
    {
        var c = ChannelOf(channelId);
        if (c == null || c.Receivers.Count == 0) return QuantumTransferResult.NoMatchingAdapter;
        if (FrameworkPatches.InPass && c.FullPass == FrameworkPatches.Pass) return QuantumTransferResult.DestinationUnavailable;
        return null;
    }

    // capsules a sender saved as pending (a crash or a save mid-transfer): delivered as receivers free up
    internal static void DeliverPending()
    {
        if (WithPending.Count == 0 || !Moon.IsAuthority) return;
        for (int k = WithPending.Count - 1; k >= 0; k--)
        {
            var o = WithPending[k]; var c = ChannelOf(o.ChannelId);
            if (c == null) continue;
            foreach (var r in c.Receivers) if (o.TryDeliverTo(r)) break;
            if (!o.HasPending) WithPending.RemoveAt(k);
        }
    }

    // ---------------------------------------------------------------- channels (same behaviour and save format)
    static Channel Ensure(string channelId, string preferredName = null)
    {
        EnsureLoaded();
        if (channels.TryGetValue(channelId, out var c)) return c;
        string name = string.IsNullOrWhiteSpace(preferredName) || IsLegacyDefault(preferredName) ? "New Channel" : preferredName;
        var wd = Moon.WorldData(WorldKey);
        if (wd != null && wd.TryGetValue("name." + channelId, out var saved) && !string.IsNullOrWhiteSpace(saved)) name = IsLegacyDefault(saved) ? "New Channel" : saved;
        c = new Channel(channelId, name); channels[channelId] = c;
        if (wd != null && !wd.ContainsKey("name." + channelId)) wd["name." + channelId] = name;
        if (ReferenceEquals(wd, loadedWorld) && wd != null) LoadPlacement(c);
        return c;
    }

    static string CreateChannelId()
    {
        int n = 1; string id;
        do id = $"channel-{n++}"; while (channels.ContainsKey(id));
        Ensure(id); return id;
    }

    public static bool CreateChannel(out string channelId)
    {
        channelId = string.Empty;
        if (!Moon.IsAuthority) return false;
        channelId = CreateChannelId(); QuantumNetwork.MarkAutoGroupCandidate(channelId); return true;
    }

    public static bool CreateAndAssign(int endpointId, out string channelId) { channelId = CreateChannelId(); return AssignEndpoint(endpointId, channelId); }

    public static bool AssignEndpoint(int endpointId, string channelId)
    {
        if (!Moon.IsAuthority || string.IsNullOrWhiteSpace(channelId)) return false;
        Ensure(channelId);
        Diagnostic($"adapter {endpointId} assigned to '{channelId}'");
        if (senders.TryGetValue(endpointId, out var o)) { o.SetChannel(channelId); Broadcast(endpointId, channelId); return true; }
        if (receivers.TryGetValue(endpointId, out var i)) { i.SetChannel(channelId); Broadcast(endpointId, channelId); return true; }
        return false;
    }

    public static bool AssignNextChannel(int endpointId)
    {
        if (!Moon.IsAuthority) return false;
        if (channels.Count == 0) return CreateAndAssign(endpointId, out _);
        var list = new List<Channel>(channels.Values); list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        string cur = ChannelFor(endpointId); int at = list.FindIndex(c => c.Id == cur);
        return AssignEndpoint(endpointId, list[(at + 1 + list.Count) % list.Count].Id);
    }

    public static bool UnassignEndpoint(int endpointId)
    {
        if (!Moon.IsAuthority) return false;
        if (senders.TryGetValue(endpointId, out var o)) { o.SetChannel(string.Empty); Broadcast(endpointId, string.Empty); return true; }
        if (receivers.TryGetValue(endpointId, out var i)) { i.SetChannel(string.Empty); Broadcast(endpointId, string.Empty); return true; }
        return false;
    }

    public static bool RenameChannel(string channelId, string name)
    {
        if (!Moon.IsAuthority || string.IsNullOrWhiteSpace(channelId) || !channels.TryGetValue(channelId, out var c)) return false;
        name = Clean(name); if (string.IsNullOrWhiteSpace(name)) return false;
        if (c.Group != null) { Place(c, c.Group, name); SendGroups(); return true; }       // inside a group: the short name
        c.Name = name; c.Short = name;
        var wd = Moon.WorldData(WorldKey); if (wd != null) wd["name." + channelId] = name;
        if (Crew.InCrew) Crew.Send(RenameMsg, Encoding.UTF8.GetBytes(channelId + "\t" + name));
        AutoGroupIfOn();                                                                    // ticked: file it by its new name
        return true;
    }

    public static bool DeleteChannel(string channelId)
    {
        if (!Moon.IsAuthority || string.IsNullOrWhiteSpace(channelId) || !channels.TryGetValue(channelId, out var c)) return false;
        foreach (var o in c.Senders.ToArray()) { o.SetChannel(string.Empty); Broadcast(o.Id, string.Empty); }
        foreach (var i in c.Receivers.ToArray()) { i.SetChannel(string.Empty); Broadcast(i.Id, string.Empty); }
        foreach (var k in new List<int>(pendingRemote.Keys)) if (pendingRemote[k] == channelId) pendingRemote.Remove(k);
        channels.Remove(channelId);
        Moon.WorldData(WorldKey)?.Remove("name." + channelId);
        var gd = GroupData(); if (gd != null) { gd.Remove("in." + channelId); gd.Remove("short." + channelId); }
        QuantumNetwork.ClearAutoGroupCandidate(channelId);
        if (Crew.InCrew) Crew.Send(DeleteMsg, Encoding.UTF8.GetBytes(channelId));
        if (c.Group != null) SendGroups();
        return true;
    }

    public static string ChannelFor(int endpointId)
    {
        if (senders.TryGetValue(endpointId, out var o)) return o.ChannelId;
        if (receivers.TryGetValue(endpointId, out var i)) return i.ChannelId;
        return string.Empty;
    }

    public static string ChannelName(string channelId) => ChannelOf(channelId)?.Name ?? "Unassigned";

    public static string ChannelLabel(string channelId)
    {
        var c = ChannelOf(channelId); if (c == null) return "None";
        if (c.Name.StartsWith("Quantum Channel ", StringComparison.OrdinalIgnoreCase)) return "Ch " + c.Name.Substring(16);
        if (c.Name.StartsWith("Channel ", StringComparison.OrdinalIgnoreCase)) return "Ch " + c.Name.Substring(8);
        return c.Name;
    }

    public static IReadOnlyList<ChannelSummary> Summaries()
    {
        EnsureLoaded();
        var list = new List<ChannelSummary>(channels.Count);
        foreach (var c in channels.Values) list.Add(new ChannelSummary(c.Id, c.Name, c.Senders.Count, c.Receivers.Count));
        list.Sort((a, b) =>
        {
            int na = string.Equals(a.Name, "New Channel", StringComparison.OrdinalIgnoreCase) ? 0 : 1, nb = string.Equals(b.Name, "New Channel", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            return na != nb ? na.CompareTo(nb) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    public static string FormatStatus()
    {
        int pending = 0; foreach (var o in senders.Values) pending += o.PendingCount;
        return $"Quantum networks: {channels.Count}, {senders.Count} output(s), {receivers.Count} input(s), {pending} pending";
    }

    public static IReadOnlyList<QuantumEndpointView> EndpointViews()
    {
        var list = new List<QuantumEndpointView>(senders.Count + receivers.Count);
        foreach (var o in senders.Values) if (o.View != null) list.Add(new QuantumEndpointView(o.Id, true, o.View));
        foreach (var i in receivers.Values) if (i.View != null) list.Add(new QuantumEndpointView(i.Id, false, i.View));
        return list;
    }

    internal static IEnumerable<QuantumOutputMachine> Senders => senders.Values;
    internal static IEnumerable<QuantumInputMachine> Receivers => receivers.Values;

    // public, for other mods: every adapter's attached state by structure id
    public static Dictionary<long, bool> AttachedById()
    {
        var d = new Dictionary<long, bool>();
        foreach (var o in senders.Values) d[o.Id] = true;
        foreach (var i in receivers.Values) d[i.Id] = i.IsAttached;
        return d;
    }

    internal static bool HasChannelInput(string channelId) => (ChannelOf(channelId)?.Receivers.Count ?? 0) > 0;
    internal static bool HasChannelOutput(string channelId) => (ChannelOf(channelId)?.Senders.Count ?? 0) > 0;
    internal static bool HasAttachedChannelInput(string channelId)
    {
        var c = ChannelOf(channelId); if (c == null) return false;
        foreach (var r in c.Receivers) if (r.IsAttached) return true;
        return false;
    }

    // ---------------------------------------------------------------- crew messages (same format as Quantum Conveyors)
    static void Broadcast(int endpointId, string channelId)
    {
        if (!Moon.IsAuthority || !Crew.InCrew) return;
        Crew.Send(AssignMsg, Encoding.UTF8.GetBytes(endpointId.ToString(CultureInfo.InvariantCulture) + "\t" + channelId + "\t" + ChannelName(channelId)));
    }
    static void ApplyAssign(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        var p = Encoding.UTF8.GetString(data).Split('\t', 3);
        if (p.Length < 2 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return;
        string ch = p[1];
        if (!string.IsNullOrWhiteSpace(ch)) Ensure(ch, p.Length >= 3 ? p[2] : null);
        var m = Moon.MachineOf(id);
        if (m is QuantumOutputMachine o) o.SetChannel(ch);
        else if (m is QuantumInputMachine i) i.SetChannel(ch);
        else pendingRemote[id] = ch;
    }
    static void ApplyRename(byte[] data)
    {
        var p = Encoding.UTF8.GetString(data ?? Array.Empty<byte>()).Split('\t', 2);
        if (p.Length != 2 || string.IsNullOrWhiteSpace(p[0])) return;
        var c = Ensure(p[0], p[1]); c.Name = CleanLong(p[1]); if (c.Group == null) c.Short = c.Name;   // full names run past 24
    }
    static void ApplyDelete(byte[] data)
    {
        string id = Encoding.UTF8.GetString(data ?? Array.Empty<byte>());
        if (string.IsNullOrWhiteSpace(id) || !channels.TryGetValue(id, out var c)) return;
        foreach (var o in c.Senders.ToArray()) o.SetChannel(string.Empty);
        foreach (var i in c.Receivers.ToArray()) i.SetChannel(string.Empty);
        channels.Remove(id);
        foreach (var k in new List<int>(pendingRemote.Keys)) if (pendingRemote[k] == id) pendingRemote.Remove(k);
    }

    // ---------------------------------------------------------------- helpers
    internal static bool TryDecode(string text, out PendingCapsule capsule)
    {
        capsule = default; var p = text.Split('|');
        if (p.Length != 4 || !byte.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cargo)
            || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var kg)
            || !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cap)) return false;
        try { capsule = new PendingCapsule((MoonCargo)cargo, kg, cap, Encoding.UTF8.GetString(Convert.FromBase64String(p[3]))); return true; }
        catch (FormatException) { return false; }
    }
    internal static string Encode(PendingCapsule c) => string.Join('|', ((byte)c.Cargo).ToString(CultureInfo.InvariantCulture),
        c.Kilograms.ToString("R", CultureInfo.InvariantCulture), c.Capacity.ToString("R", CultureInfo.InvariantCulture), Convert.ToBase64String(Encoding.UTF8.GetBytes(c.Owner ?? string.Empty)));
    static bool IsLegacyDefault(string name)
    {
        if (name.StartsWith("Quantum Channel ", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.Substring(16), out _)) return true;
        return name.StartsWith("Channel ", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.Substring(8), out _);
    }
    static string CleanLong(string name) { name = (name ?? string.Empty).Trim(); return name.Length > 160 ? name.Substring(0, 160).TrimEnd() : name; }
    static string Clean(string name) { name = (name ?? string.Empty).Trim(); return name.Length > 24 ? name.Substring(0, 24).TrimEnd() : name; }
}
