using Microsoft.Win32;
using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using StickyNotes.Core.Models;
using StickyNotes.Services;

namespace StickyNotes;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private AppSettings _settings = new();
    private readonly DispatcherTimer _savedTimer;
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

    private void LoadSettings()
    {
        _settings = _settingsService.Load();
        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        SelectComboValue(DefaultColorComboBox, _settings.DefaultColor);
    }

    private void LoadVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null
            ? "Version unknown"
            : $"Version {version.Major}.{version.Minor}.{version.Build}";
    }

    private static void SelectComboValue(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
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

    private static string GetComboValue(ComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? fallback
            : fallback;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        _settings.DefaultColor = GetComboValue(DefaultColorComboBox, "Yellow");

        _settingsService.Save(_settings);
        ApplyStartWithWindows(_settings.StartWithWindows);
        SettingsSaved?.Invoke(_settings);

        SaveStatusText.Text = "Saved";
        _savedTimer.Stop();
        _savedTimer.Start();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
    private static void ApplyStartWithWindows(bool enabled)
    {
        const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "BezelStickyNotes";

        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);

        if (key is null)
        {
            return;
        }

        if (!enabled)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        key.SetValue(valueName, $"\"{executablePath}\"");
    }
    private void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new AccountWindow
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private static string? _signedInEmail;
    private static string? _signedInEntitlementState;
    private static int _signedInTrialDaysRemaining;

    public void ApplySignedInAccountState(
        string email,
        string entitlementState,
        int trialDaysRemaining)
    {
        _signedInEmail = email;
        _signedInEntitlementState = entitlementState;
        _signedInTrialDaysRemaining = trialDaysRemaining;

        ApplyAccountStateToVisualTree();
        RefreshAccountActionButton();
        EmphasizeTrialStatus();
    }

    protected override void OnContentRendered(
        EventArgs e)
    {
        _ = ApplyCompactAccountSettingsRuntimeFix();
        ApplySettingsPremiumAndAboutEnhancements();
        _ = InitializeAccountAndStartupAsync();
        base.OnContentRendered(e);

        if (!string.IsNullOrWhiteSpace(_signedInEmail))
        {
            ApplyAccountStateToVisualTree();
        }
    }

    private void ApplyAccountStateToVisualTree()
    {
        if (string.IsNullOrWhiteSpace(_signedInEmail))
        {
            return;
        }

        string statusText =
            _signedInEntitlementState switch
            {
                "trial" =>
                    $"Trial active - {_signedInTrialDaysRemaining} day(s) remaining",

                "active" =>
                    "Subscription active",

                "grace" =>
                    "Subscription grace period",

                "expired" =>
                    "Trial or subscription expired",

                _ =>
                    "Account verified"
            };

        foreach (System.Windows.DependencyObject item
                 in EnumerateVisualTree(this))
        {
            if (item is System.Windows.Controls.TextBlock textBlock)
            {
                string text =
                    textBlock.Text ?? string.Empty;

                if (text.Contains(
                        "Trial starts when your account is activated.",
                        StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text =
                        $"Signed in as {_signedInEmail}";

                    textBlock.Foreground =
                        new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                55,
                                65,
                                70));
                }
                else if (text.Equals(
                             "Not started",
                             StringComparison.OrdinalIgnoreCase) ||
                         text.Contains(
                             "day(s) remaining",
                             StringComparison.OrdinalIgnoreCase) ||
                         text.Equals(
                             "Subscription active",
                             StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text =
                        statusText;

                    textBlock.Foreground =
                        new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                31,
                                92,
                                52));

                    if (textBlock.Parent
                        is System.Windows.Controls.Border parentBorder)
                    {
                        parentBorder.Background =
                            new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(
                                    229,
                                    245,
                                    234));

                        parentBorder.BorderBrush =
                            new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(
                                    176,
                                    216,
                                    187));
                    }
                }
            }
            else if (item is System.Windows.Controls.Button button)
            {
                string content =
                    button.Content?.ToString() ?? string.Empty;

                if (content.Contains(
                        "Sign in / Create account",
                        StringComparison.OrdinalIgnoreCase))
                {
                    button.Content =
                        "Account details";
                }
            }
        }
    }

    private static IEnumerable<System.Windows.DependencyObject>
        EnumerateVisualTree(
            System.Windows.DependencyObject root)
    {
        if (root == null)
        {
            yield break;
        }

        int count =
            System.Windows.Media.VisualTreeHelper.GetChildrenCount(
                root);

        for (int i = 0; i < count; i++)
        {
            System.Windows.DependencyObject child =
                System.Windows.Media.VisualTreeHelper.GetChild(
                    root,
                    i);

            yield return child;

            foreach (System.Windows.DependencyObject descendant
                     in EnumerateVisualTree(child))
            {
                yield return descendant;
            }
        }
    }

    private bool _accountStateReady;
    private readonly SecureSessionService _secureSessionService = new();

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
                new FirebaseAuthService(
                    config);

            AuthSession refreshed =
                await auth.RefreshSessionAsync(
                    persisted);

            _secureSessionService.Save(
                refreshed);

            var backend =
                new SecureBackendService(
                    config);

            BackendAccountState state =
                await backend.BootstrapAccountAsync(
                    refreshed);

            ApplySignedInAccountState(
                state.Email,
                state.EntitlementState,
                state.TrialDaysRemaining);
        }
        catch
        {
            _secureSessionService.Clear();
            ShowLoggedOutState();
        }
    }

    private void ShowLoggedOutState()
    {
        _signedInEmail = null;
        _signedInEntitlementState = null;
        _signedInTrialDaysRemaining = 0;

        foreach (System.Windows.DependencyObject item
                 in EnumerateVisualTree(this))
        {
            if (item is System.Windows.Controls.TextBlock textBlock)
            {
                string text =
                    textBlock.Text ?? string.Empty;

                if (text.StartsWith(
                        "Signed in as ",
                        StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text =
                        "Trial starts when your account is activated.";
                }
                else if (
                    text.Contains(
                        "Trial active",
                        StringComparison.OrdinalIgnoreCase) ||
                    text.Contains(
                        "Subscription active",
                        StringComparison.OrdinalIgnoreCase) ||
                    text.Contains(
                        "grace",
                        StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text =
                        "Not started";
                }
            }
            else if (item is System.Windows.Controls.Button button)
            {
                string content =
                    button.Content?.ToString()
                    ?? string.Empty;

                if (content.Equals(
                        "Logout",
                        StringComparison.OrdinalIgnoreCase) ||
                    content.Equals(
                        "Account details",
                        StringComparison.OrdinalIgnoreCase))
                {
                    button.Content =
                        "Sign in / Create account";

                    button.Click -= LogoutButton_Click;
                }
            }
        }
    }

    private void LogoutButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _secureSessionService.Clear();
        ShowLoggedOutState();
    }

    private void EnsureStartupEnabled()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? runKey =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run",
                    writable: true);

            string exePath =
                Environment.ProcessPath
                ?? System.Diagnostics.Process
                    .GetCurrentProcess()
                    .MainModule?
                    .FileName
                ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(
                    exePath))
            {
                runKey?.SetValue(
                    "BezelStickyNotes",
                    $"\"{exePath}\"");
            }

            foreach (System.Windows.DependencyObject item
                     in EnumerateVisualTree(this))
            {
                if (item is System.Windows.Controls.CheckBox checkBox)
                {
                    string content =
                        checkBox.Content?.ToString()
                        ?? string.Empty;

                    if (content.Contains(
                            "Start StickyNotes with Windows",
                            StringComparison.OrdinalIgnoreCase) ||
                        content.Contains(
                            "Start Bezel Sticky Notes with Windows",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        checkBox.IsChecked =
                            true;

                        checkBox.Checked -=
                            StartupCheckBox_Checked;

                        checkBox.Unchecked -=
                            StartupCheckBox_Unchecked;

                        checkBox.Checked +=
                            StartupCheckBox_Checked;

                        checkBox.Unchecked +=
                            StartupCheckBox_Unchecked;
                    }
                }
            }
        }
        catch
        {
        }
    }

    private void StartupCheckBox_Checked(
        object sender,
        RoutedEventArgs e)
    {
        EnsureStartupEnabled();
    }

    private void StartupCheckBox_Unchecked(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            using Microsoft.Win32.RegistryKey? runKey =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run",
                    writable: true);

            runKey?.DeleteValue(
                "BezelStickyNotes",
                throwOnMissingValue: false);
        }
        catch
        {
        }
    }

    private async Task InitializeAccountAndStartupAsync()
    {
        if (_accountStateReady)
        {
            return;
        }

        _accountStateReady =
            true;

        EnsureStartupEnabled();

        await RestoreSavedAccountStateAsync();
    }

    private bool _compactAccountSettingsRuntimeFixApplied;

    private async Task ApplyCompactAccountSettingsRuntimeFix()
    {
        if (_compactAccountSettingsRuntimeFixApplied)
        {
            return;
        }

        _compactAccountSettingsRuntimeFixApplied = true;

        // Keep this utility window genuinely compact.
        Width = 430;
        MinWidth = 430;
        MaxWidth = 430;
        Height = 590;
        ResizeMode = ResizeMode.NoResize;

        await Dispatcher.InvokeAsync(
            () =>
            {
                CompactSettingsVisualTree();
                RefreshAccountActionButton();
                EmphasizeTrialStatus();
            });
    }

    private void CompactSettingsVisualTree()
    {
        foreach (DependencyObject item
                 in EnumerateVisualTree(this))
        {
            if (item is Border border)
            {
                Thickness p = border.Padding;

                double horizontal =
                    Math.Min(
                        Math.Max(p.Left, p.Right),
                        10);

                double vertical =
                    Math.Min(
                        Math.Max(p.Top, p.Bottom),
                        8);

                border.Padding =
                    new Thickness(
                        horizontal,
                        vertical,
                        horizontal,
                        vertical);

                Thickness m = border.Margin;

                border.Margin =
                    new Thickness(
                        Math.Min(m.Left, 8),
                        Math.Min(m.Top, 7),
                        Math.Min(m.Right, 8),
                        Math.Min(m.Bottom, 7));
            }
            else if (item is StackPanel stack)
            {
                Thickness m = stack.Margin;

                stack.Margin =
                    new Thickness(
                        Math.Min(m.Left, 8),
                        Math.Min(m.Top, 6),
                        Math.Min(m.Right, 8),
                        Math.Min(m.Bottom, 6));
            }
            else if (item is Grid grid)
            {
                Thickness m = grid.Margin;

                grid.Margin =
                    new Thickness(
                        Math.Min(m.Left, 8),
                        Math.Min(m.Top, 6),
                        Math.Min(m.Right, 8),
                        Math.Min(m.Bottom, 6));
            }
        }
    }

    private Button? FindAccountActionButton()
    {
        foreach (DependencyObject item
                 in EnumerateVisualTree(this))
        {
            if (item is not Button button)
            {
                continue;
            }

            string text =
                button.Content?.ToString()
                ?? string.Empty;

            if (text.Equals(
                    "Account details",
                    StringComparison.OrdinalIgnoreCase) ||
                text.Equals(
                    "Logout",
                    StringComparison.OrdinalIgnoreCase) ||
                text.Contains(
                    "Sign in / Create account",
                    StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }
        }

        return null;
    }

    private void RefreshAccountActionButton()
    {
        Button? button =
            FindAccountActionButton();

        if (button == null)
        {
            return;
        }

        button.PreviewMouseLeftButtonDown -=
            AccountActionButton_PreviewMouseLeftButtonDown;

        if (_secureSessionService.HasSavedSession)
        {
            button.Content = "Logout";

            button.PreviewMouseLeftButtonDown +=
                AccountActionButton_PreviewMouseLeftButtonDown;
        }
        else
        {
            button.Content =
                "Sign in / Create account";
        }
    }

    private void AccountActionButton_PreviewMouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_secureSessionService.HasSavedSession)
        {
            return;
        }

        // Stop the older login Click handler from running.
        e.Handled = true;

        _secureSessionService.Clear();

        ShowLoggedOutState();

        RefreshAccountActionButton();
    }

    private void EmphasizeTrialStatus()
    {
        foreach (DependencyObject item
                 in EnumerateVisualTree(this))
        {
            if (item is not TextBlock textBlock)
            {
                continue;
            }

            string text =
                textBlock.Text
                ?? string.Empty;

            if (!text.Contains(
                    "Trial active",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            textBlock.FontSize = 16;
            textBlock.FontWeight =
                FontWeights.SemiBold;

            textBlock.Foreground =
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        18,
                        96,
                        49));

            textBlock.Margin =
                new Thickness(
                    4,
                    2,
                    4,
                    2);

            if (textBlock.Parent
                is Border badge)
            {
                badge.Padding =
                    new Thickness(
                        12,
                        7,
                        12,
                        7);

                badge.CornerRadius =
                    new CornerRadius(11);

                badge.Background =
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(
                            220,
                            252,
                            231));

                badge.BorderBrush =
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(
                            134,
                            239,
                            172));

                badge.BorderThickness =
                    new Thickness(1);
            }
        }
    }

    private bool _settingsPremiumAndAboutEnhanced;

    private void ApplySettingsPremiumAndAboutEnhancements()
    {
        if (_settingsPremiumAndAboutEnhanced)
        {
            return;
        }

        _settingsPremiumAndAboutEnhanced = true;

        Width = 500;
        MinWidth = 500;
        MaxWidth = 500;

        ReplaceAboutCardWithCompactButton();
        AddPremiumButtonToAccountCard();
    }

    private Border? FindSectionCard(
        string sectionTitle)
    {
        foreach (DependencyObject item
                 in EnumerateVisualTree(this))
        {
            if (item is not TextBlock textBlock)
            {
                continue;
            }

            if (!string.Equals(
                    textBlock.Text,
                    sectionTitle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DependencyObject? current =
                textBlock;

            while (current != null)
            {
                if (current is Border border)
                {
                    return border;
                }

                current =
                    System.Windows.Media.VisualTreeHelper.GetParent(
                        current);
            }
        }

        return null;
    }

    private void ReplaceAboutCardWithCompactButton()
    {
        Border? aboutCard =
            FindSectionCard("About");

        if (aboutCard == null)
        {
            return;
        }

        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        2,
                        0,
                        2,
                        0)
            };

        grid.ColumnDefinitions.Add(
            new ColumnDefinition());

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        var title =
            new TextBlock
            {
                Text = "About",
                FontSize = 15.5,
                FontWeight =
                    FontWeights.SemiBold,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        var button =
            new Button
            {
                Content = "View details",
                Width = 96,
                Height = 32,
                Cursor =
                    System.Windows.Input.Cursors.Hand
            };

        button.Click +=
            AboutDetailsButton_Click;

        Grid.SetColumn(
            button,
            1);

        grid.Children.Add(title);
        grid.Children.Add(button);

        aboutCard.Padding =
            new Thickness(
                9,
                7,
                9,
                7);

        aboutCard.Child =
            grid;
    }

    private void AboutDetailsButton_Click(
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

    private void AddPremiumButtonToAccountCard()
    {
        Border? accountCard =
            FindSectionCard(
                "Account & Plan");

        if (accountCard?.Child
            is not Panel panel)
        {
            return;
        }

        foreach (UIElement child
                 in panel.Children)
        {
            if (child is Button existing &&
                string.Equals(
                    existing.Content?.ToString(),
                    "Go Premium",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var button =
            new Button
            {
                Content = "Go Premium",
                Width = 108,
                Height = 34,
                Cursor =
                    System.Windows.Input.Cursors.Hand,
                Margin =
                    new Thickness(
                        0,
                        7,
                        0,
                        0),
                HorizontalAlignment =
                    HorizontalAlignment.Left,
                Background =
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(
                            32,
                            35,
                            40)),
                Foreground =
                    System.Windows.Media.Brushes.White,
                BorderBrush =
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(
                            32,
                            35,
                            40))
            };

        button.Click +=
            PremiumButton_Click;

        panel.Children.Add(button);
    }

    private void PremiumButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Premium checkout will be connected after the remaining app features are complete. Your current trial remains active.",
            "Bezel Sticky Notes Premium",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}