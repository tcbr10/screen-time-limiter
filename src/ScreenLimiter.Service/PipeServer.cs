using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ScreenLimiter.Core;

namespace ScreenLimiter.Service;

/// <summary>
/// The local door into the service. Each connection carries one JSON request and gets one JSON reply.
/// Windows tells us who connected, so callers can't claim to be someone else.
/// </summary>
public sealed class PipeServer(RequestRouter router, ILogger<PipeServer> log) : BackgroundService
{
    private static readonly string AgentPath = Path.Combine(AppContext.BaseDirectory, "ScreenLimiter.Agent.exe");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var security = BuildSecurity();
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = NamedPipeServerStreamAcl.Create(PipeClient.PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous, 0, 0, security);
                await server.WaitForConnectionAsync(stoppingToken);
                var connected = server;
                server = null;
                _ = Task.Run(() => HandleAsync(connected), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                log.LogError(e, "Pipe listener failed; retrying");
                await Task.Delay(1000, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream server)
    {
        await using (server)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(server, Encoding.UTF8, false, 4096, leaveOpen: true);
                var line = await reader.ReadLineAsync(timeout.Token);
                if (line is null) return;

                Response response;
                try
                {
                    var request = JsonSerializer.Deserialize<Request>(line, Json.Compact);
                    response = request is null ? Response.Fail("bad_request") : router.Handle(Identify(server), request);
                }
                catch (JsonException)
                {
                    response = Response.Fail("bad_request");
                }

                var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, Json.Compact) + "\n");
                await server.WriteAsync(payload, timeout.Token);
                await server.FlushAsync(timeout.Token);
                server.WaitForPipeDrain();
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                log.LogDebug(e, "Pipe client went away");
            }
            catch (Exception e)
            {
                log.LogError(e, "Request failed");
            }
        }
    }

    private static Caller Identify(NamedPipeServerStream server)
    {
        var sid = "";
        var isAdmin = false;
        server.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent();
            sid = identity.User?.Value ?? "";
            // True only for an elevated administrator token (or SYSTEM), not for an admin running unelevated.
            isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        });

        Native.GetNamedPipeClientSessionId(server.SafePipeHandle, out var sessionId);
        Native.GetNamedPipeClientProcessId(server.SafePipeHandle, out var processId);
        return new Caller(sid, isAdmin, IsInstalledAgent((int)processId), (int)sessionId);
    }

    /// <summary>
    /// Heartbeats only count when they come from the agent installed next to the service, so a home-made
    /// program can't pretend the lock overlay is showing.
    /// </summary>
    private static bool IsInstalledAgent(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var path = process.MainModule?.FileName;
            return path is not null && string.Equals(Path.GetFullPath(path), AgentPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static PipeSecurity BuildSecurity()
    {
        var security = new PipeSecurity();
        // Everyone signed in may talk to the service; only SYSTEM and admins may create instances of the pipe.
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
