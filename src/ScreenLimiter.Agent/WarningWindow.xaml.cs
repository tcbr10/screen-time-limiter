using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ScreenLimiter.Core;

namespace ScreenLimiter.Agent;

/// <summary>
/// "Time's almost up" card above the tray. Drawn by the agent itself rather than as a Windows
/// notification, so Focus Assist or turned-off notifications can't hide it.
/// </summary>
public partial class WarningWindow : Window
{
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(15) };

    public WarningWindow()
    {
        InitializeComponent();
        _hide.Tick += (_, _) => { _hide.Stop(); Hide(); };
    }

    public void ShowWarning(int remainingSeconds)
    {
        FlowDirection = Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        TitleText.Text = Loc.T("warn.title");
        BodyText.Text = Loc.T("warn.body", Loc.Duration(remainingSeconds));

        Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        var trayOnLeft = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;
        Left = trayOnLeft ? area.Left : area.Right - ActualWidth;
        Top = area.Bottom - ActualHeight;

        _hide.Stop();
        _hide.Start();
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        _hide.Stop();
        Hide();
    }
}
