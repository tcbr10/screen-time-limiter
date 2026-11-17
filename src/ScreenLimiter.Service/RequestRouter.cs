using ScreenLimiter.Core;

namespace ScreenLimiter.Service;

/// <summary>Who is asking. Filled in by the transport (today the local pipe, later a remote link).</summary>
public sealed record Caller(string Sid, bool IsAdmin, bool IsTrustedAgent, int SessionId);

/// <summary>
/// Decides what each request may do. It knows nothing about pipes or sockets, so a future remote
/// transport can reuse it unchanged by building its own <see cref="Caller"/>.
/// </summary>
public sealed class RequestRouter(StateManager state)
{
    public Response Handle(Caller caller, Request request)
    {
        switch (request.Op)
        {
            case Ops.Status:
                if (caller.IsTrustedAgent) state.ReportAgent(caller.SessionId, request.OverlayShown);
                return new Response { Ok = true, Status = state.Status(caller.Sid) };

            case Ops.GrantPin:
                var error = state.GrantWithPin(caller.Sid, request.Pin, request.Minutes);
                return error is null ? Response.Success() : Response.Fail(error);
        }

        if (!caller.IsAdmin) return Response.Fail("not_admin");

        switch (request.Op)
        {
            case Ops.GetConfig:
                var users = LocalUsers.List()
                    .Select(u => new LocalUserDto { Sid = u.Sid, Name = u.Name, IsAdmin = u.IsAdmin, Status = state.Status(u.Sid) })
                    .ToList();
                return new Response { Ok = true, Config = state.ConfigForAdmin(), Users = users, PinSet = state.PinSet };

            case Ops.SaveConfig:
                if (request.Config is null) return Response.Fail("bad_request");
                return state.SaveConfig(request.Config) ? Response.Success() : Response.Fail("save_failed");

            case Ops.Grant:
                if (string.IsNullOrEmpty(request.Sid)) return Response.Fail("bad_request");
                return state.Grant(request.Sid, request.Minutes) ? Response.Success() : Response.Fail("not_limited");

            case Ops.SetPin:
                if (!PinHasher.IsValidFormat(request.Pin)) return Response.Fail("bad_pin");
                return state.SetPin(request.Pin!) ? Response.Success() : Response.Fail("save_failed");

            default:
                return Response.Fail("bad_request");
        }
    }
}
