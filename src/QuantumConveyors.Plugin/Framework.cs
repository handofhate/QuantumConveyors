using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MoonFramework;

namespace QuantumConveyors;

// What a reload needs from MoonFramework's internals (MoonFramework.Internal, reached by reflection like
// FrameworkPatches): handing the adapters' machines over to the next copy, creating its machines at once, and taking
// this copy's crew-message handlers out (MoonFramework has no call to remove one).
internal static class Framework
{
    const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Type Internal(string name) => typeof(Machine).Assembly.GetType("MoonFramework.Internal." + name, true);

    // Every live machine of these buildings: saved (OnSave), stopped (OnStop) and taken off MoonFramework's live list,
    // its saved data given back (Machines.Restore) so the machine MoonFramework makes next for that adapter starts from
    // it. The input table is cleared so nothing offers a capsule to a stopped machine before the next one exists.
    internal static int HandOver(params string[] buildingIds)
    {
        var machines = Internal("Machines");
        var live = (IDictionary)machines.GetField("live", S).GetValue(null);
        var errors = (IDictionary)machines.GetField("errors", S).GetValue(null);
        var restore = machines.GetMethod("Restore", S);
        var ours = new HashSet<string>(buildingIds);
        var ids = new List<int>();
        foreach (DictionaryEntry e in live) if (e.Value is Machine m && m.Building != null && ours.Contains(m.Building.Id)) ids.Add((int)e.Key);
        foreach (var id in ids)
        {
            var m = (Machine)live[id];
            try { m.OnSave(); } catch (Exception x) { QuantumNetwork.Log?.LogWarning($"adapter {id} OnSave: {x.Message}"); }
            try { m.OnStop(); } catch (Exception x) { QuantumNetwork.Log?.LogWarning($"adapter {id} OnStop: {x.Message}"); }
            live.Remove(id); errors.Remove(m);
            restore.Invoke(null, new object[] { id, new Dictionary<string, string>(m.Data ?? new Dictionary<string, string>()) });
        }
        Internal("Intake").GetMethod("Reset", S).Invoke(null, null);
        return ids.Count;
    }

    // MoonFramework makes machines and rebuilds its input table every half second; after a reload, now
    internal static void SyncNow()
    {
        var world = Moon.World; if (world == null) return;
        Internal("Machines").GetMethod("Sync", S).Invoke(null, new object[] { world });
        Internal("Intake").GetMethod("Refresh", S).Invoke(null, new object[] { world });
    }

    internal static void RemoveCrewHandlers(Assembly ours)
    {
        var handlers = (IDictionary)Internal("CrewNet").GetField("handlers", S).GetValue(null);
        foreach (var list in handlers.Values)
            if (list is List<Action<ulong, byte[]>> l) l.RemoveAll(h => h.Method.DeclaringType?.Assembly == ours);
    }
}
