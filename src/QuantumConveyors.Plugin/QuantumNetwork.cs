using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using MoonCleaners;
using MoonFramework;
using UnityEngine;

namespace QuantumConveyors;

internal static class QuantumNetwork
{
    private static readonly List<QuantumOutputMachine> Outputs = new();
    private static readonly List<QuantumInputMachine> Inputs = new();
    private static readonly Dictionary<string, ChannelState> Channels = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, string> PendingRemoteAssignments = new();
    private static readonly Dictionary<int, UnityEngine.GameObject> BoundViews = new();
    private static ManualLogSource log;

    private const string ChannelMessage = Plugin.PluginGuid + ".assignment";
    private const string ChannelRenameMessage = Plugin.PluginGuid + ".rename";
    private const string ChannelDeleteMessage = Plugin.PluginGuid + ".delete";

    public static void Initialize(ManualLogSource source)
    {
        log = source;
        Diagnostic("QuantumNetwork initialized");
        Crew.On(ChannelMessage, (_, data) => ApplyNetworkMessage(data));
        Crew.On(ChannelRenameMessage, (_, data) => ApplyRenameMessage(data));
        Crew.On(ChannelDeleteMessage, (_, data) => ApplyDeleteMessage(data));
    }

    internal static void Diagnostic(string message)
    {
        log?.LogInfo($"[QuantumConveyors] {message}");
        try
        {
            var path = Path.Combine(Paths.BepInExRootPath, "QuantumConveyors-diagnostic.log");
            File.AppendAllText(path, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never affect gameplay.
        }
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
        if (view != null)
        {
            BoundViews[endpointId] = view;
            Diagnostic($"Bound view for endpoint {endpointId}: {view.name}");
        }
    }

    public static void BindUnidentifiedView(UnityEngine.GameObject view, bool isOutput)
    {
        if (view == null) return;
        Diagnostic($"Building OnView callback: {(isOutput ? "output" : "input")} {view.name}");
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

    public static QuantumTransferResult TryTransmit(QuantumOutputMachine source, PendingCapsule capsule)
    {
        if (!Moon.IsAuthority || string.IsNullOrWhiteSpace(source.ChannelId)) return QuantumTransferResult.NoMatchingAdapter;
        if (!Channels.TryGetValue(source.ChannelId, out var channel)) return QuantumTransferResult.NoMatchingAdapter;

        var matchingInputs = Inputs.Where(input => input.ChannelId == source.ChannelId).ToList();
        if (matchingInputs.Count == 0) return QuantumTransferResult.NoMatchingAdapter;
        var inputs = matchingInputs.Where(input => input.IsAttached).ToList();
        if (inputs.Count == 0) return QuantumTransferResult.DestinationUnavailable;

        channel.InputCursor = Normalize(channel.InputCursor, inputs.Count);
        for (var inputOffset = 0; inputOffset < inputs.Count; inputOffset++)
        {
            var inputIndex = (channel.InputCursor + inputOffset) % inputs.Count;
            if (!inputs[inputIndex].TryReceive(capsule)) continue;

            channel.InputCursor = (inputIndex + 1) % inputs.Count;
            return QuantumTransferResult.Sent;
        }

        return QuantumTransferResult.DestinationUnavailable;
    }

    public static string FormatStatus()
    {
        var pending = Outputs.Sum(output => output.PendingCount);
        return $"Quantum networks: {Channels.Count}, {Outputs.Count} output(s), {Inputs.Count} input(s), {pending} pending";
    }

    public static IReadOnlyList<ChannelSummary> Summaries()
    {
        return Channels.Values
            .OrderBy(channel => string.Equals(channel.Name, "New Channel", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
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

    internal static bool HasChannelInput(string channelId)
    {
        return !string.IsNullOrWhiteSpace(channelId) && Inputs.Any(input => input.ChannelId == channelId);
    }

    internal static bool HasAttachedChannelInput(string channelId)
    {
        return !string.IsNullOrWhiteSpace(channelId) && Inputs.Any(input => input.ChannelId == channelId && input.IsAttached);
    }

    internal static bool HasChannelOutput(string channelId)
    {
        return !string.IsNullOrWhiteSpace(channelId) && Outputs.Any(output => output.ChannelId == channelId);
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
        channelId = CreateChannelId();
        return AssignEndpoint(endpointId, channelId);
    }

    public static bool CreateChannel(out string channelId)
    {
        channelId = string.Empty;
        if (!Moon.IsAuthority) return false;
        channelId = CreateChannelId();
        return true;
    }

    public static bool AssignNextChannel(int endpointId)
    {
        if (!Moon.IsAuthority) return false;
        if (Channels.Count == 0) return CreateAndAssign(endpointId, out _);
        var channels = Channels.Values.OrderBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var current = ChannelFor(endpointId);
        var currentIndex = channels.FindIndex(channel => channel.Id == current);
        var nextIndex = (currentIndex + 1 + channels.Count) % channels.Count;
        return AssignEndpoint(endpointId, channels[nextIndex].Id);
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

    public static bool RenameChannel(string channelId, string name)
    {
        if (!Moon.IsAuthority || string.IsNullOrWhiteSpace(channelId) || !Channels.TryGetValue(channelId, out var channel)) return false;
        name = CleanChannelName(name);
        if (string.IsNullOrWhiteSpace(name)) return false;

        channel.Name = name;
        var world = Moon.WorldData(Plugin.PluginGuid);
        if (world != null) world[$"name.{channelId}"] = name;
        if (Crew.InCrew)
            Crew.Send(ChannelRenameMessage, System.Text.Encoding.UTF8.GetBytes(string.Join('\t', channelId, name)));
        return true;
    }

    public static bool DeleteChannel(string channelId)
    {
        if (!Moon.IsAuthority || string.IsNullOrWhiteSpace(channelId) || !Channels.ContainsKey(channelId)) return false;

        foreach (var output in Outputs.Where(output => output.ChannelId == channelId).ToList())
        {
            output.SetChannel(string.Empty);
            BroadcastAssignment(output.Id, string.Empty);
        }
        foreach (var input in Inputs.Where(input => input.ChannelId == channelId).ToList())
        {
            input.SetChannel(string.Empty);
            BroadcastAssignment(input.Id, string.Empty);
        }

        foreach (var endpointId in PendingRemoteAssignments
                     .Where(pair => pair.Value == channelId)
                     .Select(pair => pair.Key)
                     .ToList())
            PendingRemoteAssignments.Remove(endpointId);

        Channels.Remove(channelId);

        var world = Moon.WorldData(Plugin.PluginGuid);
        world?.Remove($"name.{channelId}");
        if (Crew.InCrew) Crew.Send(ChannelDeleteMessage, System.Text.Encoding.UTF8.GetBytes(channelId));
        return true;
    }

    public static string ChannelName(string channelId)
    {
        return !string.IsNullOrWhiteSpace(channelId) && Channels.TryGetValue(channelId, out var channel)
            ? channel.Name
            : "Unassigned";
    }

    public static string ChannelLabel(string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId) || !Channels.TryGetValue(channelId, out var channel)) return "None";
        var name = channel.Name;
        const string longPrefix = "Quantum Channel ";
        if (name.StartsWith(longPrefix, StringComparison.OrdinalIgnoreCase)) return "Ch " + name[longPrefix.Length..];
        return name.StartsWith("Channel ", StringComparison.OrdinalIgnoreCase) ? "Ch " + name[8..] : name;
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

    private static string CreateChannelId()
    {
        var next = 1;

        string id;
        do id = $"channel-{next++}";
        while (Channels.ContainsKey(id));

        EnsureChannel(id);
        return id;
    }

    private static void EnsureChannel(string channelId, string preferredName = null)
    {
        if (string.IsNullOrWhiteSpace(channelId) || Channels.ContainsKey(channelId)) return;

        var name = string.IsNullOrWhiteSpace(preferredName) || IsLegacyDefaultName(preferredName)
            ? DefaultChannelName(channelId)
            : preferredName;
        var world = Moon.WorldData(Plugin.PluginGuid);
        if (world != null && world.TryGetValue($"name.{channelId}", out var savedName) && !string.IsNullOrWhiteSpace(savedName))
            name = IsLegacyDefaultName(savedName) ? DefaultChannelName(channelId) : savedName;

        Channels[channelId] = new ChannelState(channelId, name);
        if (world != null && !world.ContainsKey($"name.{channelId}")) world[$"name.{channelId}"] = name;
    }

    private static string DefaultChannelName(string channelId)
    {
        return "New Channel";
    }

    private static bool IsLegacyDefaultName(string name)
    {
        const string prefix = "Quantum Channel ";
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return true;

        const string shortPrefix = "Channel ";
        return name.StartsWith(shortPrefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name[shortPrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static string CleanChannelName(string name)
    {
        name = (name ?? string.Empty).Trim();
        return name.Length <= 24 ? name : name[..24].TrimEnd();
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

    private static void ApplyRenameMessage(byte[] data)
    {
        var fields = System.Text.Encoding.UTF8.GetString(data ?? Array.Empty<byte>()).Split('\t', 2);
        if (fields.Length != 2 || string.IsNullOrWhiteSpace(fields[0])) return;
        EnsureChannel(fields[0], fields[1]);
        if (!Channels.TryGetValue(fields[0], out var channel)) return;
        channel.Name = CleanChannelName(fields[1]);
    }

    private static void ApplyDeleteMessage(byte[] data)
    {
        var channelId = System.Text.Encoding.UTF8.GetString(data ?? Array.Empty<byte>());
        if (string.IsNullOrWhiteSpace(channelId)) return;
        foreach (var output in Outputs.Where(output => output.ChannelId == channelId).ToList()) output.SetChannel(string.Empty);
        foreach (var input in Inputs.Where(input => input.ChannelId == channelId).ToList()) input.SetChannel(string.Empty);
        Channels.Remove(channelId);
        foreach (var endpointId in PendingRemoteAssignments
                     .Where(pair => pair.Value == channelId)
                     .Select(pair => pair.Key)
                     .ToList())
            PendingRemoteAssignments.Remove(endpointId);
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
        public string Name { get; set; }
        public int OutputCursor { get; set; }
        public int InputCursor { get; set; }
    }
}

internal readonly record struct ChannelSummary(string Id, string Name, int Outputs, int Inputs);

internal enum QuantumTransferResult
{
    Sent,
    NoMatchingAdapter,
    DestinationUnavailable,
}

internal readonly record struct QuantumEndpointView(int Id, bool IsOutput, UnityEngine.GameObject View);

internal readonly record struct PendingCapsule(MoonCargo Cargo, double Kilograms, double Capacity, string Owner);

internal sealed class QuantumOutputMachine : Machine
{
    private const float ActivityDisplaySeconds = 3f;
    private const float FullDisplayDelaySeconds = 3f;
    private readonly Queue<PendingCapsule> pending = new();
    private float activeUntil;
    private float beltFullSince = -1f;

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

    public override void Animate(float dt)
    {
        QuantumVisuals.TrySetIndicator(Part("Indicator"), CurrentIndicatorState());
    }

    public override void OnView(UnityEngine.GameObject view)
    {
        QuantumNetwork.BindView(Id, view);
    }

    public override bool Accept(string port, Capsule capsule)
    {
        RefreshAttachment();
        if (!Moon.IsAuthority || port != "input" || string.IsNullOrWhiteSpace(ChannelId)) return false;
        if (pending.Count > 0) return false;

        var capsuleToSend = new PendingCapsule(capsule.Cargo, capsule.Kilograms, capsule.Capacity, capsule.Owner);
        var result = QuantumNetwork.TryTransmit(this, capsuleToSend);
        if (result != QuantumTransferResult.Sent)
        {
            if (result == QuantumTransferResult.DestinationUnavailable && beltFullSince < 0f)
                beltFullSince = Time.unscaledTime;
            if (result == QuantumTransferResult.NoMatchingAdapter) beltFullSince = -1f;
            Tone = MachineTone.Idle;
            return false;
        }

        IsAttached = true;
        beltFullSince = -1f;
        activeUntil = Time.unscaledTime + ActivityDisplaySeconds;
        Status = "Sending";
        Tone = MachineTone.Working;
        return true;
    }

    internal bool TryDeliverTo(QuantumInputMachine input)
    {
        if (pending.Count == 0) return false;
        var next = pending.Peek();
        if (!input.TryReceive(next)) return false;
        pending.Dequeue();
        beltFullSince = -1f;
        activeUntil = Time.unscaledTime + ActivityDisplaySeconds;
        Status = "Sending";
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
        panel.Line($"Ch: {QuantumNetwork.ChannelLabel(ChannelId)}");
        panel.Line(StatusText(CurrentIndicatorState(), true));
    }

    private void RefreshAttachment()
    {
        // The framework's IsConnected helper checks belts starting at an output
        // port, so it cannot reliably report this building's incoming port.
        // Accept is only called when a real conveyor offers a capsule; do not
        // block that callback based on the one-sided helper.
        IsAttached = true;
        Status = string.IsNullOrWhiteSpace(ChannelId)
            ? "Disabled: Press E to configure"
            : "Press E to configure";
    }

    private QuantumIndicatorState CurrentIndicatorState()
    {
        if (string.IsNullOrWhiteSpace(ChannelId)) return QuantumIndicatorState.WaitingForSetup;
        if (!QuantumNetwork.HasChannelInput(ChannelId)) return QuantumIndicatorState.NoMatchingAdapter;
        if (!QuantumNetwork.HasAttachedChannelInput(ChannelId)) return QuantumIndicatorState.ConveyorMissing;
        if (activeUntil > Time.unscaledTime) return QuantumIndicatorState.Active;
        if (beltFullSince >= 0f && Time.unscaledTime - beltFullSince >= FullDisplayDelaySeconds)
            return QuantumIndicatorState.OutputBeltFull;
        return QuantumIndicatorState.Ready;
    }

    private static string StatusText(QuantumIndicatorState state, bool isOutput)
    {
        return state switch
        {
            QuantumIndicatorState.WaitingForSetup => "Waiting for setup",
            QuantumIndicatorState.NoMatchingAdapter => "No matching adapter",
            QuantumIndicatorState.ConveyorMissing => "Conveyor missing",
            QuantumIndicatorState.Active => isOutput ? "Sending" : "Receiving",
            QuantumIndicatorState.OutputBeltFull => "Output belt full",
            _ => isOutput ? "Ready to send" : "Ready to receive",
        };
    }
}

internal sealed class QuantumInputMachine : Machine
{
    private const float ActivityDisplaySeconds = 3f;
    private const float FullDisplayDelaySeconds = 3f;
    private float activeUntil;
    private float beltFullSince = -1f;
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
    }

    public override void Tick(float dt)
    {
        RefreshAttachment();
        if (Moon.IsAuthority) QuantumNetwork.Tick();
    }

    public override void Animate(float dt)
    {
        QuantumVisuals.TrySetIndicator(Part("Indicator"), CurrentIndicatorState());
    }

    internal bool TryReceive(PendingCapsule capsule)
    {
        RefreshAttachment();
        if (!IsAttached) return false;
        if (!Emit("output", capsule.Cargo, capsule.Kilograms))
        {
            if (beltFullSince < 0f) beltFullSince = Time.unscaledTime;
            return false;
        }
        beltFullSince = -1f;
        activeUntil = Time.unscaledTime + ActivityDisplaySeconds;
        Status = "Receiving";
        Tone = MachineTone.Working;
        return true;
    }

    internal void SetChannel(string channelId)
    {
        ChannelId = channelId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ChannelId)) beltFullSince = -1f;
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
        panel.Line($"Ch: {QuantumNetwork.ChannelLabel(ChannelId)}");
        panel.Line(StatusText(CurrentIndicatorState()));
    }

    private void RefreshAttachment()
    {
        IsAttached = IsConnected("output");
        Status = string.IsNullOrWhiteSpace(ChannelId)
            ? "Disabled: Press E to configure"
            : "Press E to configure";
    }

    private QuantumIndicatorState CurrentIndicatorState()
    {
        if (string.IsNullOrWhiteSpace(ChannelId)) return QuantumIndicatorState.WaitingForSetup;
        if (!IsAttached) return QuantumIndicatorState.ConveyorMissing;
        if (!QuantumNetwork.HasChannelOutput(ChannelId)) return QuantumIndicatorState.NoMatchingAdapter;
        if (activeUntil > Time.unscaledTime) return QuantumIndicatorState.Active;
        if (beltFullSince >= 0f && Time.unscaledTime - beltFullSince >= FullDisplayDelaySeconds)
            return QuantumIndicatorState.OutputBeltFull;
        return QuantumIndicatorState.Ready;
    }

    private static string StatusText(QuantumIndicatorState state)
    {
        return state switch
        {
            QuantumIndicatorState.WaitingForSetup => "Waiting for setup",
            QuantumIndicatorState.NoMatchingAdapter => "No matching adapter",
            QuantumIndicatorState.ConveyorMissing => "Conveyor missing",
            QuantumIndicatorState.Active => "Receiving",
            QuantumIndicatorState.OutputBeltFull => "Output belt full",
            _ => "Ready to receive",
        };
    }
}
