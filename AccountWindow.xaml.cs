using System.Windows;
using StickyNotes.Core.Models;
using StickyNotes.Services;

namespace StickyNotes;

public partial class AccountWindow : Window
{
    private readonly FirebaseClientConfigService _configService = new();

    public AccountWindow()
    {
        InitializeComponent();

        FirebaseClientConfig config =
            _configService.Load();

        ConfigPathText.Text =
            config.IsConfigured
                ? "Secure account backend connected."
                : $"Account backend is not configured. Edit: {_configService.ConfigPath}";
    }

    private async void GoogleSignInButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        FirebaseClientConfig config =
            _configService.Load();

        if (!config.IsConfigured)
        {
            StatusText.Text =
                $"Firebase is not configured. Edit {_configService.ConfigPath}";
            return;
        }

        if (string.IsNullOrWhiteSpace(
                config.GoogleOAuthClientId))
        {
            StatusText.Text =
                "Google sign-in is not configured.";
            return;
        }

        try
        {
            SetBusy(true);

            StatusText.Text =
                "Opening Google sign-in in your browser...";

            var google =
                new GoogleOAuthService(
                    config.GoogleOAuthClientId);

            GoogleOAuthResult googleResult =
                await google.SignInAsync();

            StatusText.Text =
                "Completing secure Google sign-in...";

            var googleBackend =
                new GoogleBackendAuthService(config);

            AuthSession session =
                await googleBackend.ExchangeAsync(
                    googleResult);

            await CompleteSecureSignInAsync(
                config,
                session);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "Google sign-in timed out or was cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text =
                ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SignInButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await AuthenticateEmailAsync(
            createAccount: false);
    }

    private async void CreateAccountButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await AuthenticateEmailAsync(
            createAccount: true);
    }

    private async Task AuthenticateEmailAsync(
        bool createAccount)
    {
        string email =
            EmailBox.Text.Trim();

        string password =
            PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            StatusText.Text =
                "Enter email and password.";
            return;
        }

        FirebaseClientConfig config =
            _configService.Load();

        if (!config.IsConfigured)
        {
            StatusText.Text =
                $"Firebase is not configured. Edit {_configService.ConfigPath}";
            return;
        }

        try
        {
            SetBusy(true);

            StatusText.Text =
                createAccount
                    ? "Creating account securely..."
                    : "Signing in securely...";

            var auth =
                new FirebaseAuthService(config);

            AuthSession session =
                createAccount
                    ? await auth.CreateAccountAsync(
                        email,
                        password)
                    : await auth.SignInAsync(
                        email,
                        password);

            await CompleteSecureSignInAsync(
                config,
                session);
        }
        catch (Exception ex)
        {
            StatusText.Text =
                ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CompleteSecureSignInAsync(
        FirebaseClientConfig config,
        AuthSession session)
    {
        var backend =
            new SecureBackendService(
                config);

        BackendAccountState state =
            await backend.BootstrapAccountAsync(
                session);

        EmailBox.Text =
            state.Email;

        StatusText.Text =
            state.EntitlementState switch
            {
                "trial" =>
                    $"Signed in as {state.Email}. Trial: {state.TrialDaysRemaining} day(s) remaining.",

                "active" =>
                    $"Signed in as {state.Email}. Subscription active.",

                "grace" =>
                    $"Signed in as {state.Email}. Grace period active.",

                "expired" =>
                    $"Signed in as {state.Email}. Trial or subscription has expired. Your local notes remain safe.",

                _ =>
                    $"Signed in as {state.Email}. Account verified."
            };

        await CloseAfterSuccessfulSignInAsync();
    }

    private async Task CloseAfterSuccessfulSignInAsync()
    {
        StatusBorder.Background =
            new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(
                    232,
                    245,
                    233));

        StatusBorder.BorderBrush =
            new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(
                    174,
                    213,
                    177));

        await Task.Delay(900);

        DialogResult = true;
        Close();
    }
    private void SetBusy(bool busy)
    {
        GoogleSignInButton.IsEnabled =
            !busy;

        EmailBox.IsEnabled =
            !busy;

        PasswordBox.IsEnabled =
            !busy;
    }
}