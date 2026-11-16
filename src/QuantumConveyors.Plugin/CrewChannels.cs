using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MoonCleaners;
using MoonFramework;

namespace QuantumConveyors;

// Channels in a crew game, for the crew-mates.
//
// The host keeps the channels (the save is theirs). A crew-mate used to hear only the changes made after they joined,
// so everything set up before - every channel, which adapter is on which, the groups - stayed unknown to them: their
// E menu was empty.
//
// (1) Seeing: a crew-mate asks the host for the whole state when their world loads and whenever they open the menu,
//     and the host answers with it (channels, assignments, groups, Auto Group, and whether crew changes are allowed).
// (2) Changing, as the host sets it ([Crew] Crew changes, or the button in the host's menu): Off (the default) - crew-mates
//     only see; Ask - a crew-mate's change waits for the host to allow or deny it (the host gets a notice and the
//     requests sit at the top of their E menu); Auto - it's made at once. A change is made with the same methods the
//     host's own menu uses, then everyone gets the new state. A crew-mate the game's crew limits stop from building
//     (1.0.6: hold Tab, Build) is always refused.
public static partial class QuantumNetwork
{
    const string HelloMsg = Plugin.PluginGuid + ".hello";        // crew-mate -> host: send me the whole state
    const string StateMsg = Plugin.PluginGuid + ".state";        // host -> crew-mate(s): the whole state
    const string RequestMsg = Plugin.PluginGuid + ".request";    // crew-mate -> host: a change from their menu

    internal static event Action StateArrived;                   // the menu redraws when it's open

    static void InitializeCrew()
    {
        Crew.On(HelloMsg, (from, _) => { if (Moon.IsAuthority) Crew.SendTo(from, StateMsg, State()); });
        Crew.On(StateMsg, (_, d) => ApplyState(d));
        Crew.On(RequestMsg, (from, d) => HandleRequest(from, d));
        Moon.WorldLoaded += _ => AskForState();
    }

    // a crew-mate's game: ask the host (at world load, and each time the menu opens)
    internal static void AskForState()
    {
        if (Crew.IsCrewmate) Crew.Send(HelloMsg, Array.Empty<byte>());
    }

    // ---------------------------------------------------------------- crew changes: Off, Ask, Auto (the host's setting)
    internal static string CrewChanges => Moon.IsAuthority ? HostCrewChanges : remoteCrewChanges;
    static string HostCrewChanges => Plugin.crewChanges?.Value is "Ask" or "Auto" ? Plugin.crewChanges.Value : "Off";
    internal static void NextCrewChanges()
    {
        if (!Moon.IsAuthority || Plugin.crewChanges == null) return;
        Plugin.crewChanges.Value = HostCrewChanges switch { "Off" => "Ask", "Ask" => "Auto", _ => "Off" };
        if (HostCrewChanges == "Off") pending.Clear();
        SendStateToCrew();
    }

    // ---------------------------------------------------------------- the whole state (host)
    // one line each: ch <id> <name> | at <adapter> <channel> | g <id> <parent> <name> | c <channel> <group> <short>
    // | auto <0|1> | crew <Off|Ask|Auto>
    static byte[] State()
    {
        EnsureLoaded();
        var sb = new StringBuilder();
        foreach (var c in channels.Values) sb.Append("ch\t").Append(c.Id).Append('\t').Append(c.Name).Append('\n');
        foreach (var o in senders.Values) if (!string.IsNullOrEmpty(o.ChannelId)) Line(sb, o.Id, o.ChannelId);
        foreach (var i in receivers.Values) if (!string.IsNullOrEmpty(i.ChannelId)) Line(sb, i.Id, i.ChannelId);
        foreach (var g in groups.Values) sb.Append("g\t").Append(g.Id).Append('\t').Append(g.Parent).Append('\t').Append(g.Name).Append('\n');
        foreach (var c in channels.Values)
            if (c.Group != null) sb.Append("c\t").Append(c.Id).Append('\t').Append(c.Group).Append('\t').Append(c.Short).Append('\n');
        sb.Append("auto\t").Append(AutoGroupOn ? "1" : "0").Append('\n');
        sb.Append("crew\t").Append(HostCrewChanges).Append('\n');
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    static void Line(StringBuilder sb, int adapter, string channel) =>
        sb.Append("at\t").Append(adapter.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(channel).Append('\n');

    // the host after any change: everyone gets the new state (the host's menu calls this after each change too)
    internal static void SendStateToCrew()
    {
        if (Moon.IsAuthority && Crew.InCrew) Crew.Send(StateMsg, State());
    }

    // ---------------------------------------------------------------- the whole state (crew-mate)
    static bool remoteAuto; static string remoteCrewChanges = "Off";

    // Auto Group as the menu shows it: the host's own setting, or the one the host sent
    internal static bool AutoGroupShown => Moon.IsAuthority ? AutoGroupOn : remoteAuto;

    static void ApplyState(byte[] data)
    {
        if (Moon.IsAuthority || data == null) return;
        EnsureLoaded();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var assigned = new Dictionary<int, string>();
        var groupLines = new StringBuilder();
        foreach (var line in Encoding.UTF8.GetString(data).Split('\n'))
        {
            var p = line.Split('\t');
            switch (p[0])
            {
                case "ch" when p.Length >= 3: names[p[1]] = string.Join("\t", p.Skip(2)); break;
                case "at" when p.Length >= 3 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id): assigned[id] = p[2]; break;
                case "g": case "c": groupLines.Append(line).Append('\n'); break;
                case "auto" when p.Length >= 2: remoteAuto = p[1] == "1"; break;
                case "crew" when p.Length >= 2: remoteCrewChanges = p[1]; break;
            }
        }
        // channels: the host's, by name; any other one goes (its adapters set free below)
        foreach (var kv in names) { var c = Ensure(kv.Key, kv.Value); c.Name = CleanLong(kv.Value); if (c.Group == null) c.Short = c.Name; }
        // adapters: on the host's channel, or on none
        foreach (var o in senders.Values.ToArray()) o.SetChannel(assigned.TryGetValue(o.Id, out var ch) ? ch : string.Empty);
        foreach (var i in receivers.Values.ToArray()) i.SetChannel(assigned.TryGetValue(i.Id, out var ch) ? ch : string.Empty);
        pendingRemote.Clear();
        foreach (var kv in assigned) if (!senders.ContainsKey(kv.Key) && !receivers.ContainsKey(kv.Key)) pendingRemote[kv.Key] = kv.Value;
        foreach (var id in channels.Keys.ToArray()) if (!names.ContainsKey(id)) channels.Remove(id);
        ApplyGroups(Encoding.UTF8.GetBytes(groupLines.ToString()));
        StateArrived?.Invoke();
    }

    // ---------------------------------------------------------------- a crew-mate's menu: requests to the host
    // op <tab> args. The crew-mate's menu calls this instead of the host-only methods.
    internal static void Request(string op, params string[] args)
    {
        if (!Crew.IsCrewmate || CrewChanges == "Off") return;
        Crew.Send(RequestMsg, Encoding.UTF8.GetBytes(op + "\t" + string.Join("\t", args)));
        if (CrewChanges == "Ask") Notice("Asked the host: " + Describe(op, args));
    }

    // a request waiting for the host (Ask)
    internal sealed class Pending { public ulong From; public string Who, What; public string[] Parts; }
    static readonly List<Pending> pending = new();
    internal static IReadOnlyList<Pending> PendingRequests => pending;

    static void HandleRequest(ulong from, byte[] data)
    {
        if (!Moon.IsAuthority || data == null) return;
        var p = Encoding.UTF8.GetString(data).Split('\t');
        string who = PlayerName(from), what = Describe(p[0], p.Skip(1).ToArray());
        if (HostCrewChanges == "Off" || BuildingRefused(from))
        {
            var why = HostCrewChanges == "Off" ? "crew changes are off" : "the crew limits stop them building";
            Log?.LogInfo("Channel change from " + who + " refused (" + why + "): " + what);
            Crew.SendTo(from, StateMsg, State());                     // their menu shows how things really stand
            return;
        }
        if (HostCrewChanges == "Ask")
        {
            pending.Add(new Pending { From = from, Who = who, What = what, Parts = p });
            Notice(who + " asks to " + what + ". Open a Quantum menu (E) to allow or deny it.");
            StateArrived?.Invoke();                                   // the host's menu, if open, shows it
            return;
        }
        Apply(from, p);
    }

    // the host allows (Ask) a waiting request, or denies it
    internal static void Answer(Pending r, bool allow)
    {
        if (!pending.Remove(r)) return;
        if (allow && !BuildingRefused(r.From)) Apply(r.From, r.Parts);
        else Crew.SendTo(r.From, StateMsg, State());
    }

    static void Apply(ulong from, string[] p)
    {
        string A(int i) => p.Length > i ? p[i] : string.Empty;
        int N(int i) => int.TryParse(A(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1;
        bool changed = p[0] switch
        {
            "assign" => AssignEndpoint(N(1), A(2)),
            "unassign" => UnassignEndpoint(N(1)),
            "create" => CreateChannelIn(A(1), out _),
            "rename" => RenameChannel(A(1), A(2)),
            "delete" => DeleteChannel(A(1)),
            "newgroup" => CreateGroup(A(1), A(2), out _),
            "renamegroup" => RenameGroup(A(1), A(2)),
            "deletegroup" => DeleteGroup(A(1)),
            "movechannel" => MoveChannel(A(1), A(2)),
            "movegroup" => MoveGroup(A(1), A(2)),
            "auto" => SetAutoGroup(A(1) == "1") >= 0,
            _ => false,
        };
        if (changed) Diagnostic("crew-mate " + from + ": " + string.Join(" ", p));
        SendStateToCrew();                                           // changed or not: everyone sees where things stand
    }

    // a request in words, for notices and the host's menu
    internal static string Describe(string op, string[] a)
    {
        string A(int i) => a.Length > i ? a[i] : string.Empty;
        string Ch(int i) => ChannelOf(A(i))?.Name ?? "a channel";
        string G(int i) => string.IsNullOrEmpty(A(i)) ? "the top level" : GroupName(A(i)) ?? "a group";
        return op switch
        {
            "assign" => "put an adapter on " + Ch(2),
            "unassign" => "take an adapter off its channel",
            "create" => "make a new channel",
            "rename" => "rename " + Ch(1) + " to \"" + A(2) + "\"",
            "delete" => "delete " + Ch(1),
            "newgroup" => "make a group \"" + A(2) + "\"",
            "renamegroup" => "rename the group " + G(1) + " to \"" + A(2) + "\"",
            "deletegroup" => "delete the group " + G(1),
            "movechannel" => "move " + Ch(1) + " into " + G(2),
            "movegroup" => "move the group " + G(1) + " into " + G(2),
            "auto" => "turn Auto Group " + (A(1) == "1" ? "on" : "off"),
            _ => op,
        };
    }

    static string PlayerName(ulong id)
    {
        try { var n = MoonSteamLobby.PlayerName(id); if (!string.IsNullOrEmpty(n)) return n; } catch (Exception e) { Diagnostic("name: " + e.Message); }
        return "A crew-mate";
    }

    // a line in the game's crew feed, on this machine
    static void Notice(string text)
    {
        try { MoonCrewSession.Instance?.Notify(text); } catch (Exception e) { Diagnostic("notice: " + e.Message); }
        Log?.LogInfo(text);
    }

    // the game's crew limits (Moon Sweepers 1.0.6 and later): a crew-mate without Build may not change channels either.
    // Found by name, so 1.0.5 (which has none) builds and runs the same.
    static System.Reflection.MethodInfo limitOf; static bool limitLooked;
    static bool BuildingRefused(ulong player)
    {
        if (!limitLooked)
        {
            limitLooked = true;
            var t = typeof(MoonWorld).Assembly.GetType("MoonCleaners.MoonCrewLimits");
            limitOf = t?.GetMethod("Of", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(ulong) }, null);
        }
        if (limitOf == null) return false;
        try { return (Convert.ToInt32(limitOf.Invoke(null, new object[] { player })) & 1) != 0; }   // Limit.Build = 1
        catch (Exception e) { Diagnostic("crew limits not read: " + e.Message); return false; }
    }
}
