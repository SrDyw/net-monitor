using System.Collections.Generic;
using System.Text.Json;

namespace NetMonitor.Agent;

/// <summary>Traffic with one remote endpoint during the last second, attributed to a process.</summary>
public sealed record FlowDelta(int Pid, string Proto, string Ip, int Port, long Down, long Up);

/// <summary>
/// One line on the pipe from the elevated agent to the app. <see cref="Kind"/> picks the fields:
/// "tick" (Flows, Names), "dns" (Pid, Name, Ips), "conn" (Pid, Proto, Ip, Port, Open), "error" (Text).
/// </summary>
public sealed class AgentMessage
{
    public string Kind { get; init; } = "";
    public List<FlowDelta>? Flows { get; init; }
    /// <summary>Names of processes first seen in this tick, so short-lived ones can still be attributed.</summary>
    public Dictionary<int, string>? Names { get; init; }
    public int Pid { get; init; }
    public string? Name { get; init; }
    public string[]? Ips { get; init; }
    public string? Proto { get; init; }
    public string? Ip { get; init; }
    public int Port { get; init; }
    public bool Open { get; init; }
    public string? Text { get; init; }

    public static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
}
