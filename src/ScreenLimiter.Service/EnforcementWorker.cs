using System.Diagnostics;
using ScreenLimiter.Core;

namespace ScreenLimiter.Service;

/// <summary>
/// The enforcement loop. Every few seconds it counts time for whoever is at an unlocked screen and
/// acts when their time is up. It never relies on the tray agent: the agent only makes things friendlier.
/// </summary>
public sealed class EnforcementWorker(StateManager state, ILogger<EnforcementWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    /// <summary>Longest gap counted as use. Anything longer was sleep or hibernation.</summary>
    private const double MaxCountedGapSeconds = 10;
    private static readonly TimeSpan AgentSilence = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AgentRelaunchEvery = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan OverlayFreshness = TimeSpan.FromSeconds(10);
    /// <summary>How long the agent gets to put up its lock overlay before the session is disconnected.</summary>
    private static readonly TimeSpan LockGrace = TimeSpan.FromSeconds(20);
    /// <summary>How long the "you'll be signed out" overlay shows before sign-out.</summary>
    private static readonly TimeSpan SignOutGrace = TimeSpan.FromSeconds(30);

    private static readonly string AgentPath = Path.Combine(AppContext.BaseDirectory, "ScreenLimiter.Agent.exe");

    private sealed class SessionTrack
    {
        public long? DeniedSinceTick;
        public long LastLaunchTick = long.MinValue / 2;
    }

    private readonly Dictionary<int, SessionTrack> _tracks = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Screen limiter started");
        var clock = Stopwatch.StartNew();
        var last = 0.0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = clock.Elapsed.TotalSeconds;
                var elapsed = Math.Min(now - last, MaxCountedGapSeconds);
                last = now;

                var active = Sessions.Active();
                var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var session in active) Enforce(session, elapsed, counted);

                foreach (var gone in _tracks.Keys.Except(active).ToList())
                {
                    _tracks.Remove(gone);
                    state.ForgetSession(gone);
                }
                state.FlushIfDue();
            }
            catch (Exception e)
            {
                log.LogError(e, "Enforcement tick failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        state.Flush();
    }

    private void Enforce(int session, double elapsed, HashSet<string> counted)
    {
        var sid = Sessions.UserSid(session);
        if (sid is null) return;

        var policy = state.PolicyFor(sid);
        if (policy is null || policy.Mode == LimitMode.None)
        {
            _tracks.Remove(session);
            return;
        }

        if (!_tracks.TryGetValue(session, out var track)) _tracks[session] = track = new SessionTrack();
        var tick = Environment.TickCount64;

        // Keep the agent alive: if it was closed or killed, start it again.
        if (!state.AgentSeen(session, AgentSilence) && tick - track.LastLaunchTick > AgentRelaunchEvery.TotalMilliseconds)
        {
            track.LastLaunchTick = tick;
            if (!Sessions.Launch(session, AgentPath)) log.LogDebug("Couldn't start the agent in session {Session}", session);
        }

        // A locked screen isn't screen time, and there's nothing to enforce behind the lock.
        if (Sessions.IsLocked(session))
        {
            track.DeniedSinceTick = null;
            return;
        }

        // If the same account is somehow active in two sessions, count the time once.
        var verdict = counted.Add(sid) ? state.Tick(sid, elapsed) : state.Evaluate(sid);
        if (verdict.Allowed)
        {
            track.DeniedSinceTick = null;
            return;
        }

        track.DeniedSinceTick ??= tick;
        var deniedFor = TimeSpan.FromMilliseconds(tick - track.DeniedSinceTick.Value);

        if (policy.OnTimeUp == TimeUpAction.SignOut)
        {
            if (deniedFor < SignOutGrace) return;
            log.LogInformation("Time is up for {User}: signing out session {Session}", policy.UserName, session);
            Sessions.Logoff(session);
            track.DeniedSinceTick = null;
        }
        else
        {
            // The agent's full-screen overlay is the polite lock. If it isn't up, fall back to the hard one.
            if (deniedFor < LockGrace || state.OverlayShown(session, OverlayFreshness)) return;
            log.LogInformation("Time is up for {User}: disconnecting session {Session}", policy.UserName, session);
            Sessions.Disconnect(session);
            track.DeniedSinceTick = null;
        }
    }
}
