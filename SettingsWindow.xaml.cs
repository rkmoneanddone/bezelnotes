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
        SelectComboValue(EdgeComboBox, _settings.Edge);
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
        _settings.Edge = GetComboValue(EdgeComboBox, "Right");
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
}


