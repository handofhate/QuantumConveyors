using System.Globalization;
using BepInEx.Logging;
using MoonCleaners;
using MoonFramework;

namespace QuantumConveyors;

internal static class QuantumNetwork
{
    private static readonly List<QuantumOutputMachine> Outputs = new();
    private static readonly List<QuantumInputMachine> Inputs = new();
    private static ManualLogSource log;
    private static int outputCursor;
    private static int inputCursor;

    public static void Initialize(ManualLogSource source) => log = source;

    public static void Add(QuantumOutputMachine machine) => Outputs.Add(machine);
    public static void Add(QuantumInputMachine machine) => Inputs.Add(machine);
    public static void Remove(QuantumOutputMachine machine) => Outputs.Remove(machine);
    public static void Remove(QuantumInputMachine machine) => Inputs.Remove(machine);

    public static void Tick()
    {
        if (!Moon.IsAuthority || Outputs.Count == 0 || Inputs.Count == 0) return;

        outputCursor = Normalize(outputCursor, Outputs.Count);
        inputCursor = Normalize(inputCursor, Inputs.Count);

        for (var outputOffset = 0; outputOffset < Outputs.Count; outputOffset++)
        {
            var output = Outputs[(outputCursor + outputOffset) % Outputs.Count];
            if (!output.HasPending) continue;

            for (var inputOffset = 0; inputOffset < Inputs.Count; inputOffset++)
            {
                var inputIndex = (inputCursor + inputOffset) % Inputs.Count;
                if (!output.TryDeliverTo(Inputs[inputIndex])) continue;

                outputCursor = (outputCursor + outputOffset + 1) % Outputs.Count;
                inputCursor = (inputIndex + 1) % Inputs.Count;
                return;
            }
        }
    }

    public static string FormatStatus()
    {
        var pending = Outputs.Sum(output => output.PendingCount);
        return $"Quantum network: {Outputs.Count} output(s), {Inputs.Count} input(s), {pending} pending";
    }

    public static void Report(Exception exception)
    {
        if (log != null) log.LogError(exception);
    }

    private static int Normalize(int cursor, int count) => count == 0 ? 0 : cursor % count;

    internal static string Encode(Capsule capsule)
    {
        return string.Join('|',
            ((byte)capsule.Cargo).ToString(CultureInfo.InvariantCulture),
            capsule.Kilograms.ToString("R", CultureInfo.InvariantCulture),
            capsule.Capacity.ToString("R", CultureInfo.InvariantCulture),
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(capsule.Owner ?? string.Empty)));
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
}

internal readonly record struct PendingCapsule(MoonCargo Cargo, double Kilograms, double Capacity, string Owner);

internal sealed class QuantumOutputMachine : Machine
{
    private readonly Queue<PendingCapsule> pending = new();

    internal bool HasPending => pending.Count > 0;
    internal int PendingCount => pending.Count;

    public override void OnStart()
    {
        QuantumNetwork.Add(this);
        if (Data.TryGetValue("pending", out var saved) && !string.IsNullOrWhiteSpace(saved))
        {
            foreach (var item in saved.Split(';'))
            {
                if (QuantumNetwork.TryDecode(item, out var capsule)) pending.Enqueue(capsule);
            }
        }
        Status = "Ready to send";
    }

    public override void OnStop() => QuantumNetwork.Remove(this);

    public override bool Accept(string port, Capsule capsule)
    {
        if (!Moon.IsAuthority || port != "input") return false;
        pending.Enqueue(new PendingCapsule(capsule.Cargo, capsule.Kilograms, capsule.Capacity, capsule.Owner));
        Status = "Resource queued";
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

    public override void OnSave()
    {
        Data["pending"] = string.Join(';', pending.Select(c => string.Join('|',
            ((byte)c.Cargo).ToString(CultureInfo.InvariantCulture),
            c.Kilograms.ToString("R", CultureInfo.InvariantCulture),
            c.Capacity.ToString("R", CultureInfo.InvariantCulture),
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(c.Owner ?? string.Empty)))));
    }

    public override void Describe(MachinePanel panel)
    {
        panel.Line("Quantum Output");
        panel.Line(QuantumNetwork.FormatStatus());
        panel.Line($"This endpoint: {pending.Count} pending");
    }
}

internal sealed class QuantumInputMachine : Machine
{
    public override void OnStart()
    {
        QuantumNetwork.Add(this);
        Status = "Waiting for resource";
        Tone = MachineTone.Idle;
    }

    public override void OnStop() => QuantumNetwork.Remove(this);

    public override void Tick(float dt)
    {
        if (Moon.IsAuthority) QuantumNetwork.Tick();
    }

    internal bool TryReceive(PendingCapsule capsule)
    {
        if (!IsConnected("output")) return false;
        if (!Emit("output", capsule.Cargo, capsule.Kilograms)) return false;
        Status = "Resource delivered";
        Tone = MachineTone.Working;
        return true;
    }

    public override void Describe(MachinePanel panel)
    {
        panel.Line("Quantum Input");
        panel.Line(QuantumNetwork.FormatStatus());
        panel.Line("Waiting for a connected output belt");
    }
}
