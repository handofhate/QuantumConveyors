using System.Collections.Generic;
using UnityEngine;

namespace QuantumConveyors;

// How much each channel has moved lately: one time per capsule a receiver put on its belt, kept for Window seconds.
// Read through HandleHttp (/quantum/flow); a dead line shows as a channel moving nothing, or a receiver with no LastIn.
internal static class Flow
{
    internal const float Window = 600f;                       // 10 minutes
    static readonly Dictionary<string, Queue<float>> moved = new();

    internal static void Moved(string channelId)
    {
        if (string.IsNullOrEmpty(channelId)) return;
        if (!moved.TryGetValue(channelId, out var q)) moved[channelId] = q = new Queue<float>();
        float now = Time.unscaledTime; q.Enqueue(now);
        while (q.Count > 0 && now - q.Peek() > Window) q.Dequeue();
    }

    // capsules this channel moved in the last `seconds` (at most Window)
    internal static int Count(string channelId, float seconds)
    {
        if (!moved.TryGetValue(channelId, out var q)) return 0;
        float since = Time.unscaledTime - Mathf.Min(seconds, Window); int n = 0;
        foreach (var t in q) if (t >= since) n++;
        return n;
    }
}
