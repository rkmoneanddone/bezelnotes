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
    }

    protected override void OnContentRendered(
        EventArgs e)
    {
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
}