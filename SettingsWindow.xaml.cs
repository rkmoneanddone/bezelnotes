using Microsoft.Win32;
using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StickyNotes.Core.Models;
using StickyNotes.Services;

namespace StickyNotes;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly SecureSessionService _secureSessionService = new();
    private readonly DispatcherTimer _savedTimer;

    private AppSettings _settings = new();
    private bool _accountStateReady;

    public event Action<AppSettings>? SettingsSaved;

    public SettingsWindow()
    {
        InitializeComponent();

        _savedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };

        _savedTimer.Tick += (_, _) =>
        {
            _savedTimer.Stop();
            SaveStatusText.Text = string.Empty;
        };

        LoadSettings();
        LoadVersion();
    }

    protected override void OnContentRendered(
        EventArgs e)
    {
        base.OnContentRendered(e);

        if (!_accountStateReady)
        {
            _accountStateReady = true;
            _ = InitializeAccountAndStartupAsync();
        }
    }

    private void LoadSettings()
    {
        _settings = _settingsService.Load();

        StartWithWindowsCheckBox.IsChecked =
            _settings.StartWithWindows;

        SelectComboValue(
            DefaultColorComboBox,
            _settings.DefaultColor);
    }

    private void LoadVersion()
    {
        Version? version =
            Assembly.GetExecutingAssembly()
                .GetName()
                .Version;

        VersionText.Text =
            version is null
                ? "Version unknown"
                : $"Version {version.Major}.{version.Minor}.{version.Build}";
    }

    private static void SelectComboValue(
        ComboBox comboBox,
        string value)
    {
        foreach (ComboBoxItem item
                 in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(
                    item.Content?.ToString(),
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private static string GetComboValue(
        ComboBox comboBox,
        string fallback)
    {
        return comboBox.SelectedItem
            is ComboBoxItem item
                ? item.Content?.ToString()
                  ?? fallback
                : fallback;
    }

    private void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _settings.StartWithWindows =
            StartWithWindowsCheckBox.IsChecked == true;

        _settings.DefaultColor =
            GetComboValue(
                DefaultColorComboBox,
                "Yellow");

        _settingsService.Save(_settings);

        ApplyStartWithWindows(
            _settings.StartWithWindows);

        SettingsSaved?.Invoke(_settings);

        SaveStatusText.Text = "Saved";

        _savedTimer.Stop();
        _savedTimer.Start();
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private void ExitButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private async void AccountButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_secureSessionService.HasSavedSession)
        {
            _secureSessionService.Clear();
            new NotificationCacheService().Clear();
            new BackendBootstrapCacheService().Clear();
            ShowLoggedOutState();
            return;
        }

        var window =
            new AccountWindow
            {
                Owner = this
            };

        window.ShowDialog();

        ShowAccountLoadingState();
        await RestoreSavedAccountStateAsync();
    }

    private void PremiumButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Premium checkout is not connected yet. Your current trial remains active.",
            "Bezel Sticky Notes Premium",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void AboutButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window =
            new AboutWindow
            {
                Owner = this
            };

        window.ShowDialog();
    }

    private async Task InitializeAccountAndStartupAsync()
    {
        ShowAccountLoadingState();
        EnsureStartupEnabled();
        await RestoreSavedAccountStateAsync();
    }

    private async Task RefreshNotificationIfDueAsync(
        AuthSession session,
        FirebaseClientConfig config)
    {
        ShowNotificationLoadingState();
        var cache =
            new NotificationCacheService();

        NotificationCacheEnvelope? cached =
            cache.Load();

        if (!cache.IsRefreshDue(
                DateTimeOffset.UtcNow))
        {
            ApplyBackendNotifications(
                cached?.Notifications);
            return;
        }

        try
        {
            var backend =
                new SecureBackendService(config);

            NotificationRefreshResponse response =
                await backend.GetActiveNotificationAsync(
                    session);

            cache.Save(
                response.Notifications,
                response.CheckedAtUtc == default
                    ? DateTimeOffset.UtcNow
                    : response.CheckedAtUtc);

            ApplyBackendNotifications(
                response.Notifications);
        }
        catch
        {
            if (cached is not null)
            {
                ApplyBackendNotifications(
                    cached.Notifications);
            }
            else
            {
                ShowNotificationErrorState();
            }
        }
    }
    private async Task RestoreSavedAccountStateAsync()
    {
        PersistedSession? persisted =
            _secureSessionService.Load();

        if (persisted == null)
        {
            ShowLoggedOutState();
            return;
        }

        try
        {
            FirebaseClientConfig config =
                new FirebaseClientConfigService().Load();

            var auth =
                new FirebaseAuthService(config);

            AuthSession refreshed =
                await auth.RefreshSessionAsync(
                    persisted);

            _secureSessionService.Save(
                refreshed);

            var backend =
                new SecureBackendService(config);

            BackendAccountState state =
                await backend.GetBootstrapStateAsync(
                    refreshed);

            var effective =
                GetEffectiveAccountState(
                    state);

            ApplySignedInAccountState(
                state.Email,
                effective.State,
                effective.DaysRemaining,
                state.PlanCode);

            await RefreshNotificationIfDueAsync(
                refreshed,
                config);
        }
        catch (SavedSessionInvalidException)
        {
            _secureSessionService.Clear();
            new NotificationCacheService().Clear();
            new BackendBootstrapCacheService().Clear();
            ShowLoggedOutState();
        }
        catch
        {
            BackendAccountState? cached =
                new BackendBootstrapCacheService()
                    .LoadFresh(
                        persisted.UserId,
                        DateTimeOffset.UtcNow);

            if (cached is not null)
            {
                var effective =
                    GetEffectiveAccountState(
                        cached);

                ApplySignedInAccountState(
                    string.IsNullOrWhiteSpace(cached.Email)
                        ? persisted.Email
                        : cached.Email,
                    effective.State,
                    effective.DaysRemaining,
                    cached.PlanCode);
            }
            else
            {
                ShowAccountOfflineState(
                    persisted.Email);
            }

            NotificationCacheEnvelope? notificationCache =
                new NotificationCacheService().Load();

            if (notificationCache is not null)
            {
                ApplyBackendNotifications(
                    notificationCache.Notifications);
            }
            else
            {
                ShowNotificationErrorState();
            }
        }
    }
private void ShowNotificationLoadingState()
    {
        BackendNotificationsPanel.Children.Clear();
        BackendNotificationsPanel.Visibility =
            Visibility.Collapsed;

        NotificationStatusText.Text =
            "Loading...";
        NotificationStatusText.Visibility =
            Visibility.Visible;
    }

    private void ShowNotificationErrorState()
    {
        BackendNotificationsPanel.Children.Clear();
        BackendNotificationsPanel.Visibility =
            Visibility.Collapsed;

        NotificationStatusText.Text =
            "Unable to load notifications";
        NotificationStatusText.Visibility =
            Visibility.Visible;
    }
    private void ApplyBackendNotifications(
        IReadOnlyList<BackendNotification>? notifications)
    {
        BackendNotificationsPanel.Children.Clear();

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        var active =
            (notifications ??
                Array.Empty<BackendNotification>())
            .Where(notification =>
                (!notification.StartAtUtc.HasValue ||
                    notification.StartAtUtc.Value <= now) &&
                (!notification.ExpiresAtUtc.HasValue ||
                    notification.ExpiresAtUtc.Value >= now) &&
                !string.IsNullOrWhiteSpace(
                    notification.Message))
            .ToList();

        if (active.Count == 0)
        {
            BackendNotificationsPanel.Visibility =
                Visibility.Collapsed;

            NotificationStatusText.Text =
                "Nothing to show";
            NotificationStatusText.Visibility =
                Visibility.Visible;
            return;
        }

        NotificationStatusText.Visibility =
            Visibility.Collapsed;

        foreach (BackendNotification notification in active)
        {
            BackendNotificationsPanel.Children.Add(
                BuildBackendNotificationCard(
                    notification));
        }

        BackendNotificationsPanel.Visibility =
            Visibility.Visible;
    }

    private Border BuildBackendNotificationCard(
        BackendNotification notification)
    {
        string type =
            string.IsNullOrWhiteSpace(notification.Type)
                ? "info"
                : notification.Type.Trim().ToLowerInvariant();

        Color accent;
        Color background;
        Color border;

        switch (type)
        {
            case "critical":
            case "error":
                accent = Color.FromRgb(185, 28, 28);
                background = Color.FromRgb(254, 242, 242);
                border = Color.FromRgb(254, 202, 202);
                break;

            case "warning":
                accent = Color.FromRgb(217, 119, 6);
                background = Color.FromRgb(255, 248, 231);
                border = Color.FromRgb(253, 230, 138);
                break;

            case "success":
                accent = Color.FromRgb(22, 163, 74);
                background = Color.FromRgb(240, 253, 244);
                border = Color.FromRgb(187, 247, 208);
                break;

            case "update":
                accent = Color.FromRgb(37, 99, 235);
                background = Color.FromRgb(239, 246, 255);
                border = Color.FromRgb(191, 219, 254);
                break;

            default:
                type = "info";
                accent = Color.FromRgb(37, 99, 235);
                background = Color.FromRgb(239, 246, 255);
                border = Color.FromRgb(191, 219, 254);
                break;
        }

        var title =
            new TextBlock
            {
                Text =
                    string.IsNullOrWhiteSpace(notification.Title)
                        ? "Bezel Sticky Notes"
                        : notification.Title.Trim(),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };

        var badge =
            new Border
            {
                Background =
                    new SolidColorBrush(
                        Color.FromArgb(
                            26,
                            accent.R,
                            accent.G,
                            accent.B)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child =
                    new TextBlock
                    {
                        Text =
                            char.ToUpperInvariant(type[0]) +
                            type[1..],
                        FontSize = 11.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground =
                            new SolidColorBrush(accent)
                    }
            };

        var header =
            new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

        header.Children.Add(title);
        header.Children.Add(badge);

        var message =
            new TextBlock
            {
                Text = notification.Message.Trim(),
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 13,
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(85, 91, 100)),
                TextWrapping = TextWrapping.Wrap
            };

        var content =
            new StackPanel
            {
                Margin = new Thickness(10, 0, 0, 0)
            };

        content.Children.Add(header);
        content.Children.Add(message);

        var grid =
            new Grid();

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(4)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        var accentBar =
            new Border
            {
                Background =
                    new SolidColorBrush(accent),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 1, 0, 1)
            };

        Grid.SetColumn(accentBar, 0);
        Grid.SetColumn(content, 1);

        grid.Children.Add(accentBar);
        grid.Children.Add(content);

        return new Border
        {
            Background =
                new SolidColorBrush(background),
            BorderBrush =
                new SolidColorBrush(border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 9),
            Child = grid
        };
    }

    private void HideBackendNotifications()
    {
        BackendNotificationsPanel.Children.Clear();
        BackendNotificationsPanel.Visibility =
            Visibility.Collapsed;
    }
    private static (string State, int DaysRemaining)
        GetEffectiveAccountState(
            BackendAccountState state)
    {
        string effectiveState =
            state.EntitlementState;

        int daysRemaining =
            state.TrialDaysRemaining;

        if (string.Equals(
                effectiveState,
                "trial",
                StringComparison.OrdinalIgnoreCase) &&
            state.TrialEndsAtUtc.HasValue)
        {
            TimeSpan remaining =
                state.TrialEndsAtUtc.Value -
                DateTimeOffset.UtcNow;

            if (remaining <= TimeSpan.Zero)
            {
                return ("expired", 0);
            }

            daysRemaining =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        remaining.TotalDays));
        }

        return (
            effectiveState,
            Math.Max(0, daysRemaining));
    }

    private void ShowAccountLoadingState()
    {
        HideBackendNotifications();
        AccountEmailText.Text =
            "Checking account...";

        EntitlementStatusText.Text =
            "Checking...";

        EntitlementStatusText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    107,
                    114,
                    128));

        AccountActionButton.Content =
            "Loading...";

        AccountActionButton.IsEnabled =
            false;
    }
    public void ApplySignedInAccountState(
        string email,
        string entitlementState,
        int trialDaysRemaining,
        string planCode = "none")
    {
        PlanSummaryText.Text =
            planCode.ToLowerInvariant() switch
            {
                "monthly" => "Monthly plan",
                "yearly" => "Yearly plan",
                _ when string.Equals(
                    entitlementState,
                    "active",
                    StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        entitlementState,
                        "grace",
                        StringComparison.OrdinalIgnoreCase)
                    => "Premium plan",
                _ => "7-day free trial"
            };
AccountActionButton.IsEnabled =
            true;
        AccountEmailText.Text =
            $"Signed in as {email}";

        AccountActionButton.Content =
            "Logout";

        string dayWord =
            trialDaysRemaining == 1
                ? "day"
                : "days";

        switch (entitlementState)
        {
            case "trial":
                EntitlementStatusText.Text =
                    $"Trial active - {trialDaysRemaining} {dayWord} left";

                EntitlementStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            31,
                            122,
                            66));
                break;

            case "active":
                EntitlementStatusText.Text =
                    "Premium active";

                EntitlementStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            31,
                            122,
                            66));
                break;

            case "grace":
                EntitlementStatusText.Text =
                    "Payment grace period";

                EntitlementStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            180,
                            104,
                            24));
                break;

            case "expired":
                EntitlementStatusText.Text =
                    "Trial expired";

                EntitlementStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            180,
                            48,
                            48));
                break;

            default:
                EntitlementStatusText.Text =
                    "Account verified";

                EntitlementStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            75,
                            85,
                            99));
                break;
        }
    }

        private void ShowAccountOfflineState(
        string email)
    {
        PlanSummaryText.Text =
            "Account status unavailable";

        AccountActionButton.IsEnabled =
            true;

        AccountEmailText.Text =
            string.IsNullOrWhiteSpace(email)
                ? "Saved account"
                : $"Signed in as {email}";

        EntitlementStatusText.Text =
            "Connection unavailable";

        EntitlementStatusText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    180,
                    104,
                    24));

        AccountActionButton.Content =
            "Logout";
    }
private void ShowLoggedOutState()
    {
        PlanSummaryText.Text = "7-day free trial";
        HideBackendNotifications();
        AccountActionButton.IsEnabled =
            true;
        AccountEmailText.Text =
            "Trial starts when your account is activated.";

        EntitlementStatusText.Text =
            "Not started";

        EntitlementStatusText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    107,
                    114,
                    128));

        AccountActionButton.Content =
            "Sign in / Create account";
    }

    private void EnsureStartupEnabled()
    {
        try
        {
            const string keyPath =
                @"Software\Microsoft\Windows\CurrentVersion\Run";

            const string valueName =
                "BezelStickyNotes";

            using RegistryKey? runKey =
                Registry.CurrentUser.OpenSubKey(
                    keyPath,
                    writable: true);

            string exePath =
                Environment.ProcessPath
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(
                    exePath))
            {
                return;
            }

            runKey?.SetValue(
                valueName,
                $"\"{exePath}\"");

            StartWithWindowsCheckBox.IsChecked =
                true;
        }
        catch
        {
        }
    }

    private static void ApplyStartWithWindows(
        bool enabled)
    {
        const string keyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";

        const string valueName =
            "BezelStickyNotes";

        using RegistryKey? key =
            Registry.CurrentUser.OpenSubKey(
                keyPath,
                writable: true);

        if (key is null)
        {
            return;
        }

        if (!enabled)
        {
            key.DeleteValue(
                valueName,
                throwOnMissingValue: false);

            return;
        }

        string? executablePath =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(
                executablePath))
        {
            return;
        }

        key.SetValue(
            valueName,
            $"\"{executablePath}\"");
    }
}