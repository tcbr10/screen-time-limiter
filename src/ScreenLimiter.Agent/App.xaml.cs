using System.Windows;
using System.Windows.Threading;
using ScreenLimiter.Core;

namespace ScreenLimiter.Agent;

/// <summary>
/// Runs in the limited user's session: tray icon with time left, warnings before the end, and the
/// lock overlay. It decides nothing. The service enforces the limit whether or not this is running.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private System.Windows.Forms.NotifyIcon? _tray;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private StatusPopup? _popup;
    private WarningWindow? _warning;
    private OverlayWindow? _overlay;
    private int _promoteTries;
    private StatusDto? _status;
    private int _previousRemaining = int.MaxValue;
    private bool _polling;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\ScreenLimiter.Agent", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = TrayIcon.Create(),
            Text = Loc.T("app.title"),
            Visible = true,
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left) TogglePopup();
        };

        _timer.Tick += async (_, _) =>
        {
            PromoteTrayIcon();
            await PollAsync();
        };
        _timer.Start();
        _ = PollAsync();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (_overlay is not null) _overlay.AllowClose = true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }

    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var overlayShown = _overlay is { IsVisible: true };
            var response = await PipeClient.SendAsync(new Request { Op = Ops.Status, OverlayShown = overlayShown });
            if (!response.Ok || response.Status is null)
            {
                // Service unreachable: show nothing alarming. The service relaunches us when it's back.
                _status = null;
                SetTrayText(Loc.T("status.noservice"));
                _popup?.Update(null);
                return;
            }

            var status = response.Status;
            _status = status;
            Loc.Language = status.Language;
            _popup?.Update(status);

            if (!status.Limited)
            {
                HideOverlay();
                SetTrayText(Loc.T("status.unlimited"));
                _previousRemaining = int.MaxValue;
                return;
            }

            if (status.Allowed)
            {
                HideOverlay();
                SetTrayText(Loc.T("status.left.short", Loc.Duration(status.RemainingSeconds)));
                WarnIfDue(status);
            }
            else
            {
                SetTrayText(Loc.T("status.timeup"));
                ShowOverlay(status);
            }
            _previousRemaining = status.RemainingSeconds;
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>Shows one notification when the time left crosses a warning threshold.</summary>
    private void WarnIfDue(StatusDto status)
    {
        var crossed = status.WarnMinutes
            .Where(m => m > 0 && status.RemainingSeconds <= m * 60 && _previousRemaining > m * 60)
            .ToList();
        if (crossed.Count == 0) return;

        _warning ??= new WarningWindow();
        _warning.ShowWarning(status.RemainingSeconds);
    }

    /// <summary>
    /// Windows 11 tucks new tray icons into the hidden overflow. A limited user should see their time
    /// at a glance, so the first time Windows lists our icon, ask for it to be shown on the taskbar.
    /// The user can still hide it again; we only set it while it has never been set.
    /// </summary>
    private void PromoteTrayIcon()
    {
        if (_promoteTries > 30) return;
        _promoteTries++;
        try
        {
            using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (root is null) return;
            var exe = Environment.ProcessPath ?? "";
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name, writable: true);
                var path = key?.GetValue("ExecutablePath") as string;
                if (key is null || path is null || !path.EndsWith(System.IO.Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase)) continue;
                if (key.GetValue("IsPromoted") is null) key.SetValue("IsPromoted", 1, Microsoft.Win32.RegistryValueKind.DWord);
                _promoteTries = int.MaxValue;
                return;
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            _promoteTries = int.MaxValue;
        }
    }

    private void ShowOverlay(StatusDto status)
    {
        if (_overlay is null)
        {
            _overlay = new OverlayWindow();
            _overlay.Granted += () => _ = PollAsync();
        }
        _overlay.Update(status);
        if (!_overlay.IsVisible)
        {
            _popup?.Hide();
            _warning?.Hide();
            _overlay.Show();
            _overlay.Activate();
        }
    }

    private void HideOverlay()
    {
        if (_overlay is { IsVisible: true }) _overlay.Hide();
    }

    private void TogglePopup()
    {
        if (_overlay is { IsVisible: true }) return;
        _popup ??= new StatusPopup();
        if (_popup.IsVisible)
        {
            _popup.Hide();
            return;
        }
        _popup.Update(_status);
        _popup.ShowNearTray();
    }

    private void SetTrayText(string text)
    {
        if (_tray is null) return;
        var full = $"{Loc.T("app.title")}: {text}";
        _tray.Text = full.Length > 63 ? full[..63] : full;
    }
}

internal static class TrayIcon
{
    /// <summary>Draws a small clock so the project needs no icon file.</summary>
    public static System.Drawing.Icon Create()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var face = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(37, 99, 235));
            g.FillEllipse(face, 1, 1, 30, 30);
            using var hand = new System.Drawing.Pen(System.Drawing.Color.White, 3f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
            };
            g.DrawLine(hand, 16, 16, 16, 7);
            g.DrawLine(hand, 16, 16, 22, 16);
        }
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }
}
