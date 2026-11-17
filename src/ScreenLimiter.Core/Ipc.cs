using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace ScreenLimiter.Core;

/// <summary>Operation names understood by the service.</summary>
public static class Ops
{
    // Any signed-in user (the tray agent).
    public const string Status = "status";
    public const string GrantPin = "grantPin";

    // Elevated administrators only (the admin console).
    public const string GetConfig = "getConfig";
    public const string SaveConfig = "saveConfig";
    public const string Grant = "grant";
    public const string SetPin = "setPin";
}

public sealed class Request
{
    public string Op { get; set; } = "";
    public string? Sid { get; set; }
    public int Minutes { get; set; }
    public string? Pin { get; set; }
    public AppConfig? Config { get; set; }
    /// <summary>Sent with every status poll: is the agent's lock overlay currently on screen?</summary>
    public bool OverlayShown { get; set; }
}

public sealed class StatusDto
{
    public bool Limited { get; set; }
    public bool Allowed { get; set; } = true;
    public int RemainingSeconds { get; set; }
    public int LimitSeconds { get; set; }
    public int UsedSeconds { get; set; }
    public LimitMode Mode { get; set; }
    public TimeUpAction OnTimeUp { get; set; }
    public int[] WarnMinutes { get; set; } = [];
    public string Language { get; set; } = "he";
    public bool PinSet { get; set; }
    /// <summary>When blocked: what ran out.</summary>
    public BlockReason Reason { get; set; }
    /// <summary>When blocked: when time comes back on its own, in the service's local time. Null if not this week.</summary>
    public DateTime? NextAllowedLocal { get; set; }
    /// <summary>The service's local time, so "today" and "tomorrow" match the service, not the user's clock.</summary>
    public DateTime NowLocal { get; set; }
}

public sealed class LocalUserDto
{
    public string Sid { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsAdmin { get; set; }
    public StatusDto? Status { get; set; }
}

public sealed class Response
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public StatusDto? Status { get; set; }
    public AppConfig? Config { get; set; }
    public List<LocalUserDto>? Users { get; set; }
    public bool PinSet { get; set; }

    public static Response Success() => new() { Ok = true };
    public static Response Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>One request, one response, one connection. Used by the agent and the admin console.</summary>
public static class PipeClient
{
    public const string PipeName = "ScreenLimiter.v1";

    public static async Task<Response> SendAsync(Request request, int timeoutMs = 4000)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            // Identification level: the service may learn who we are, but can't act as us.
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);

            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json.Compact) + "\n");
            await pipe.WriteAsync(payload, cts.Token).ConfigureAwait(false);
            await pipe.FlushAsync(cts.Token).ConfigureAwait(false);

            using var reader = new StreamReader(pipe, Encoding.UTF8);
            var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
            if (line is null) return Response.Fail("service_unavailable");
            return JsonSerializer.Deserialize<Response>(line, Json.Compact) ?? Response.Fail("service_unavailable");
        }
        catch (Exception)
        {
            // Whatever went wrong (service stopped, timeout, broken pipe), callers treat it the same way.
            return Response.Fail("service_unavailable");
        }
    }
}
