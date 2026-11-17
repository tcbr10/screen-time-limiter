using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;
using ScreenLimiter.Core;

namespace ScreenLimiter.Service;

/// <summary>
/// The single owner of settings and usage. Everything that reads or changes them goes through here,
/// under one lock, so the enforcement loop and the pipe server never see half-updated state.
/// </summary>
public sealed class StateManager
{
    private const int MaxPinFailures = 5;
    private static readonly TimeSpan PinLockout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly JsonStore _store;
    private readonly ILogger<StateManager> _log;
    private AppConfig _config;
    private readonly UsageState _usage;
    private bool _usageDirty;
    private long _lastSaveTick = Environment.TickCount64;

    private int _pinFailures;
    private long _pinLockedUntilTick;

    private readonly ConcurrentDictionary<int, (long Tick, bool Overlay)> _agents = new();

    public StateManager(ILogger<StateManager> log)
    {
        _log = log;
        _store = new JsonStore(JsonStore.DefaultDirectory);
        SecureDataDirectory(_store.Directory);
        _config = _store.Load<AppConfig>(JsonStore.ConfigFile);
        _config.Normalize();
        _usage = _store.Load<UsageState>(JsonStore.UsageFile);
        _usage.Users ??= [];
        _config.TimeZoneId ??= TimeZoneInfo.Local.Id;
        // Writes DeviceId and the time zone on first run and proves early that the directory is writable.
        TrySave(JsonStore.ConfigFile, _config);
    }

    // ---- Policies and usage ----

    public UserPolicy? PolicyFor(string sid)
    {
        lock (_gate) return FindPolicy(sid);
    }

    /// <summary>Counts <paramref name="seconds"/> of active screen time, then says whether the user may continue.</summary>
    public Verdict Tick(string sid, double seconds)
    {
        lock (_gate)
        {
            var policy = FindPolicy(sid);
            if (policy is null || policy.Mode == LimitMode.None) return Verdict.Unlimited;
            var usage = RolledOverUsage(sid);
            _usageDirty = true;
            return RulesEngine.CountUsage(policy, usage, seconds, LocalNow, DateTime.UtcNow);
        }
    }

    public Verdict Evaluate(string sid)
    {
        lock (_gate)
        {
            var policy = FindPolicy(sid);
            if (policy is null || policy.Mode == LimitMode.None) return Verdict.Unlimited;
            return RulesEngine.Evaluate(policy, RolledOverUsage(sid), LocalNow, DateTime.UtcNow);
        }
    }

    /// <summary>Local time in the time zone saved with the settings. Call under the lock.</summary>
    private DateTime LocalNow => _config.LocalNow(DateTime.UtcNow);

    public StatusDto Status(string sid)
    {
        lock (_gate)
        {
            var policy = FindPolicy(sid);
            var now = LocalNow;
            var dto = new StatusDto { Language = _config.Language, PinSet = _config.PinHash is not null, NowLocal = now };
            if (policy is null || policy.Mode == LimitMode.None) return dto;

            var usage = RolledOverUsage(sid);
            var verdict = RulesEngine.Evaluate(policy, usage, now, DateTime.UtcNow);
            if (!verdict.Allowed)
            {
                dto.Reason = RulesEngine.ReasonBlocked(policy, usage);
                dto.NextAllowedLocal = RulesEngine.NextAllowed(policy, usage, now, _config.WeekStartsOn);
            }
            dto.Limited = true;
            dto.Allowed = verdict.Allowed;
            dto.RemainingSeconds = verdict.RemainingSeconds;
            dto.LimitSeconds = verdict.LimitSeconds;
            dto.UsedSeconds = verdict.UsedSeconds;
            dto.Mode = policy.Mode;
            dto.OnTimeUp = policy.OnTimeUp;
            dto.WarnMinutes = policy.WarnMinutes;
            return dto;
        }
    }

    /// <summary>Adds extra time. Returns false when the user has no active limit.</summary>
    public bool Grant(string sid, int minutes)
    {
        minutes = Math.Clamp(minutes, 1, 240);
        lock (_gate)
        {
            var policy = FindPolicy(sid);
            if (policy is null || policy.Mode == LimitMode.None) return false;
            RulesEngine.Grant(policy, RolledOverUsage(sid), minutes, LocalNow, DateTime.UtcNow);
            TrySave(JsonStore.UsageFile, _usage);
            _log.LogInformation("Granted {Minutes} extra minutes to {User}", minutes, policy.UserName);
            return true;
        }
    }

    /// <summary>Adds extra time if the PIN is right. Returns null on success, otherwise an error code.</summary>
    public string? GrantWithPin(string sid, string? pin, int minutes)
    {
        lock (_gate)
        {
            if (_config.PinHash is null) return "no_pin";
            if (Environment.TickCount64 < _pinLockedUntilTick) return "pin_locked";
            if (pin is null || !PinHasher.Verify(pin, _config.PinHash))
            {
                if (++_pinFailures >= MaxPinFailures)
                {
                    _pinFailures = 0;
                    _pinLockedUntilTick = Environment.TickCount64 + (long)PinLockout.TotalMilliseconds;
                    _log.LogWarning("Parent PIN entry locked after repeated wrong attempts");
                    return "pin_locked";
                }
                return "wrong_pin";
            }
            _pinFailures = 0;
        }
        return Grant(sid, Math.Clamp(minutes, 1, 120)) ? null : "not_limited";
    }

    // ---- Admin operations ----

    /// <summary>A copy for the admin console. The PIN hash never leaves the service.</summary>
    public AppConfig ConfigForAdmin()
    {
        lock (_gate)
        {
            var copy = Json.Clone(_config);
            copy.PinHash = null;
            return copy;
        }
    }

    public bool PinSet
    {
        get { lock (_gate) return _config.PinHash is not null; }
    }

    public bool SaveConfig(AppConfig incoming)
    {
        incoming.Normalize();
        lock (_gate)
        {
            incoming.PinHash = _config.PinHash;
            incoming.DeviceId = _config.DeviceId;
            incoming.SchemaVersion = _config.SchemaVersion;
            // The console sends the PC's time zone as the admin sees it; keep the old one if it's unusable.
            if (!IsKnownTimeZone(incoming.TimeZoneId)) incoming.TimeZoneId = _config.TimeZoneId;
            if (!TrySave(JsonStore.ConfigFile, incoming)) return false;
            _config = incoming;
            _log.LogInformation("Settings saved for {Count} users", incoming.Users.Count);
            return true;
        }
    }

    public bool SetPin(string pin)
    {
        lock (_gate)
        {
            _config.PinHash = PinHasher.Hash(pin);
            _pinFailures = 0;
            _pinLockedUntilTick = 0;
            return TrySave(JsonStore.ConfigFile, _config);
        }
    }

    // ---- Agent heartbeats ----

    public void ReportAgent(int sessionId, bool overlayShown) =>
        _agents[sessionId] = (Environment.TickCount64, overlayShown);

    public bool AgentSeen(int sessionId, TimeSpan within) =>
        _agents.TryGetValue(sessionId, out var a) && Environment.TickCount64 - a.Tick <= within.TotalMilliseconds;

    public bool OverlayShown(int sessionId, TimeSpan within) =>
        _agents.TryGetValue(sessionId, out var a) && a.Overlay && Environment.TickCount64 - a.Tick <= within.TotalMilliseconds;

    public void ForgetSession(int sessionId) => _agents.TryRemove(sessionId, out _);

    // ---- Persistence ----

    public void FlushIfDue()
    {
        lock (_gate)
        {
            if (!_usageDirty || Environment.TickCount64 - _lastSaveTick < SaveEvery.TotalMilliseconds) return;
            FlushLocked();
        }
    }

    public void Flush()
    {
        lock (_gate) FlushLocked();
    }

    private void FlushLocked()
    {
        if (TrySave(JsonStore.UsageFile, _usage)) _usageDirty = false;
        _lastSaveTick = Environment.TickCount64;
    }

    private bool TrySave<T>(string name, T value)
    {
        try
        {
            _store.Save(name, value);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.LogError(e, "Couldn't write {File}", name);
            return false;
        }
    }

    private static bool IsKnownTimeZone(string? id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return false; }
    }

    private UserPolicy? FindPolicy(string sid) =>
        _config.Users.FirstOrDefault(u => string.Equals(u.Sid, sid, StringComparison.OrdinalIgnoreCase));

    private UserUsage RolledOverUsage(string sid)
    {
        var usage = _usage.Users.FirstOrDefault(u => string.Equals(u.Sid, sid, StringComparison.OrdinalIgnoreCase));
        if (usage is null)
        {
            usage = new UserUsage { Sid = sid };
            _usage.Users.Add(usage);
        }
        RulesEngine.Rollover(usage, DateOnly.FromDateTime(LocalNow), _config.WeekStartsOn);
        return usage;
    }

    /// <summary>Only SYSTEM and administrators may read or write the settings and usage files.</summary>
    private void SecureDataDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            foreach (var sidType in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            {
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sidType, null),
                    FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            }
            new DirectoryInfo(path).SetAccessControl(security);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SystemException)
        {
            _log.LogError(e, "Couldn't lock down {Path}", path);
        }
    }
}
