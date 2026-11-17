using System.Globalization;
using System.Windows;
using ScreenLimiter.Core;

namespace ScreenLimiter.Agent;

/// <summary>The small read-only "time left" card that opens from the tray icon.</summary>
public partial class StatusPopup : Window
{
    public StatusPopup()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
    }

    public void Update(StatusDto? status)
    {
        FlowDirection = Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        if (status is null)
        {
            Caption.Text = Loc.T("app.title");
            TimeLeft.Text = "—";
            Detail.Text = Loc.T("status.noservice");
            Bar.Visibility = Visibility.Collapsed;
            return;
        }

        if (!status.Limited)
        {
            Caption.Text = Loc.T("app.title");
            TimeLeft.Text = Loc.T("status.unlimited");
            Detail.Text = "";
            Bar.Visibility = Visibility.Collapsed;
            return;
        }

        Caption.Text = Loc.T(status.Mode switch
        {
            LimitMode.Daily => "status.left.today",
            LimitMode.Weekly => "status.left.week",
            _ => "status.left",
        });
        TimeLeft.Text = status.Allowed ? Loc.Duration(status.RemainingSeconds) : Loc.T("status.timeup");
        Bar.Visibility = Visibility.Visible;
        var used = status.LimitSeconds > 0 ? Math.Clamp((double)status.UsedSeconds / status.LimitSeconds, 0, 1) : 1;
        UsedColumn.Width = new GridLength(used, GridUnitType.Star);
        LeftColumn.Width = new GridLength(1 - used, GridUnitType.Star);
        Detail.Text = Loc.T("status.usedof", Loc.Duration(status.UsedSeconds, roundUp: false), Loc.Duration(status.LimitSeconds));
    }

    /// <summary>Opens in the corner where Windows puts the tray: bottom-left on right-to-left systems.</summary>
    public void ShowNearTray()
    {
        Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        var trayOnLeft = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;
        Left = trayOnLeft ? area.Left : area.Right - ActualWidth;
        Top = area.Bottom - ActualHeight;
        Activate();
    }
}
