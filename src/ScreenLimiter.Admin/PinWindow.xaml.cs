using System.Windows;
using ScreenLimiter.Core;

namespace ScreenLimiter.Admin;

/// <summary>Sets the parent PIN used on the lock screen. The PIN goes straight to the service, which stores only a hash.</summary>
public partial class PinWindow : Window
{
    public PinWindow()
    {
        InitializeComponent();
        FlowDirection = Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Loaded += (_, _) => First.Focus();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!PinHasher.IsValidFormat(First.Password))
        {
            Error.Text = Loc.T("pin.err.format");
            return;
        }
        if (First.Password != Second.Password)
        {
            Error.Text = Loc.T("pin.err.match");
            return;
        }

        var response = await PipeClient.SendAsync(new Request { Op = Ops.SetPin, Pin = First.Password });
        if (!response.Ok)
        {
            Error.Text = Loc.T(response.Error == "service_unavailable" ? "err.service" : "err.save");
            return;
        }
        DialogResult = true;
    }
}
