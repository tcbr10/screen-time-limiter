using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace ScreenLimiter.Service;

/// <summary>Who is signed in, whether their screen is locked, and the ways to end or interrupt a session.</summary>
internal static class Sessions
{
    /// <summary>Ids of sessions with a user at the screen (console or remote desktop).</summary>
    public static List<int> Active()
    {
        var result = new List<int>();
        if (!Native.WTSEnumerateSessionsW(IntPtr.Zero, 0, 1, out var buffer, out var count)) return result;
        try
        {
            var size = Marshal.SizeOf<Native.WTS_SESSION_INFO>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<Native.WTS_SESSION_INFO>(buffer + i * size);
                if (info.State == Native.WTSActive && info.SessionId != 0) result.Add(info.SessionId);
            }
        }
        finally
        {
            Native.WTSFreeMemory(buffer);
        }
        return result;
    }

    /// <summary>The SID of the session's user, or null when nobody is signed in there (or we aren't SYSTEM).</summary>
    public static string? UserSid(int sessionId)
    {
        if (!Native.WTSQueryUserToken((uint)sessionId, out var token)) return null;
        try
        {
            using var identity = new WindowsIdentity(token);
            return identity.User?.Value;
        }
        finally
        {
            Native.CloseHandle(token);
        }
    }

    public static bool IsLocked(int sessionId)
    {
        if (!Native.WTSQuerySessionInformationW(IntPtr.Zero, sessionId, Native.WTSSessionInfoEx, out var buffer, out var bytes))
            return false;
        try
        {
            // WTSINFOEXW: Level (4 bytes + 4 padding), then WTSINFOEX_LEVEL1_W { SessionId, SessionState, SessionFlags, ... }.
            const int sessionFlagsOffset = 16;
            if (bytes < sessionFlagsOffset + 4) return false;
            return Marshal.ReadInt32(buffer, sessionFlagsOffset) == Native.WTS_SESSIONSTATE_LOCK;
        }
        finally
        {
            Native.WTSFreeMemory(buffer);
        }
    }

    /// <summary>Sends the session back to the sign-in screen. Programs keep running.</summary>
    public static void Disconnect(int sessionId) => Native.WTSDisconnectSession(IntPtr.Zero, sessionId, false);

    /// <summary>Signs the user out. Unsaved work is lost, which is why warnings come first.</summary>
    public static void Logoff(int sessionId) => Native.WTSLogoffSession(IntPtr.Zero, sessionId, false);

    /// <summary>Starts a program on the user's desktop, running as that user. Requires SYSTEM.</summary>
    public static bool Launch(int sessionId, string exePath)
    {
        if (!File.Exists(exePath)) return false;
        if (!Native.WTSQueryUserToken((uint)sessionId, out var token)) return false;
        var environment = IntPtr.Zero;
        try
        {
            Native.CreateEnvironmentBlock(out environment, token, false);
            var startup = new Native.STARTUPINFO
            {
                cb = Marshal.SizeOf<Native.STARTUPINFO>(),
                lpDesktop = @"winsta0\default",
            };
            var commandLine = new StringBuilder($"\"{exePath}\"");
            var ok = Native.CreateProcessAsUserW(token, null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                environment == IntPtr.Zero ? 0 : Native.CREATE_UNICODE_ENVIRONMENT, environment,
                Path.GetDirectoryName(exePath), ref startup, out var process);
            if (ok)
            {
                Native.CloseHandle(process.hProcess);
                Native.CloseHandle(process.hThread);
            }
            return ok;
        }
        finally
        {
            if (environment != IntPtr.Zero) Native.DestroyEnvironmentBlock(environment);
            Native.CloseHandle(token);
        }
    }
}
