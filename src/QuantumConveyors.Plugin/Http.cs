using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;
using MoonFramework;
using UnityEngine;

namespace QuantumConveyors;

// Answers to HTTP requests, for mods that serve HTTP and pass requests on to plugins (Plugin/Conveyors.HandleHttp).
// Without such a mod nothing calls this. Reads are GET, changes POST; a change only works on the host (as in the menu).
// Each thing has its own list: channels, adapters, groups; GET /quantum is the summary.
internal static class Http
{
    // one line per request, for a host's help list ("METHOD /path?params | what it does")
    internal static readonly string[] Help =
    {
        "GET /quantum | Summary: channels, groups, inputs, outputs, capsules pending, auto group, the status line",
        "GET /quantum/channels | Every channel: id, name, short name, group (id and path), outputs, inputs",
        "POST /quantum/channels/create?name=&group= | A new channel; without group= a build name like Iron-Line-2 is filed into its group (host only)",
        "POST /quantum/channels/modify?channel= | name= renames; group= moves it into a group (group= empty: out of every group) (host only)",
        "POST /quantum/channels/remove?channel= | Delete a channel (host only)",
        "GET /quantum/adapters | Every adapter: id, kind, role, position, channel (id and name), attached to a belt",
        "POST /quantum/adapters/modify?id=&channel= | Put an adapter on a channel; channel= empty takes it off (host only)",
        "GET /quantum/groups | Every group: id, name, parent, path, channels and adapters in it",
        "POST /quantum/groups/create?name=&parent= | A new group (parent= empty or left out: top level) (host only)",
        "POST /quantum/groups/modify?group= | name= renames; parent= moves it (empty: top level) (host only)",
        "POST /quantum/groups/remove?group= | Delete a group; its channels and groups move up a level (host only)",
        "POST /quantum/groups/convert | File every ungrouped build-named channel into groups (host only)",
        "GET /quantum/flow?minutes=5 | Per channel: capsules moved in the last minutes (up to 10), and the receivers that placed none",
    };

    internal static string Handle(string method, string path, NameValueCollection q)
    {
        if (path != "/quantum" && !path.StartsWith("/quantum/", StringComparison.Ordinal)) return null;
        bool post = method == "POST";
        try
        {
            switch (path)
            {
                case "/quantum": return post ? Fail("use GET") : Summary();
                case "/quantum/channels": return post ? Fail("use GET") : Channels();
                case "/quantum/adapters": return post ? Fail("use GET") : Adapters();
                case "/quantum/groups": return post ? Fail("use GET") : Groups();
                case "/quantum/flow": return post ? Fail("use GET") : FlowNow(float.TryParse(q["minutes"], NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 5f);
                case "/quantum/channels/create": return post ? CreateChannel(q["name"], q["group"]) : Fail("use POST");
                case "/quantum/channels/modify": return post ? ModifyChannel(q) : Fail("use POST");
                case "/quantum/channels/remove": return post ? Result(QuantumNetwork.DeleteChannel(q["channel"] ?? "")) : Fail("use POST");
                case "/quantum/adapters/modify": return post ? ModifyAdapter(q) : Fail("use POST");
                case "/quantum/groups/create": return post ? CreateGroup(q["name"], q["parent"]) : Fail("use POST");
                case "/quantum/groups/modify": return post ? ModifyGroup(q) : Fail("use POST");
                case "/quantum/groups/remove": return post ? Result(QuantumNetwork.DeleteGroup(q["group"] ?? "")) : Fail("use POST");
                case "/quantum/groups/convert": return post ? "{\"ok\":true,\"filed\":" + QuantumNetwork.ConvertNames() + "}" : Fail("use POST");
            }
        }
        catch (Exception e) { return Fail(e.GetType().Name + ": " + e.Message); }
        return "{\"error\":" + S("unknown path " + path) + ",\"paths\":[" + string.Join(",", Help.Select(h => S(h.Split('|')[0].Trim()))) + "]}";
    }

    static string Summary()
    {
        int outputs = QuantumNetwork.Senders.Count(), inputs = QuantumNetwork.Receivers.Count(), pending = QuantumNetwork.Senders.Sum(o => o.PendingCount);
        return "{\"ok\":true,\"channels\":" + QuantumNetwork.Summaries().Count + ",\"groups\":" + QuantumNetwork.AllGroups.Count()
            + ",\"outputs\":" + outputs + ",\"inputs\":" + inputs + ",\"pending\":" + pending
            + ",\"autoGroup\":" + (QuantumNetwork.AutoGroupOn ? "true" : "false") + ",\"status\":" + S(QuantumNetwork.FormatStatus()) + "}";
    }

    static string Channels()
    {
        var sb = new StringBuilder("{\"ok\":true,\"channels\":[");
        bool first = true;
        foreach (var c in QuantumNetwork.Summaries())
        {
            if (!first) sb.Append(','); first = false;
            var gid = QuantumNetwork.GroupOf(c.Id);
            sb.Append("{\"id\":").Append(S(c.Id)).Append(",\"name\":").Append(S(c.Name)).Append(",\"short\":").Append(S(QuantumNetwork.ShortName(c.Id)))
              .Append(",\"group\":").Append(S(gid)).Append(",\"groupPath\":").Append(S(QuantumNetwork.GroupPath(gid)))
              .Append(",\"outputs\":").Append(c.Outputs).Append(",\"inputs\":").Append(c.Inputs).Append('}');
        }
        return sb.Append("]}").ToString();
    }

    static string Adapters()
    {
        var sb = new StringBuilder("{\"ok\":true,\"adapters\":[");
        var depots = Moon.World?.depots; bool first = true;
        void Adapter(int id, bool sender, string channel, bool attached)
        {
            if (!first) sb.Append(','); first = false;
            int kind = -1; string pos = "null";
            if (depots != null && depots.TryGetValue(id, out var d)) { kind = d.kind; pos = "[" + F(d.x) + "," + F(d.y) + "," + F(d.z) + "]"; }
            sb.Append("{\"id\":").Append(id).Append(",\"kind\":").Append(kind).Append(",\"role\":").Append(S(sender ? "out (station end)" : "in (machine end)"))
              .Append(",\"pos\":").Append(pos).Append(",\"channel\":").Append(S(channel))
              .Append(",\"channelName\":").Append(S(string.IsNullOrEmpty(channel) ? null : QuantumNetwork.ChannelName(channel)))
              .Append(",\"attached\":").Append(attached ? "true" : "false").Append('}');
        }
        foreach (var o in QuantumNetwork.Senders) Adapter(o.Id, true, o.ChannelId, true);
        foreach (var i in QuantumNetwork.Receivers) Adapter(i.Id, false, i.ChannelId, i.IsAttached);
        return sb.Append("]}").ToString();
    }

    static string Groups()
    {
        var counts = QuantumNetwork.GroupCounts();
        var sb = new StringBuilder("{\"ok\":true,\"groups\":[");
        bool first = true;
        foreach (var g in QuantumNetwork.AllGroups.OrderBy(g => QuantumNetwork.GroupPath(g.Id), StringComparer.OrdinalIgnoreCase))
        {
            if (!first) sb.Append(','); first = false;
            counts.TryGetValue(g.Id, out var n);
            sb.Append("{\"id\":").Append(S(g.Id)).Append(",\"name\":").Append(S(g.Name)).Append(",\"parent\":").Append(S(g.Parent.Length == 0 ? null : g.Parent))
              .Append(",\"path\":").Append(S(QuantumNetwork.GroupPath(g.Id))).Append(",\"channels\":").Append(n.channels).Append(",\"adapters\":").Append(n.adapters).Append('}');
        }
        return sb.Append("]}").ToString();
    }

    static string FlowNow(float minutes)
    {
        float secs = Mathf.Clamp(minutes, 0.1f, Flow.Window / 60f) * 60f, since = Time.unscaledTime - secs;
        var sb = new StringBuilder("{\"ok\":true,\"minutes\":").Append(F(secs / 60f)).Append(",\"channels\":[");
        bool first = true;
        foreach (var c in QuantumNetwork.Summaries())
        {
            var idle = QuantumNetwork.Receivers.Where(r => r.ChannelId == c.Id && r.LastIn < since).Select(r => r.Id + (r.IsAttached ? "" : " (no belt)")).ToList();
            if (!first) sb.Append(','); first = false;
            sb.Append("{\"id\":").Append(S(c.Id)).Append(",\"name\":").Append(S(c.Name)).Append(",\"moved\":").Append(Flow.Count(c.Id, secs))
              .Append(",\"outputs\":").Append(c.Outputs).Append(",\"inputs\":").Append(c.Inputs)
              .Append(",\"idleReceivers\":[").Append(string.Join(",", idle.Select(S))).Append("]}");
        }
        return sb.Append("]}").ToString();
    }

    static string CreateChannel(string name, string group)
    {
        string id;
        if (!string.IsNullOrEmpty(group))
        {
            if (!QuantumNetwork.CreateChannelIn(group, out id)) return Fail("no group " + group + ", or not the host");
            if (!string.IsNullOrEmpty(name)) QuantumNetwork.RenameChannel(id, name);
        }
        else
        {
            if (!QuantumNetwork.CreateChannel(out id)) return Fail("only the host can create channels");
            if (!string.IsNullOrEmpty(name)) QuantumNetwork.FileByCode(id, name);
        }
        return "{\"ok\":true,\"channel\":" + S(id) + ",\"name\":" + S(QuantumNetwork.ChannelName(id)) + ",\"group\":" + S(QuantumNetwork.GroupOf(id)) + "}";
    }

    static string ModifyChannel(NameValueCollection q)
    {
        string ch = q["channel"]; if (string.IsNullOrEmpty(ch)) return Fail("channel= is required");
        bool any = false, ok = true;
        if (q["name"] != null) { ok &= QuantumNetwork.RenameChannel(ch, q["name"]); any = true; }
        if (q["group"] != null) { ok &= QuantumNetwork.MoveChannel(ch, q["group"]); any = true; }
        return any ? Result(ok) : Fail("give name= to rename, or group= to move it");
    }

    static string ModifyAdapter(NameValueCollection q)
    {
        if (!int.TryParse(q["id"], out var id)) return Fail("id= is required (an adapter's structure id)");
        if (q["channel"] == null) return Fail("channel= is required (empty takes the adapter off its channel)");
        if (q["channel"].Length == 0) return Result(QuantumNetwork.UnassignEndpoint(id));
        return Result(QuantumNetwork.AssignEndpoint(id, q["channel"]), ",\"channel\":" + S(QuantumNetwork.ChannelFor(id)));
    }

    static string CreateGroup(string name, string parent)
    {
        if (string.IsNullOrEmpty(name)) return Fail("name= is required");
        if (!QuantumNetwork.CreateGroup(parent ?? string.Empty, name, out var gid)) return Fail("couldn't create it (no such parent, or not the host)");
        return "{\"ok\":true,\"group\":" + S(gid) + ",\"path\":" + S(QuantumNetwork.GroupPath(gid)) + "}";
    }

    static string ModifyGroup(NameValueCollection q)
    {
        string gid = q["group"]; if (string.IsNullOrEmpty(gid)) return Fail("group= is required");
        bool any = false, ok = true;
        if (q["name"] != null) { ok &= QuantumNetwork.RenameGroup(gid, q["name"]); any = true; }
        if (q["parent"] != null) { ok &= QuantumNetwork.MoveGroup(gid, q["parent"]); any = true; }
        return any ? Result(ok) : Fail("give name= to rename, or parent= to move it");
    }

    static string Result(bool ok, string extra = "") => "{\"ok\":" + (ok ? "true" : "false") + ",\"reason\":null" + extra + "}";
    static string Fail(string why) => "{\"ok\":false,\"reason\":" + S(why) + "}";
    static string F(double v) => double.IsFinite(v) ? Math.Round(v, 4).ToString(CultureInfo.InvariantCulture) : "null";
    static string S(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
            else if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
            else sb.Append(ch);
        }
        return sb.Append('"').ToString();
    }
}
