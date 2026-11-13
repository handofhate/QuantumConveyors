using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;
using MoonFramework;
using UnityEngine;

namespace QuantumConveyors;

// Answers to HTTP requests, for mods that serve HTTP and pass requests on to plugins (Plugin.HandleHttp). Without
// such a mod nothing calls this. Reads are GET, changes POST; a change only works on the host (as in the menu).
//   GET  /quantum/channels                every channel (id, name, group, outputs, inputs), every adapter (id, kind,
//                                         role, pos, channel, channelName, attached) and the status line
//   POST /quantum/channels/create?name=   a new channel (a build name like "Iron-Line-2" is filed into its group)
//   POST /quantum/channels/modify         adapter=&channel= puts an adapter on a channel; adapter= alone takes it off;
//                                         channel=&name= renames
//   POST /quantum/channels/remove?channel=
//   POST /quantum/convert                 file every ungrouped build-named channel into groups
//   GET  /quantum/flow[?minutes=5]        per channel: capsules moved in the last minutes, and receivers that placed none
internal static class Http
{
    // one line per request, for a host's help list ("METHOD /path?params | what it does")
    internal static readonly string[] Help =
    {
        "GET /quantum/channels | Every channel (id, name, group, outputs, inputs), every adapter (id, kind, role, pos, channel, attached), the status line",
        "POST /quantum/channels/create?name= | A new channel; a build name like Iron-Line-2 is filed into its group (host only)",
        "POST /quantum/channels/modify | adapter=&channel= puts an adapter on a channel; adapter= alone takes it off; channel=&name= renames (host only)",
        "POST /quantum/channels/remove?channel= | Delete a channel (host only)",
        "POST /quantum/convert | File every ungrouped build-named channel into groups (host only)",
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
                case "/quantum/channels": return post ? Fail("use GET") : Channels();
                case "/quantum/flow": return post ? Fail("use GET") : FlowNow(float.TryParse(q["minutes"], NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 5f);
                case "/quantum/channels/create": return post ? Create(q["name"]) : Fail("use POST");
                case "/quantum/channels/modify": return post ? Modify(q) : Fail("use POST");
                case "/quantum/channels/remove": return post ? Result(QuantumNetwork.DeleteChannel(q["channel"] ?? "")) : Fail("use POST");
                case "/quantum/convert": return post ? "{\"ok\":true,\"filed\":" + QuantumNetwork.ConvertNames() + "}" : Fail("use POST");
            }
        }
        catch (Exception e) { return Fail(e.GetType().Name + ": " + e.Message); }
        return "{\"error\":" + S("unknown path " + path) + ",\"paths\":[\"GET /quantum/channels\",\"GET /quantum/flow\",\"POST /quantum/channels/create\","
            + "\"POST /quantum/channels/modify\",\"POST /quantum/channels/remove\",\"POST /quantum/convert\"]}";
    }

    static string Channels()
    {
        var sb = new StringBuilder("{\"ok\":true,\"channels\":[");
        bool first = true;
        foreach (var c in QuantumNetwork.Summaries())
        {
            if (!first) sb.Append(','); first = false;
            sb.Append("{\"id\":").Append(S(c.Id)).Append(",\"name\":").Append(S(c.Name)).Append(",\"group\":").Append(S(QuantumNetwork.GroupPath(QuantumNetwork.GroupOf(c.Id))))
              .Append(",\"outputs\":").Append(c.Outputs).Append(",\"inputs\":").Append(c.Inputs).Append('}');
        }
        sb.Append("],\"adapters\":[");
        var depots = Moon.World?.depots; first = true;
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
        return sb.Append("],\"status\":").Append(S(QuantumNetwork.FormatStatus())).Append('}').ToString();
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

    static string Create(string name)
    {
        if (!QuantumNetwork.CreateChannel(out var id)) return Fail("only the host can create channels");
        if (!string.IsNullOrEmpty(name)) QuantumNetwork.FileByCode(id, name);
        return "{\"ok\":true,\"channel\":" + S(id) + ",\"name\":" + S(QuantumNetwork.ChannelName(id)) + "}";
    }

    static string Modify(NameValueCollection q)
    {
        string ch = q["channel"];
        bool hasAdapter = int.TryParse(q["adapter"], out var adapter);
        if (hasAdapter && !string.IsNullOrEmpty(ch))
            return Result(QuantumNetwork.AssignEndpoint(adapter, ch), ",\"channel\":" + S(QuantumNetwork.ChannelFor(adapter)));
        if (hasAdapter) return Result(QuantumNetwork.UnassignEndpoint(adapter));
        if (!string.IsNullOrEmpty(ch) && q["name"] != null) return Result(QuantumNetwork.RenameChannel(ch, q["name"]));
        return Fail("give adapter= (with channel= to assign, without to unassign), or channel= and name= to rename");
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
