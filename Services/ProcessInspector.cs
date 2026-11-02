using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NetMonitor.Agent;

namespace NetMonitor.Services;

public enum InspectorState
{
    Idle,
    WaitingForPermission,
    Running,
    Denied,
    Failed,
}

/// <summary>
/// The app's half of the process inspector. Launches the elevated agent (one UAC prompt),
/// owns the pipe it writes to, and raises each message on the UI thread.
/// </summary>
public sealed class ProcessInspector : IDisposable
{
    private const int ErrorCancelled = 1223; // the user said no to UAC

    private NamedPipeServerStream? _pipe;
    private CancellationTokenSource? _cts;

    public InspectorState State { get; private set; } = InspectorState.Idle;
    public string? Error { get; private set; }

    public event EventHandler? StateChanged;
    public event EventHandler<AgentMessage>? Message;

    public async void Start()
    {
        if (State is InspectorState.Running or InspectorState.WaitingForPermission) return;
        Stop();
        SetState(InspectorState.WaitingForPermission);

        string name = $"NetMonitor-{Guid.NewGuid():N}";
        // This run's pipe and token live in locals: a Stop() or a newer Start() replaces the
        // fields, and every await below checks the token before touching shared state again.
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        _cts = cts;
        NamedPipeServerStream? pipe = null;
        try
        {
            pipe = CreatePipe(name);
            _pipe = pipe;
            bool launched = await Task.Run(() => LaunchAgent(name));
            if (token.IsCancellationRequested) return;
            if (!launched)
            {
                CloseAll();
                SetState(InspectorState.Denied);
                return;
            }

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
            wait.CancelAfter(TimeSpan.FromSeconds(20));
            await pipe.WaitForConnectionAsync(wait.Token);
            if (token.IsCancellationRequested) return;
            SetState(InspectorState.Running);
            await ReadAsync(pipe, token);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            return; // stopped on purpose; whatever the closed pipe threw is expected
        }
        catch (OperationCanceledException)
        {
            Fail("El agente no respondió. Si aprobaste el aviso con otra cuenta de administrador, " +
                 "el inspector solo funciona cuando lo apruebas con tu propia cuenta.");
            return;
        }
        catch (Exception ex)
        {
            Log.Error("inspector", ex);
            Fail(ex.Message);
            return;
        }
        if (!token.IsCancellationRequested && State == InspectorState.Running) Fail("El agente de rastreo se detuvo.");
    }

    public void Stop()
    {
        _cts?.Cancel();
        CloseAll();
        if (State != InspectorState.Idle) SetState(InspectorState.Idle);
    }

    private async Task ReadAsync(Stream pipe, CancellationToken token)
    {
        using var reader = new StreamReader(pipe);
        while (!token.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(token);
            if (line is null) return; // agent exited
            AgentMessage? message;
            try { message = JsonSerializer.Deserialize<AgentMessage>(line, AgentMessage.Json); }
            catch (JsonException) { continue; }
            if (message is null) continue;
            if (message.Kind == "error")
            {
                Fail(message.Text ?? "Error del agente.");
                return;
            }
            Message?.Invoke(this, message);
        }
    }

    /// <summary>Starts this executable as the agent; false when the UAC prompt was declined.</summary>
    private static bool LaunchAgent(string pipeName)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!, $"{EtwAgent.Flag} {pipeName} {Environment.ProcessId}")
        {
            UseShellExecute = true,
            Verb = IsElevated() ? "" : "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            Process.Start(psi);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
    }

    /// <summary>Pipe only this user can open; the elevated agent runs as the same user.</summary>
    private static NamedPipeServerStream CreatePipe(string name)
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User!;
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, security);
    }

    private static bool IsElevated() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private void Fail(string error)
    {
        CloseAll();
        Error = error;
        SetState(InspectorState.Failed);
    }

    private void CloseAll()
    {
        // Closing the pipe makes the agent's next write fail, and it exits within a second.
        try { _pipe?.Dispose(); } catch (IOException) { }
        _pipe = null;
    }

    private void SetState(InspectorState state)
    {
        State = state;
        if (state != InspectorState.Failed) Error = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Stop();
}
