using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenLimiter.Core;

namespace ScreenLimiter.Admin;

/// <summary>One line in the users list.</summary>
public sealed class UserItem(LocalUserDto dto)
{
    public LocalUserDto Dto { get; } = dto;
    public string Sid => Dto.Sid;
    public string Name => Dto.Name;
    public bool IsAdmin => Dto.IsAdmin;

    public string StatusText
    {
        get
        {
            if (Dto.IsAdmin) return Loc.T("admin.neverlimited");
            var s = Dto.Status;
            if (s is null || !s.Limited) return Loc.T("status.unlimited");
            return s.Allowed ? Loc.T("status.left.short", Loc.Duration(s.RemainingSeconds)) : Loc.T("status.timeup");
        }
    }

    /// <summary>Time left reads in grey, "time's up" in red, "no limit" and admins in light grey.</summary>
    public Brush StatusBrush => Res(Dto.IsAdmin || Dto.Status is not { Limited: true } ? "Muted"
        : Dto.Status.Allowed ? "Secondary" : "Danger");

    public Brush NameBrush => Res(Dto.IsAdmin ? "Muted" : "Text");

    /// <summary>First two letters of the name, like the mockup's "NO" for Noa.</summary>
    public string Initials
    {
        get
        {
            var letters = new string(Name.Where(char.IsLetterOrDigit).Take(2).ToArray());
            return letters.ToUpperInvariant();
        }
    }

    public Brush AvatarBrush => Res(Dto.IsAdmin ? "Control" : "AccentBg");
    public Visibility InitialsVisibility => Dto.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
    public Visibility LockVisibility => Dto.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}

/// <summary>One editable row: a weekday (or "every day") with its minutes and its from–to window.</summary>
public sealed class DayRow : INotifyPropertyChanged
{
    private double _minutes;

    /// <summary>0–6 for a weekday, -1 for the "every day" row.</summary>
    public int DayIndex { get; init; }
    public string Label { get; init; } = "";

    public double Minutes
    {
        get => _minutes;
        set
        {
            _minutes = Math.Round(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Minutes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinutesText)));
        }
    }

    public string MinutesText => _minutes <= 0 ? Loc.T("edit.blocked") : Loc.Duration((int)_minutes * 60);

    public string Start { get; set; } = "";
    public string End { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class MainWindow : Window
{
    private static readonly int[] GrantOptions = [15, 30, 60, 120];
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(10);
    private static readonly int[][] WarnOptions = [[15, 5], [5], []];

    private readonly ObservableCollection<DayRow> _rows = [];
    private AppConfig _config = new();
    private UserPolicy? _current;
    private UserItem? _currentItem;
    /// <summary>True while code is filling the controls, so change handlers don't react to it.</summary>
    private bool _loading = true;
    /// <summary>True while the users list is being replaced by a background refresh.</summary>
    private bool _refreshing;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer = new() { Interval = RefreshEvery };

    public MainWindow()
    {
        InitializeComponent();
        FlowDirection = Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        DailyRows.ItemsSource = _rows;
        HoursRows.ItemsSource = _rows;

        LanguageBox.Items.Add("עברית");
        LanguageBox.Items.Add("English");
        LanguageBox.SelectedIndex = Loc.Language == "he" ? 0 : 1;

        foreach (var minutes in GrantOptions)
        {
            var item = new MenuItem { Header = Loc.Plus(minutes), Tag = minutes };
            item.Click += GrantMenu_Click;
            GrantMenu.Items.Add(item);
        }
        GrantMenu.PlacementTarget = GrantButton;

        // Fit small screens: the window never runs off the top or bottom.
        var area = SystemParameters.WorkArea;
        Height = Math.Min(Height, area.Height - 16);
        MinHeight = Math.Min(MinHeight, Height);

        TimeUpBox.Items.Add(Loc.T("edit.timeup.lock"));
        TimeUpBox.Items.Add(Loc.T("edit.timeup.signout"));
        WarnBox.Items.Add(Loc.T("edit.warn.15_5"));
        WarnBox.Items.Add(Loc.T("edit.warn.5"));
        WarnBox.Items.Add(Loc.T("edit.warn.none"));

        RefreshPanels();
        _loading = false;
        Loaded += async (_, _) =>
        {
            await ReloadAsync(null);
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
        // Keeps the time left in the users list current. Doesn't touch the editor, so unsaved changes stay.
        _refreshTimer.Tick += async (_, _) => await RefreshStatusesAsync();
    }

    // ---- Loading ----

    private async Task ReloadAsync(string? keepSid)
    {
        var response = await PipeClient.SendAsync(new Request { Op = Ops.GetConfig });
        if (!response.Ok || response.Config is null)
        {
            SetServiceState(ok: false, Loc.T(response.Error == "not_admin" ? "err.notadmin" : "err.service"));
            Editor.IsEnabled = false;
            return;
        }

        _config = response.Config;
        _config.Normalize();

        if (_config.Language != Loc.Language)
        {
            Loc.Language = _config.Language;
            Reopen();
            return;
        }

        SetServiceState(ok: true, Loc.T("admin.protection.on"));
        PinState.Text = Loc.T(response.PinSet ? "admin.pinset" : "pin.notset");

        var items = (response.Users ?? []).Select(u => new UserItem(u)).ToList();
        UsersList.ItemsSource = items;
        UsersList.SelectedItem = items.FirstOrDefault(i => i.Sid == keepSid)
                                 ?? items.FirstOrDefault(i => !i.IsAdmin)
                                 ?? items.FirstOrDefault();
    }

    /// <summary>Updates only the users list (time left, time's up). The editor and its unsaved changes are left alone.</summary>
    private async Task RefreshStatusesAsync()
    {
        if (!IsActive && !IsVisible) return;
        var response = await PipeClient.SendAsync(new Request { Op = Ops.GetConfig });
        if (!response.Ok || response.Users is null)
        {
            SetServiceState(ok: false, Loc.T(response.Error == "not_admin" ? "err.notadmin" : "err.service"));
            return;
        }
        SetServiceState(ok: true, Loc.T("admin.protection.on"));
        PinState.Text = Loc.T(response.PinSet ? "admin.pinset" : "pin.notset");

        var keepSid = _currentItem?.Sid;
        var items = response.Users.Select(u => new UserItem(u)).ToList();
        _refreshing = true;
        try
        {
            UsersList.ItemsSource = items;
            UsersList.SelectedItem = items.FirstOrDefault(i => i.Sid == keepSid);
            var selected = UsersList.SelectedItem as UserItem;
            if (selected is not null) _currentItem = selected;
            UpdateBadge();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void SetServiceState(bool ok, string text)
    {
        ServiceState.Text = text;
        var brush = (Brush)FindResource(ok ? "Success" : "Danger");
        ServiceState.Foreground = brush;
        ServiceIcon.Stroke = brush;
        ServiceIcon.Data = (Geometry)FindResource(ok ? "IconShieldCheck" : "IconShieldX");
    }

    /// <summary>"Limit active" in green, "Time's up" in red, nothing when the user has no saved limit.</summary>
    private void UpdateBadge()
    {
        var status = _currentItem?.Dto.Status;
        if (_currentItem is null || _currentItem.IsAdmin || status is not { Limited: true })
        {
            Badge.Visibility = Visibility.Collapsed;
            GrantButton.Visibility = Visibility.Collapsed;
            return;
        }
        Badge.Visibility = Visibility.Visible;
        GrantButton.Visibility = Visibility.Visible;
        BadgeText.Text = Loc.T(status.Allowed ? "admin.badge.active" : "admin.badge.timeup");
        Badge.Background = (Brush)FindResource(status.Allowed ? "SuccessBg" : "DangerBg");
        BadgeText.Foreground = (Brush)FindResource(status.Allowed ? "Success" : "Danger");
    }

    /// <summary>Rebuilds the window so every label picks up the new language and direction.</summary>
    private void Reopen()
    {
        var window = new MainWindow();
        Application.Current.MainWindow = window;
        window.Show();
        Close();
    }

    private void User_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        if (UsersList.SelectedItem is UserItem item) LoadUser(item);
    }

    private void LoadUser(UserItem item)
    {
        _loading = true;
        _currentItem = item;
        var saved = _config.Users.FirstOrDefault(u => string.Equals(u.Sid, item.Sid, StringComparison.OrdinalIgnoreCase));
        _current = saved is null ? new UserPolicy { Sid = item.Sid } : Json.Clone(saved);
        _current.UserName = item.Name;
        _current.Normalize();

        UserName.Text = item.Name;
        Message.Text = "";
        Editor.IsEnabled = true;
        AdminNote.Visibility = item.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        PolicyPanel.Visibility = item.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        UpdateBadge();

        var modeButton = _current.Mode switch
        {
            LimitMode.Daily => ModeDaily,
            LimitMode.Weekly => ModeWeekly,
            LimitMode.AllowedHours => ModeHours,
            _ => ModeNone,
        };
        modeButton.IsChecked = true;

        SameBox.IsChecked = _current.SameForAllDays;
        WeeklySlider.Value = _current.WeeklyMinutes;
        CapSlider.Value = _current.WeeklyDailyCapMinutes;
        TimeUpBox.SelectedIndex = _current.OnTimeUp == TimeUpAction.SignOut ? 1 : 0;
        WarnBox.SelectedIndex = _current.WarnMinutes.Contains(15) ? 0 : _current.WarnMinutes.Length > 0 ? 1 : 2;

        BuildRows();
        _loading = false;
        RefreshPanels();
        UpdateWeeklyText();
    }

    // ---- Rows ----

    private void BuildRows()
    {
        if (_current is null) return;
        _rows.Clear();
        if (_current.SameForAllDays)
        {
            _rows.Add(new DayRow
            {
                DayIndex = -1,
                Label = Loc.T("edit.everyday"),
                Minutes = _current.DailyMinutesAll,
                Start = FormatTime(_current.WindowAll.StartMinute),
                End = FormatTime(_current.WindowAll.EndMinute),
            });
            return;
        }

        for (var i = 0; i < 7; i++)
        {
            var day = ((int)_config.WeekStartsOn + i) % 7;
            _rows.Add(new DayRow
            {
                DayIndex = day,
                Label = Loc.DayName(day),
                Minutes = _current.DailyMinutes[day],
                Start = FormatTime(_current.Windows[day].StartMinute),
                End = FormatTime(_current.Windows[day].EndMinute),
            });
        }
    }

    /// <summary>
    /// Copies the rows back into the working policy. Returns false if a time can't be read;
    /// with <paramref name="strict"/> off, unreadable times are left as they were.
    /// </summary>
    private bool CommitRows(bool strict)
    {
        if (_current is null) return true;
        var readTimes = _current.Mode == LimitMode.AllowedHours;
        foreach (var row in _rows)
        {
            var minutes = (int)row.Minutes;
            var window = row.DayIndex < 0 ? _current.WindowAll : _current.Windows[row.DayIndex];

            if (row.DayIndex < 0) _current.DailyMinutesAll = minutes;
            else _current.DailyMinutes[row.DayIndex] = minutes;

            if (!readTimes) continue;
            if (TryParseTime(row.Start, out var start) && TryParseTime(row.End, out var end) && start < end)
            {
                window.StartMinute = start;
                window.EndMinute = end;
            }
            else if (strict)
            {
                return false;
            }
        }
        return true;
    }

    private static string FormatTime(int minute) => $"{minute / 60:00}:{minute % 60:00}";

    private static bool TryParseTime(string? text, out int minute)
    {
        minute = 0;
        var parts = (text ?? "").Trim().Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        if (h < 0 || m < 0 || m > 59 || h > 24 || (h == 24 && m != 0)) return false;
        minute = h * 60 + m;
        return true;
    }

    // ---- Control events ----

    private LimitMode SelectedMode =>
        ModeDaily.IsChecked == true ? LimitMode.Daily
        : ModeWeekly.IsChecked == true ? LimitMode.Weekly
        : ModeHours.IsChecked == true ? LimitMode.AllowedHours
        : LimitMode.None;

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _current is null) return;
        CommitRows(strict: false);
        _current.Mode = SelectedMode;
        RefreshPanels();
    }

    private void Same_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _current is null) return;
        CommitRows(strict: false);
        _current.SameForAllDays = SameBox.IsChecked == true;
        BuildRows();
    }

    private void Weekly_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateWeeklyText();

    private void UpdateWeeklyText()
    {
        // Fires while the window is still being built, before the text blocks exist.
        if (WeeklyText is null || CapText is null || WeeklySlider is null || CapSlider is null) return;
        WeeklyText.Text = WeeklySlider.Value <= 0 ? Loc.T("edit.blocked") : Loc.Duration((int)WeeklySlider.Value * 60);
        CapText.Text = CapSlider.Value <= 0 ? Loc.T("edit.weekly.nocap") : Loc.Duration((int)CapSlider.Value * 60);
    }

    private void RefreshPanels()
    {
        var mode = _current?.Mode ?? LimitMode.None;
        static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

        SameBox.Visibility = Show(mode is LimitMode.Daily or LimitMode.AllowedHours);
        DailyRows.Visibility = Show(mode == LimitMode.Daily);
        WeeklyPanel.Visibility = Show(mode == LimitMode.Weekly);
        HoursRows.Visibility = Show(mode == LimitMode.AllowedHours);
        OptionsPanel.Visibility = Show(mode != LimitMode.None);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _currentItem is null) return;

        _current.Mode = SelectedMode;
        _current.SameForAllDays = SameBox.IsChecked == true;
        if (!CommitRows(strict: true))
        {
            ShowMessage(Loc.T("edit.err.time"), error: true);
            return;
        }
        _current.WeeklyMinutes = (int)WeeklySlider.Value;
        _current.WeeklyDailyCapMinutes = (int)CapSlider.Value;
        _current.OnTimeUp = TimeUpBox.SelectedIndex == 1 ? TimeUpAction.SignOut : TimeUpAction.Lock;
        _current.WarnMinutes = WarnOptions[Math.Clamp(WarnBox.SelectedIndex, 0, WarnOptions.Length - 1)];

        var updated = Json.Clone(_config);
        TimeZoneInfo.ClearCachedData();
        updated.TimeZoneId = TimeZoneInfo.Local.Id;
        updated.Users.RemoveAll(u => string.Equals(u.Sid, _current.Sid, StringComparison.OrdinalIgnoreCase));
        updated.Users.Add(Json.Clone(_current));

        var response = await PipeClient.SendAsync(new Request { Op = Ops.SaveConfig, Config = updated });
        if (!response.Ok)
        {
            ShowMessage(Loc.T(response.Error == "service_unavailable" ? "err.service" : "err.save"), error: true);
            return;
        }

        var sid = _current.Sid;
        await ReloadAsync(sid);
        ShowMessage(Loc.T("edit.saved"), error: false);
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (_currentItem is not null) LoadUser(_currentItem);
    }

    /// <summary>Opens the "+15 min / +30 min / ..." menu under the button.</summary>
    private void Grant_Click(object sender, RoutedEventArgs e)
    {
        GrantMenu.FlowDirection = FlowDirection;
        GrantMenu.IsOpen = true;
    }

    private async void GrantMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_currentItem is null || sender is not MenuItem { Tag: int minutes }) return;
        var response = await PipeClient.SendAsync(new Request { Op = Ops.Grant, Sid = _currentItem.Sid, Minutes = minutes });
        if (!response.Ok)
        {
            // "not_limited": there's no saved limit to extend yet.
            ShowMessage(Loc.T(response.Error == "service_unavailable" ? "err.service" : "status.unlimited"), error: true);
            return;
        }
        await ReloadAsync(_currentItem.Sid);
        ShowMessage(Loc.T("edit.added", Loc.Duration(minutes * 60)), error: false);
    }

    private async void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var language = LanguageBox.SelectedIndex == 0 ? "he" : "en";
        if (language == Loc.Language) return;

        var updated = Json.Clone(_config);
        updated.Language = language;
        var response = await PipeClient.SendAsync(new Request { Op = Ops.SaveConfig, Config = updated });
        if (!response.Ok)
        {
            ShowMessage(Loc.T("err.save"), error: true);
            return;
        }
        Loc.Language = language;
        Reopen();
    }

    private async void Pin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PinWindow { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await ReloadAsync(_currentItem?.Sid);
            ShowMessage(Loc.T("pin.saved"), error: false);
        }
    }

    // ---- Title bar ----

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Max_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // A maximized borderless window hangs a few pixels past the screen; pull the content back in.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = FindResource(WindowState == WindowState.Maximized ? "IconRestore" : "IconMax");
    }

    private void ShowMessage(string text, bool error)
    {
        Message.Text = text;
        Message.Foreground = (Brush)FindResource(error ? "Danger" : "Success");
    }
}
