using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StickyNotes;

public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "About Bezel Sticky Notes";
        Width = 440;
        Height = 500;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background =
            new SolidColorBrush(
                Color.FromRgb(
                    248,
                    249,
                    250));
        Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    37,
                    40,
                    45));
        FontFamily =
            new FontFamily("Segoe UI");

        Version? version =
            Assembly.GetExecutingAssembly()
                .GetName()
                .Version;

        string versionText =
            version is null
                ? "Version unknown"
                : $"Version {version.Major}.{version.Minor}.{version.Build}";

        var root =
            new Grid
            {
                Margin =
                    new Thickness(18)
            };

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        root.RowDefinitions.Add(
            new RowDefinition());

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        var header =
            new StackPanel();

        header.Children.Add(
            new TextBlock
            {
                Text =
                    "Bezel Sticky Notes",
                FontSize = 21,
                FontWeight =
                    FontWeights.SemiBold
            });

        header.Children.Add(
            new TextBlock
            {
                Text = versionText,
                FontSize = 13,
                Margin =
                    new Thickness(
                        0,
                        3,
                        0,
                        0),
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            100,
                            106,
                            115))
            });

        root.Children.Add(header);

        var scroll =
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                Margin =
                    new Thickness(
                        0,
                        14,
                        0,
                        10)
            };

        Grid.SetRow(
            scroll,
            1);

        var body =
            new StackPanel();

        AddSection(
            body,
            "What it does",
            "Bezel Sticky Notes keeps quick notes attached to the edge of your Windows desktop. Notes stay out of the way until you hover, preview, or open them for editing.");

        AddSection(
            body,
            "Local notes",
            "Your note title and content are stored locally on this PC. Bezel does not store your note content in Firebase.");

        AddSection(
            body,
            "Account & trial",
            "Your account is used to verify the 7-day trial and, later, your Premium subscription. Trial and entitlement status are verified by the secure backend rather than decided by the desktop app.");

        AddSection(
            body,
            "Cloud sync",
            "Google Drive and OneDrive sync are planned as optional features. When enabled, note data will sync only to storage you own. Cloud sync is not active yet.");

        AddSection(
            body,
            "Premium",
            "Premium checkout is not connected yet. The Go Premium button is present so the final subscription flow can be added without redesigning Settings.");

        AddSection(
            body,
            "Startup",
            "Bezel Sticky Notes can start automatically with Windows. This can be changed from Settings.");

        AddSection(
            body,
            "Privacy",
            "Firebase is used for account, trial, entitlement, installation, and subscription verification. Note content remains separate from that account backend.");

        AddSection(
            body,
            "Support & updates",
            "Support contact, privacy-policy link, release notes, and update checking will be connected before Store release.");

        scroll.Content =
            body;

        root.Children.Add(scroll);

        var closeButton =
            new Button
            {
                Content = "Close",
                Width = 88,
                Height = 34,
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                Cursor =
                    System.Windows.Input.Cursors.Hand
            };

        closeButton.Click +=
            (_, _) => Close();

        Grid.SetRow(
            closeButton,
            2);

        root.Children.Add(
            closeButton);

        Content =
            root;
    }

    private static void AddSection(
        Panel parent,
        string title,
        string body)
    {
        parent.Children.Add(
            new TextBlock
            {
                Text = title,
                FontSize = 14.5,
                FontWeight =
                    FontWeights.SemiBold,
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        3)
            });

        parent.Children.Add(
            new TextBlock
            {
                Text = body,
                TextWrapping =
                    TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 19,
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        13),
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            78,
                            85,
                            93))
            });
    }
}