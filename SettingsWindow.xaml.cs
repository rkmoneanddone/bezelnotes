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
        var cache =
            new NotificationCacheService();

        NotificationCacheEnvelope? cached =
            cache.Load();

        if (!cache.IsRefreshDue(
                DateTimeOffset.UtcNow))
        {
            ApplyBackendNotification(
                cached?.Notification);
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
                response.Notification,
                response.CheckedAtUtc == default
                    ? DateTimeOffset.UtcNow
                    : response.CheckedAtUtc);

            ApplyBackendNotification(
                response.Notification);
        }
        catch
        {
            ApplyBackendNotification(
                cached?.Notification);
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
                effective.DaysRemaining);
            ApplyBackendNotification(
                state.Notification);
            await RefreshNotificationIfDueAsync(
                refreshed,
                config);
        }
        catch
        {
            _secureSessionService.Clear();
            ShowLoggedOutState();
        }
    }

    private void ApplyBackendNotification(
        BackendNotification? notification)
    {
        if (notification is null)
        {
            HideBackendNotification();
            return;
        }

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        if (notification.StartAtUtc.HasValue &&
            notification.StartAtUtc.Value > now)
        {
            HideBackendNotification();
            return;
        }

        if (notification.ExpiresAtUtc.HasValue &&
            notification.ExpiresAtUtc.Value < now)
        {
            HideBackendNotification();
            return;
        }

        string title =
            string.IsNullOrWhiteSpace(
                notification.Title)
                ? "Bezel Sticky Notes"
                : notification.Title.Trim();

        string message =
            notification.Message?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(message))
        {
            HideBackendNotification();
            return;
        }

        BackendNotificationTitle.Text =
            title;

        BackendNotificationMessage.Text =
            message;

        switch (
            notification.Type?.Trim().ToLowerInvariant())
        {
            case "critical":
            case "error":
                BackendNotificationCard.Background =
                    new SolidColorBrush(
                        Color.FromRgb(254, 242, 242));
                BackendNotificationCard.BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(254, 202, 202));
                BackendNotificationAccent.Background =
                    new SolidColorBrush(
                        Color.FromRgb(185, 28, 28));
                break;

            case "warning":
                BackendNotificationCard.Background =
                    new SolidColorBrush(
                        Color.FromRgb(255, 248, 231));
                BackendNotificationCard.BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(253, 230, 138));
                BackendNotificationAccent.Background =
                    new SolidColorBrush(
                        Color.FromRgb(217, 119, 6));
                break;

            case "success":
                BackendNotificationCard.Background =
                    new SolidColorBrush(
                        Color.FromRgb(240, 253, 244));
                BackendNotificationCard.BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(187, 247, 208));
                BackendNotificationAccent.Background =
                    new SolidColorBrush(
                        Color.FromRgb(22, 163, 74));
                break;

            default:
                BackendNotificationCard.Background =
                    new SolidColorBrush(
                        Color.FromRgb(239, 246, 255));
                BackendNotificationCard.BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(191, 219, 254));
                BackendNotificationAccent.Background =
                    new SolidColorBrush(
                        Color.FromRgb(37, 99, 235));
                break;
        }

        BackendNotificationCard.Visibility =
            Visibility.Visible;
    }

    private void HideBackendNotification()
    {
        BackendNotificationTitle.Text =
            string.Empty;

        BackendNotificationMessage.Text =
            string.Empty;

        BackendNotificationCard.Visibility =
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
        HideBackendNotification();
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
        int trialDaysRemaining)
    {
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

    private void ShowLoggedOutState()
    {
        HideBackendNotification();
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