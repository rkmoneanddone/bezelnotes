using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StickyNotes;

public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "About Bezel Sticky Notes";
        Width = 380;
        Height = 345;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(248, 249, 250));
        Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(37, 40, 45));
        FontFamily = new FontFamily("Segoe UI");

        var root =
            new StackPanel
            {
                Margin = new Thickness(18)
            };

        root.Children.Add(
            new TextBlock
            {
                Text = "Bezel Sticky Notes",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(32, 35, 40))
            });

        root.Children.Add(
            new TextBlock
            {
                Text = "Version 1.0.0",
                FontSize = 13.5,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(98, 104, 112))
            });

        var descriptionBorder =
            new Border
            {
                Margin = new Thickness(0, 14, 0, 0),
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(8),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(238, 243, 248)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(217, 225, 232)),
                BorderThickness = new Thickness(1)
            };

        descriptionBorder.Child =
            new TextBlock
            {
                Text =
                    "A lightweight Windows sticky-notes app that keeps notes local by default and docks them neatly to the screen edge.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13.2
            };

        root.Children.Add(descriptionBorder);

        root.Children.Add(
            new TextBlock
            {
                Text = "Privacy & data",
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 13, 0, 0)
            });

        root.Children.Add(
            new TextBlock
            {
                Text =
                    "Your note content remains on this PC unless you explicitly enable your own cloud storage. Firebase is used for account, trial, entitlement, and subscription verification.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(78, 85, 93))
            });

        root.Children.Add(
            new TextBlock
            {
                Text =
                    "Support, privacy-policy, and update links will be connected before Store release.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Margin = new Thickness(0, 12, 0, 0),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(106, 112, 120))
            });

        var closeButton =
            new Button
            {
                Content = "Close",
                Width = 88,
                Height = 34,
                Margin = new Thickness(0, 14, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = System.Windows.Input.Cursors.Hand
            };

        closeButton.Click +=
            (_, _) => Close();

        root.Children.Add(closeButton);

        Content = root;
    }
}