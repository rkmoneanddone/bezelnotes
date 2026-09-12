using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StickyNotes.Core.Models;
using StickyNotes.Services;

namespace StickyNotes;

public sealed class PremiumWindow : Window
{
    private readonly AuthSession _session;
    private readonly SecureBackendService _backend;
    private BackendAccountState _state;
    private readonly string _market;

    private readonly TextBlock _statusText = new();
    private readonly TextBlock _marketText = new();
    private readonly TextBlock _sixMonthPriceText = new();
    private readonly TextBlock _yearlyPriceText = new();

    private readonly Button _sixMonthButton = new();
    private readonly Button _yearlyButton = new();
    private readonly Button _refreshButton = new();
    private readonly Button _manageBillingButton = new();

    private CancellationTokenSource? _pollCts;

    public PremiumWindow(
        FirebaseClientConfig config,
        AuthSession session,
        BackendAccountState initialState)
    {
        _session = session;
        _backend = new SecureBackendService(config);
        _state = initialState;
        _market = ResolveMarket();

        Title = "Bezel Sticky Notes Premium";
        Width = 620;
        Height = 650;
        MinWidth = 620;
        MaxWidth = 620;
        MinHeight = 650;
        MaxHeight = 650;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(244, 245, 247));
        Foreground = new SolidColorBrush(Color.FromRgb(47, 50, 55));
        FontFamily = new FontFamily("Segoe UI");

        Content = BuildUi();
        ApplyState(initialState);

        Closed += (_, _) =>
        {
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = null;
        };
    }

    private UIElement BuildUi()
    {
        var root = new Grid
        {
            Margin = new Thickness(22)
        };

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();

        header.Children.Add(new TextBlock
        {
            Text = "Bezel Premium",
            FontFamily = new FontFamily("Georgia"),
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 36, 58))
        });

        header.Children.Add(new TextBlock
        {
            Text = "Choose a recurring Premium plan. Checkout is completed securely through Dodo Payments.",
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 13.5,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 106, 115)),
            TextWrapping = TextWrapping.Wrap
        });

        _marketText.Margin = new Thickness(0, 8, 0, 0);
        _marketText.FontSize = 12.5;
        _marketText.FontWeight = FontWeights.SemiBold;
        header.Children.Add(_marketText);

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var trialCard = Card();
        var trialStack = new StackPanel();

        trialStack.Children.Add(new TextBlock
        {
            Text = "7-day free trial",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold
        });

        trialStack.Children.Add(new TextBlock
        {
            Text = "The trial is controlled by your Bezel account. Dodo does not add a second trial.",
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 106, 115)),
            TextWrapping = TextWrapping.Wrap
        });

        trialCard.Child = trialStack;
        Grid.SetRow(trialCard, 2);
        root.Children.Add(trialCard);

        var plans = new Grid();
        plans.ColumnDefinitions.Add(new ColumnDefinition());
        plans.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        plans.ColumnDefinitions.Add(new ColumnDefinition());

        var sixCard = CreatePlanCard(
            "6 months",
            _sixMonthPriceText,
            "Recurring every 6 months",
            _sixMonthButton,
            "Choose 6 months",
            async () => await StartCheckoutAsync("six_month"));

        Grid.SetColumn(sixCard, 0);
        plans.Children.Add(sixCard);

        var yearCard = CreatePlanCard(
            "Yearly",
            _yearlyPriceText,
            "Recurring every year",
            _yearlyButton,
            "Choose yearly",
            async () => await StartCheckoutAsync("yearly"));

        Grid.SetColumn(yearCard, 2);
        plans.Children.Add(yearCard);

        Grid.SetRow(plans, 4);
        root.Children.Add(plans);

        var statusCard = Card();
        var statusStack = new StackPanel();

        statusStack.Children.Add(new TextBlock
        {
            Text = "Payment status",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold
        });

        _statusText.Margin = new Thickness(0, 6, 0, 0);
        _statusText.FontSize = 12.5;
        _statusText.TextWrapping = TextWrapping.Wrap;
        _statusText.Foreground =
            new SolidColorBrush(Color.FromRgb(80, 86, 95));
        statusStack.Children.Add(_statusText);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0)
        };

        _refreshButton.Content = "Refresh Premium status";
        _refreshButton.Padding = new Thickness(12, 7, 12, 7);
        _refreshButton.Cursor = System.Windows.Input.Cursors.Hand;
        _refreshButton.Click += async (_, _) =>
            await RefreshPremiumStatusAsync(true);
        actions.Children.Add(_refreshButton);

        _manageBillingButton.Content = "Manage billing";
        _manageBillingButton.Padding = new Thickness(12, 7, 12, 7);
        _manageBillingButton.Margin = new Thickness(8, 0, 0, 0);
        _manageBillingButton.Cursor = System.Windows.Input.Cursors.Hand;
        _manageBillingButton.Click += (_, _) =>
            OpenBillingPortal();
        actions.Children.Add(_manageBillingButton);

        statusStack.Children.Add(actions);
        statusCard.Child = statusStack;

        Grid.SetRow(statusCard, 6);
        root.Children.Add(statusCard);

        var securityNote = new TextBlock
        {
            Text = "Premium is activated only after the backend receives verified payment confirmation. The desktop app never grants itself Premium.",
            Margin = new Thickness(2, 12, 2, 0),
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 106, 115)),
            TextWrapping = TextWrapping.Wrap
        };

        Grid.SetRow(securityNote, 7);
        root.Children.Add(securityNote);

        var closeButton = new Button
        {
            Content = "Close",
            Width = 96,
            Padding = new Thickness(10, 7, 10, 7),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = System.Windows.Input.Cursors.Hand
        };

        closeButton.Click += (_, _) => Close();

        Grid.SetRow(closeButton, 8);
        root.Children.Add(closeButton);

        return root;
    }

    private static Border Card()
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(224, 226, 230)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14)
        };
    }

    private static Border CreatePlanCard(
        string title,
        TextBlock priceText,
        string recurrenceText,
        Button button,
        string buttonText,
        Func<Task> action)
    {
        var card = Card();
        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold
        });

        priceText.Margin = new Thickness(0, 7, 0, 0);
        priceText.FontSize = 17;
        priceText.FontWeight = FontWeights.Bold;
        stack.Children.Add(priceText);

        stack.Children.Add(new TextBlock
        {
            Text = recurrenceText,
            Margin = new Thickness(0, 4, 0, 12),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 106, 115))
        });

        button.Content = buttonText;
        button.Padding = new Thickness(12, 8, 12, 8);
        button.Cursor = System.Windows.Input.Cursors.Hand;
        button.Background = new SolidColorBrush(Color.FromRgb(48, 52, 58));
        button.Foreground = Brushes.White;
        button.BorderThickness = new Thickness(0);
        button.Click += async (_, _) => await action();

        stack.Children.Add(button);
        card.Child = stack;

        return card;
    }

    private static string ResolveMarket()
    {
        try
        {
            return string.Equals(
                RegionInfo.CurrentRegion.TwoLetterISORegionName,
                "IN",
                StringComparison.OrdinalIgnoreCase)
                ? "india"
                : "international";
        }
        catch
        {
            return "international";
        }
    }

    private BackendPaymentPlan GetPlan(
        BackendAccountState state,
        string planCode)
    {
        BackendPaymentPlanGroup group =
            planCode == "six_month"
                ? state.PaymentConfig.SixMonth
                : state.PaymentConfig.Yearly;

        return _market == "india"
            ? group.India
            : group.International;
    }

    private static string FormatPrice(
        BackendPaymentPlan plan)
    {
        if (plan.AmountMinor <= 0 ||
            string.IsNullOrWhiteSpace(plan.Currency))
        {
            return "Unavailable";
        }

        decimal amount = plan.AmountMinor / 100m;

        return
            $"{plan.Currency.ToUpperInvariant()} {amount:0.##}";
    }

    private void ApplyState(
        BackendAccountState state)
    {
        _state = state;

        _marketText.Text =
            _market == "india"
                ? "Regional pricing: India"
                : "Regional pricing: International";

        BackendPaymentPlan six =
            GetPlan(state, "six_month");

        BackendPaymentPlan year =
            GetPlan(state, "yearly");

        _sixMonthPriceText.Text =
            FormatPrice(six);

        _yearlyPriceText.Text =
            FormatPrice(year);

        bool premium =
            state.PremiumEnabled ||
            string.Equals(
                state.EntitlementState,
                "active",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                state.EntitlementState,
                "grace",
                StringComparison.OrdinalIgnoreCase);

        bool paymentsEnabled =
            state.AppConfig.PaymentsEnabled &&
            state.PaymentConfig.Enabled;

        _sixMonthButton.IsEnabled =
            !premium &&
            paymentsEnabled &&
            six.Enabled;

        _yearlyButton.IsEnabled =
            !premium &&
            paymentsEnabled &&
            year.Enabled;

        _manageBillingButton.IsEnabled =
            premium &&
            !string.IsNullOrWhiteSpace(
                state.PaymentConfig.CustomerPortalUrl);

        if (premium)
        {
            _statusText.Text =
                state.EntitlementState.Equals(
                    "grace",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Premium is in a payment grace period."
                    : "Premium is active on this account.";

            _sixMonthButton.Content =
                "Premium active";

            _yearlyButton.Content =
                "Premium active";
        }
        else if (!paymentsEnabled)
        {
            _statusText.Text =
                "Premium checkout is currently disabled by Bezel configuration.";
        }
        else
        {
            _statusText.Text =
                "Choose a plan to open secure checkout.";

            _sixMonthButton.Content =
                "Choose 6 months";

            _yearlyButton.Content =
                "Choose yearly";
        }
    }

    private async Task StartCheckoutAsync(
        string planCode)
    {
        BackendPaymentPlan plan =
            GetPlan(_state, planCode);

        if (!plan.Enabled)
        {
            _statusText.Text =
                "This Premium plan is currently unavailable.";
            return;
        }

        SetBusy(true);

        try
        {
            _statusText.Text =
                "Creating secure Dodo checkout...";

            PaymentCheckoutResponse checkout =
                await _backend.CreatePaymentCheckoutAsync(
                    _session,
                    planCode,
                    _market);

            if (string.IsNullOrWhiteSpace(
                    checkout.CheckoutUrl))
            {
                throw new InvalidOperationException(
                    "The backend did not return a checkout URL.");
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = checkout.CheckoutUrl,
                    UseShellExecute = true
                });

            _statusText.Text =
                "Checkout opened in your browser. " +
                "Bezel will check for verified payment confirmation.";

            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = new CancellationTokenSource();

            _ = PollForPremiumAsync(
                _pollCts.Token);
        }
        catch (Exception ex)
        {
            _statusText.Text =
                "Could not start checkout: " +
                ex.Message;

            SetBusy(false);
        }
    }

    private async Task PollForPremiumAsync(
        CancellationToken cancellationToken)
    {
        int[] delaysSeconds =
        {
            10,
            15,
            20,
            30
        };

        try
        {
            foreach (int delaySeconds in delaysSeconds)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(delaySeconds),
                    cancellationToken);

                BackendAccountState fresh =
                    await _backend.BootstrapAccountAsync(
                        _session);

                new BackendBootstrapCacheService()
                    .Save(fresh);

                if (fresh.PremiumEnabled ||
                    string.Equals(
                        fresh.EntitlementState,
                        "active",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        fresh.EntitlementState,
                        "grace",
                        StringComparison.OrdinalIgnoreCase))
                {
                    ApplyState(fresh);

                    _statusText.Text =
                        "Payment confirmed. Premium is active.";

                    SetBusy(false);
                    return;
                }
            }

            _statusText.Text =
                "Payment is not confirmed yet. " +
                "If checkout completed, use Refresh Premium status.";
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _statusText.Text =
                "Automatic status checking paused. " +
                "Use Refresh Premium status.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshPremiumStatusAsync(
        bool showMessage)
    {
        _refreshButton.IsEnabled = false;

        try
        {
            if (showMessage)
            {
                _statusText.Text =
                    "Refreshing Premium status...";
            }

            BackendAccountState fresh =
                await _backend.BootstrapAccountAsync(
                    _session);

            new BackendBootstrapCacheService()
                .Save(fresh);

            ApplyState(fresh);

            if (fresh.PremiumEnabled ||
                string.Equals(
                    fresh.EntitlementState,
                    "active",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    fresh.EntitlementState,
                    "grace",
                    StringComparison.OrdinalIgnoreCase))
            {
                _statusText.Text =
                    "Premium is active.";
            }
            else if (showMessage)
            {
                _statusText.Text =
                    "Premium is not active yet.";
            }
        }
        catch (Exception ex)
        {
            _statusText.Text =
                "Could not refresh Premium status: " +
                ex.Message;
        }
        finally
        {
            _refreshButton.IsEnabled = true;
        }
    }

    private void OpenBillingPortal()
    {
        string url =
            _state.PaymentConfig.CustomerPortalUrl;

        if (string.IsNullOrWhiteSpace(url))
        {
            _statusText.Text =
                "Billing management is not configured.";
            return;
        }

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            _statusText.Text =
                "Could not open billing management: " +
                ex.Message;
        }
    }

    private void SetBusy(
        bool busy)
    {
        if (busy)
        {
            _sixMonthButton.IsEnabled = false;
            _yearlyButton.IsEnabled = false;
            _refreshButton.IsEnabled = false;
            return;
        }

        ApplyState(_state);
        _refreshButton.IsEnabled = true;
    }
}