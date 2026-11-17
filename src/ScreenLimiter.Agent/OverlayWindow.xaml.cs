using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using ScreenLimiter.Core;

namespace ScreenLimiter.Agent;

/// <summary>
/// The full-screen "time's up" screen. A parent can type the PIN to add time, or anyone can switch user.
/// Closing or killing it gains nothing: the service then disconnects the session instead.
/// </summary>
public partial class OverlayWindow : Window
{
    private static readonly int[] MinuteOptions = [15, 30, 60];

    public event Action? Granted;

    /// <summary>Set when the session is ending, so the overlay doesn't block sign-out or shutdown.</summary>
    public bool AllowClose { get; set; }

    public OverlayWindow()
    {
        InitializeComponent();
        foreach (var minutes in MinuteOptions) MinutesBox.Items.Add(Loc.Plus(minutes));
        MinutesBox.SelectedIndex = 0;

        Loaded += (_, _) => CoverAllScreens();
        Deactivated += (_, _) =>
        {
            if (!IsVisible) return;
            Topmost = true;
            Activate();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            CoverAllScreens();
            ShowError(null);
            PinBox.Clear();
            PinBox.Focus();
        };
    }

    public void Update(StatusDto status)
    {
        Card.FlowDirection = Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        TitleText.Text = Loc.T(status.Reason switch
        {
            BlockReason.Week => "overlay.title.week",
            BlockReason.Hours => "overlay.title.hours",
            _ => "overlay.title.day",
        });
        BodyText.Text = BodyFor(status);
        PinHint.Text = Loc.T("overlay.pin");
        PinHint.Visibility = PinBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SwitchButton.Content = Loc.T("overlay.switch");
        PinPanel.Visibility = status.PinSet ? Visibility.Visible : Visibility.Collapsed;

        var selected = MinutesBox.SelectedIndex;
        MinutesBox.Items.Clear();
        foreach (var minutes in MinuteOptions) MinutesBox.Items.Add(Loc.Plus(minutes));
        MinutesBox.SelectedIndex = selected < 0 ? 0 : selected;
    }

    /// <summary>"More time starts tomorrow at 00:00." when we know; otherwise who to ask.</summary>
    private static string BodyFor(StatusDto status)
    {
        if (status.OnTimeUp == TimeUpAction.SignOut) return Loc.T("overlay.body.signout");
        if (status.NextAllowedLocal is not { } next) return Loc.T("overlay.body.lock");
        var when = Loc.When(next, status.NowLocal);
        return Loc.T(status.Reason == BlockReason.Hours ? "overlay.next.hours" : "overlay.next", when);
    }

    private void ShowError(string? text)
    {
        ErrorText.Text = text ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PinBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        PinHint.Visibility = PinBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Stretches over every monitor and centres the card on the primary one.</summary>
    private void CoverAllScreens()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        // The primary screen's top-left is (0,0) in virtual-screen coordinates.
        PrimaryArea.Margin = new Thickness(-SystemParameters.VirtualScreenLeft, -SystemParameters.VirtualScreenTop, 0, 0);
        PrimaryArea.Width = SystemParameters.PrimaryScreenWidth;
        PrimaryArea.Height = SystemParameters.PrimaryScreenHeight;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 and friends: stay up. The window is only ever hidden by the app when time is granted.
        // The exception is Windows signing the user out, which must not be held up.
        if (!AllowClose) e.Cancel = true;
        base.OnClosing(e);
    }

    private bool _sending;

    private void PinBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AddTime();
        }
    }

    /// <summary>Checks the PIN with the service and, if it's right, adds the chosen time.</summary>
    private async void AddTime()
    {
        if (_sending) return;
        var pin = PinBox.Password;
        if (string.IsNullOrEmpty(pin))
        {
            ShowError(Loc.T("overlay.err.empty"));
            PinBox.Focus();
            return;
        }

        var index = Math.Clamp(MinutesBox.SelectedIndex, 0, MinuteOptions.Length - 1);
        _sending = true;
        var response = await PipeClient.SendAsync(new Request { Op = Ops.GrantPin, Pin = pin, Minutes = MinuteOptions[index] });
        _sending = false;
        PinBox.Clear();

        if (response.Ok)
        {
            ShowError(null);
            Granted?.Invoke();
            return;
        }

        ShowError(Loc.T(response.Error switch
        {
            "pin_locked" => "overlay.err.locked",
            "no_pin" => "overlay.err.nopin",
            "service_unavailable" => "overlay.err.service",
            _ => "overlay.err.pin",
        }));
        PinBox.Focus();
    }

    private void Switch_Click(object sender, RoutedEventArgs e) => LockWorkStation();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();
}
