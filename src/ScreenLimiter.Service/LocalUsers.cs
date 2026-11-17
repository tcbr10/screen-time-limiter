using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace ScreenLimiter.Service;

internal sealed record LocalUser(string Sid, string Name, bool IsAdmin);

/// <summary>Lists the people who have a profile on this PC.</summary>
internal static class LocalUsers
{
    private const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    public static List<LocalUser> List()
    {
        var admins = AdminSids();
        var users = new List<LocalUser>();
        using var key = Registry.LocalMachine.OpenSubKey(ProfileListKey);
        if (key is null) return users;

        foreach (var sid in key.GetSubKeyNames())
        {
            // S-1-5-21-… are local and domain accounts, S-1-12-1-… are Entra ID accounts. Skips service profiles.
            if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) && !sid.StartsWith("S-1-12-1-", StringComparison.Ordinal))
                continue;
            if (sid.Contains('.') || sid.Contains('_')) continue; // leftover ".bak" / "_Classes" entries

            var name = AccountName(sid);
            if (name is null) continue; // the account was deleted but its profile remains
            users.Add(new LocalUser(sid, name, admins.Contains(sid)));
        }
        return users.OrderBy(u => u.IsAdmin).ThenBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string? AccountName(string sid)
    {
        try
        {
            var account = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
            var slash = account.LastIndexOf('\\');
            return slash >= 0 ? account[(slash + 1)..] : account;
        }
        catch (Exception e) when (e is IdentityNotMappedException or ArgumentException or SystemException)
        {
            return null;
        }
    }

    /// <summary>SIDs that are direct members of the local Administrators group.</summary>
    private static HashSet<string> AdminSids()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // The group's display name is translated on non-English Windows, so resolve it from its fixed SID.
            var groupAccount = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
                .Translate(typeof(NTAccount)).Value;
            var groupName = groupAccount[(groupAccount.LastIndexOf('\\') + 1)..];

            if (Native.NetLocalGroupGetMembers(null, groupName, 0, out var buffer, -1, out var read, out _, IntPtr.Zero) != 0)
                return result;
            try
            {
                for (var i = 0; i < read; i++)
                {
                    var sidPointer = Marshal.ReadIntPtr(buffer, i * IntPtr.Size);
                    result.Add(new SecurityIdentifier(sidPointer).Value);
                }
            }
            finally
            {
                Native.NetApiBufferFree(buffer);
            }
        }
        catch (SystemException)
        {
            // Without the list every account shows as a standard user, which only affects a label.
        }
        return result;
    }
}
