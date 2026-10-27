using System.Globalization;
using BepInEx.Logging;
using MoonCleaners;
using MoonFramework;

namespace QuantumConveyors;

internal static class QuantumNetwork
{
    private static readonly List<QuantumOutputMachine> Outputs = new();
    private static readonly List<QuantumInputMachine> Inputs = new();
    private static readonly Dictionary<string, ChannelState> Channels = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, string> PendingRemoteAssignments = new();
    private static readonly Dictionary<int, UnityEngine.GameObject> BoundViews = new();
    private static ManualLogSource log;
    private static int fallbackChannelNumber;

    private const string ChannelMessage = Plugin.PluginGuid + ".assignment";

    public static void Initialize(ManualLogSource source)
    {
        log = source;
        Crew.On(ChannelMessage, (_, data) => ApplyNetworkMessage(data));
    }

    public static void Add(QuantumOutputMachine machine)
    {
        if (!Outputs.Contains(machine)) Outputs.Add(machine);
        if (!string.IsNullOrWhiteSpace(machine.ChannelId)) EnsureChannel(machine.ChannelId);
        if (PendingRemoteAssignments.Remove(machine.Id, out var remoteChannel)) machine.SetChannel(remoteChannel);
    }

    public static void Add(QuantumInputMachine machine)
    {
        if (!Inputs.Contains(machine)) Inputs.Add(machine);
        if (!string.IsNullOrWhiteSpace(machine.ChannelId)) EnsureChannel(machine.ChannelId);
        if (PendingRemoteAssignments.Remove(machine.Id, out var remoteChannel)) machine.SetChannel(remoteChannel);
    }

    public static void Remove(QuantumOutputMachine machine)
    {
        Outputs.Remove(machine);
        BoundViews.Remove(machine.Id);
    }

    public static void Remove(QuantumInputMachine machine)
    {
        Inputs.Remove(machine);
        BoundViews.Remove(machine.Id);
    }

    public static void BindView(int endpointId, UnityEngine.GameObject view)
    {
        if (view != null) BoundViews[endpointId] = view;
    }

    public static void BindUnidentifiedView(UnityEngine.GameObject view, bool isOutput)
    {
        if (view == null) return;
        var position = view.transform.position;
        if (isOutput)
        {
            var match = Outputs
                .Where(machine => !BoundViews.ContainsKey(machine.Id))
                .OrderBy(machine => DistanceSquared(machine.Record, position))
                .FirstOrDefault();
            if (match != null) BoundViews[match.Id] = view;
        }
        else
        {
            var match = Inputs
                .Where(machine => !BoundViews.ContainsKey(machine.Id))
                .OrderBy(machine => DistanceSquared(machine.Record, position))
                .FirstOrDefault();
            if (match != null) BoundViews[match.Id] = view;
        }
    }

    public static void ConfigureInteraction(UnityEngine.GameObject view, QuantumEndpointInteraction interaction)
    {
        var output = Outputs.FirstOrDefault(machine => machine.View == view);
        if (output != null)
        {
            interaction.Configure(output.Id, true);
            return;
        }

        var input = Inputs.FirstOrDefault(machine => machine.View == view);
        if (input != null) interaction.Configure(input.Id, false);
    }

    public static IReadOnlyList<QuantumEndpointView> EndpointViews()
    {
        var views = new List<QuantumEndpointView>(Outputs.Count + Inputs.Count);
        views.AddRange(Outputs.Select(machine => new QuantumEndpointView(machine.Id, true, ViewFor(machine.Id, machine.View)))
            .Where(endpoint => endpoint.View != null));
        views.AddRange(Inputs.Select(machine => new QuantumEndpointView(machine.Id, false, ViewFor(machine.Id, machine.View)))
            .Where(endpoint => endpoint.View != null));
        return views;
    }

    private static UnityEngine.GameObject ViewFor(int endpointId, UnityEngine.GameObject fallback)
    {
        if (BoundViews.TryGetValue(endpointId, out var view) && view != null) return view;
        return fallback;
    }

    private static float DistanceSquared(MoonCleaners.MoonDepotSnapshot record, UnityEngine.Vector3 position)
    {
        var dx = record.x - position.x;
        var dy = record.y - position.y;
        var dz = record.z - position.z;
        return dx * dx + dy * dy + dz * dz;
    }

    public static void Tick()
    {
        if (!Moon.IsAuthority || Outputs.Count == 0 || Inputs.Count == 0) return;

        foreach (var channel in Channels.Values)
        {
            var outputs = Outputs.Where(output => output.ChannelId == channel.Id && output.IsAttached && output.HasPending).ToList();
            var inputs = Inputs.Where(input => input.ChannelId == channel.Id && input.IsAttached).ToList();
            if (outputs.Count == 0 || inputs.Count == 0) continue;

            channel.OutputCursor = Normalize(channel.OutputCursor, outputs.Count);
            channel.InputCursor = Normalize(channel.InputCursor, inputs.Count);

            var delivered = false;
            for (var outputOffset = 0; outputOffset < outputs.Count && !delivered; outputOffset++)
            {
                var output = outputs[(channel.OutputCursor + outputOffset) % outputs.Count];
                for (var inputOffset = 0; inputOffset < inputs.Count; inputOffset++)
                {
                    var inputIndex = (channel.InputCursor + inputOffset) % inputs.Count;
                    if (!output.TryDeliverTo(inputs[inputIndex])) continue;

                    channel.OutputCursor = (channel.OutputCursor + outputOffset + 1) % outputs.Count;
                    channel.InputCursor = (inputIndex + 1) % inputs.Count;
                    delivered = true;
                    break;
                }
            }
        }
    }

    public static bool TryTransmit(QuantumOutputMachine source, PendingCapsule capsule)
    {
        if (!Moon.IsAuthority || !source.IsAttached || string.IsNullOrWhiteSpace(source.ChannelId)) return false;
        if (!Channels.TryGetValue(source.ChannelId, out var channel)) return false;

        var inputs = Inputs
            .Where(input => input.ChannelId == source.ChannelId && input.IsAttached)
            .ToList();
        if (inputs.Count == 0) return false;

        channel.InputCursor = Normalize(channel.InputCursor, inputs.Count);
        for (var inputOffset = 0; inputOffset < inputs.Count; inputOffset++)
        {
            var inputIndex = (channel.InputCursor + inputOffset) % inputs.Count;
            if (!inputs[inputIndex].TryReceive(capsule)) continue;

            channel.InputCursor = (inputIndex + 1) % inputs.Count;
            return true;
        }

        return false;
    }

    public static string FormatStatus()
    {
        var pending = Outputs.Sum(output => output.PendingCount);
        return $"Quantum networks: {Channels.Count}, {Outputs.Count} output(s), {Inputs.Count} input(s), {pending} pending";
    }

    public static IReadOnlyList<ChannelSummary> Summaries()
    {
        return Channels.Values
            .OrderBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
            .Select(channel => new ChannelSummary(
                channel.Id,
                channel.Name,
                Outputs.Count(output => output.ChannelId == channel.Id),
                Inputs.Count(input => input.ChannelId == channel.Id)))
            .ToList();
    }

    public static string ChannelFor(int endpointId)
    {
        var output = Outputs.FirstOrDefault(machine => machine.Id == endpointId);
        if (output != null) return output.ChannelId;
        var input = Inputs.FirstOrDefault(machine => machine.Id == endpointId);
        return input?.ChannelId ?? string.Empty;
    }

    public static bool AssignEndpoint(int endpointId, string channelId)
    {
        if (!Moon.IsAuthority || string.IsNullOrWhiteSpace(channelId)) return false;
        EnsureChannel(channelId);

        var output = Outputs.FirstOrDefault(machine => machine.Id == endpointId);
        if (output != null)
        {
            output.SetChannel(channelId);
            BroadcastAssignment(endpointId, channelId);
            return true;
        }

        var input = Inputs.FirstOrDefault(machine => machine.Id == endpointId);
        if (input == null) return false;
        input.SetChannel(channelId);
        BroadcastAssignment(endpointId, channelId);
        return true;
    }

    public static bool CreateAndAssign(int endpointId, out string channelId)
    {
        channelId = CreateChannel();
        return AssignEndpoint(endpointId, channelId);
    }

    public static bool UnassignEndpoint(int endpointId)
    {
        if (!Moon.IsAuthority) return false;

        var output = Outputs.FirstOrDefault(machine => machine.Id == endpointId);
        if (output != null)
        {
            output.SetChannel(string.Empty);
            BroadcastAssignment(endpointId, string.Empty);
            return true;
        }

        var input = Inputs.FirstOrDefault(machine => machine.Id == endpointId);
        if (input == null) return false;
        input.SetChannel(string.Empty);
        BroadcastAssignment(endpointId, string.Empty);
        return true;
    }

    public static string ChannelName(string channelId)
    {
        return !string.IsNullOrWhiteSpace(channelId) && Channels.TryGetValue(channelId, out var channel)
            ? channel.Name
            : "Unassigned";
    }

    public static void Report(Exception exception)
    {
        if (log != null) log.LogError(exception);
    }

    internal static bool TryDecode(string text, out PendingCapsule capsule)
    {
        capsule = default;
        var fields = text.Split('|');
        if (fields.Length != 4 || !byte.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cargo) ||
            !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var kilograms) ||
            !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var capacity)) return false;

        try
        {
            var owner = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(fields[3]));
            capsule = new PendingCapsule((MoonCargo)cargo, kilograms, capacity, owner);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string CreateChannel()
    {
        var world = Moon.WorldData(Plugin.PluginGuid);
        var next = 1;
        if (world != null && world.TryGetValue("next-channel", out var saved) && int.TryParse(saved, out var parsed))
            next = Math.Max(1, parsed);

        string id;
        do id = $"channel-{next++}";
        while (Channels.ContainsKey(id));

        if (world != null) world["next-channel"] = next.ToString(CultureInfo.InvariantCulture);
        EnsureChannel(id);
        return id;
    }

    private static void EnsureChannel(string channelId, string preferredName = null)
    {
        if (string.IsNullOrWhiteSpace(channelId) || Channels.ContainsKey(channelId)) return;

        var name = string.IsNullOrWhiteSpace(preferredName) ? $"Quantum Channel {++fallbackChannelNumber}" : preferredName;
        var world = Moon.WorldData(Plugin.PluginGuid);
        if (world != null && world.TryGetValue($"name.{channelId}", out var savedName) && !string.IsNullOrWhiteSpace(savedName))
            name = savedName;

        Channels[channelId] = new ChannelState(channelId, name);
        if (world != null && !world.ContainsKey($"name.{channelId}")) world[$"name.{channelId}"] = name;
    }

    private static void BroadcastAssignment(int endpointId, string channelId)
    {
        if (!Moon.IsAuthority || !Crew.InCrew) return;
        var payload = string.Join('\t', endpointId.ToString(CultureInfo.InvariantCulture), channelId, ChannelName(channelId));
        Crew.Send(ChannelMessage, System.Text.Encoding.UTF8.GetBytes(payload));
    }

    private static void ApplyNetworkMessage(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        var fields = System.Text.Encoding.UTF8.GetString(data).Split('\t', 3);
        if (fields.Length < 2 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var endpointId)) return;
        var channelId = fields[1];
        var machine = Moon.MachineOf(endpointId);
        if (string.IsNullOrWhiteSpace(channelId))
        {
            if (machine is QuantumOutputMachine unassignedOutput) unassignedOutput.SetChannel(string.Empty);
            else if (machine is QuantumInputMachine unassignedInput) unassignedInput.SetChannel(string.Empty);
            else PendingRemoteAssignments[endpointId] = string.Empty;
            return;
        }

        EnsureChannel(channelId, fields.Length >= 3 ? fields[2] : null);
        if (machine is QuantumOutputMachine output) output.SetChannel(channelId);
        else if (machine is QuantumInputMachine input) input.SetChannel(channelId);
        else PendingRemoteAssignments[endpointId] = channelId;
    }

    private static int Normalize(int cursor, int count) => count == 0 ? 0 : cursor % count;

    private sealed class ChannelState
    {
        public ChannelState(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
        public int OutputCursor { get; set; }
        public int InputCursor { get; set; }
    }
}

internal readonly record struct ChannelSummary(string Id, string Name, int Outputs, int Inputs);

internal readonly record struct QuantumEndpointView(int Id, bool IsOutput, UnityEngine.GameObject View);

internal readonly record struct PendingCapsule(MoonCargo Cargo, double Kilograms, double Capacity, string Owner);

internal sealed class QuantumOutputMachine : Machine
{
    private readonly Queue<PendingCapsule> pending = new();

    internal bool HasPending => pending.Count > 0;
    internal int PendingCount => pending.Count;
    internal string ChannelId { get; private set; } = string.Empty;
    internal bool IsAttached { get; private set; }

    public override void OnStart()
    {
        ChannelId = Data.TryGetValue("channel", out var savedChannel) ? savedChannel : string.Empty;
        QuantumNetwork.Add(this);
        if (Data.TryGetValue("pending", out var saved) && !string.IsNullOrWhiteSpace(saved))
        {
            foreach (var item in saved.Split(';'))
            {
                if (QuantumNetwork.TryDecode(item, out var capsule)) pending.Enqueue(capsule);
            }
        }
        RefreshAttachment();
    }

    public override void OnStop() => QuantumNetwork.Remove(this);

    public override void Tick(float dt) => RefreshAttachment();

    public override void OnView(UnityEngine.GameObject view)
    {
        QuantumNetwork.BindView(Id, view);
        var interaction = QuantumEndpointInteraction.Attach(view);
        interaction.Configure(Id, true);
    }

    public override bool Accept(string port, Capsule capsule)
    {
        RefreshAttachment();
        if (!Moon.IsAuthority || !IsAttached || port != "input" || string.IsNullOrWhiteSpace(ChannelId)) return false;
        if (pending.Count > 0) return false;

        var capsuleToSend = new PendingCapsule(capsule.Cargo, capsule.Kilograms, capsule.Capacity, capsule.Owner);
        if (!QuantumNetwork.TryTransmit(this, capsuleToSend))
        {
            Status = "Waiting for output belt";
            Tone = MachineTone.Idle;
            return false;
        }

        Status = "Resource sent";
        Tone = MachineTone.Working;
        return true;
    }

    internal bool TryDeliverTo(QuantumInputMachine input)
    {
        if (pending.Count == 0) return false;
        var next = pending.Peek();
        if (!input.TryReceive(next)) return false;
        pending.Dequeue();
        Status = pending.Count == 0 ? "Ready to send" : "Resources queued";
        Tone = pending.Count == 0 ? MachineTone.Idle : MachineTone.Working;
        return true;
    }

    internal void SetChannel(string channelId)
    {
        ChannelId = channelId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ChannelId)) Data.Remove("channel");
        else Data["channel"] = ChannelId;
        RefreshAttachment();
    }

    public override void OnSave()
    {
        if (string.IsNullOrWhiteSpace(ChannelId)) Data.Remove("channel");
        else Data["channel"] = ChannelId;
        Data["pending"] = string.Join(';', pending.Select(c => string.Join('|',
            ((byte)c.Cargo).ToString(CultureInfo.InvariantCulture),
            c.Kilograms.ToString("R", CultureInfo.InvariantCulture),
            c.Capacity.ToString("R", CultureInfo.InvariantCulture),
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(c.Owner ?? string.Empty)))));
    }

    public override void Describe(MachinePanel panel)
    {
        panel.Line("Quantum Output");
        panel.Line($"Channel: {QuantumNetwork.ChannelName(ChannelId)}");
        panel.Line($"{pending.Count} pending");
    }

    private void RefreshAttachment()
    {
        IsAttached = IsConnected("input");
        if (!IsAttached) Status = "Disabled: connect a conveyor";
        else if (string.IsNullOrWhiteSpace(ChannelId)) Status = "Unassigned";
        else Status = QuantumNetwork.ChannelName(ChannelId);
    }
}

internal sealed class QuantumInputMachine : Machine
{
    internal string ChannelId { get; private set; } = string.Empty;
    internal bool IsAttached { get; private set; }

    public override void OnStart()
    {
        ChannelId = Data.TryGetValue("channel", out var savedChannel) ? savedChannel : string.Empty;
        QuantumNetwork.Add(this);
        RefreshAttachment();
        Tone = MachineTone.Idle;
    }

    public override void OnStop() => QuantumNetwork.Remove(this);

    public override void OnView(UnityEngine.GameObject view)
    {
        QuantumNetwork.BindView(Id, view);
        var interaction = QuantumEndpointInteraction.Attach(view);
        interaction.Configure(Id, false);
    }

    public override void Tick(float dt)
    {
        RefreshAttachment();
        if (Moon.IsAuthority) QuantumNetwork.Tick();
    }

    internal bool TryReceive(PendingCapsule capsule)
    {
        RefreshAttachment();
        if (!IsAttached) return false;
        if (!Emit("output", capsule.Cargo, capsule.Kilograms)) return false;
        Status = "Resource delivered";
        Tone = MachineTone.Working;
        return true;
    }

    internal void SetChannel(string channelId)
    {
        ChannelId = channelId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ChannelId)) Data.Remove("channel");
        else Data["channel"] = ChannelId;
        RefreshAttachment();
    }

    public override void OnSave()
    {
        if (string.IsNullOrWhiteSpace(ChannelId)) Data.Remove("channel");
        else Data["channel"] = ChannelId;
    }

    public override void Describe(MachinePanel panel)
    {
        panel.Line("Quantum Input");
        panel.Line($"Channel: {QuantumNetwork.ChannelName(ChannelId)}");
        panel.Line("Waiting for a connected output belt");
    }

    private void RefreshAttachment()
    {
        IsAttached = IsConnected("output");
        if (!IsAttached) Status = "Disabled: connect a conveyor";
        else if (string.IsNullOrWhiteSpace(ChannelId)) Status = "Unassigned";
        else Status = QuantumNetwork.ChannelName(ChannelId);
    }
}
