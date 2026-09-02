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

        FirebaseClientConfig config = _configService.Load();

        ConfigPathText.Text = config.IsConfigured
            ? "Firebase secure backend configured."
            : $"Firebase is not configured yet. Edit: {_configService.ConfigPath}";
    }

    private async void SignInButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await AuthenticateAsync(createAccount: false);
    }

    private async void CreateAccountButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await AuthenticateAsync(createAccount: true);
    }

    private async Task AuthenticateAsync(bool createAccount)
    {
        string email = EmailBox.Text.Trim();
        string password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            StatusText.Text = "Enter email and password.";
            return;
        }

        FirebaseClientConfig config = _configService.Load();

        if (!config.IsConfigured)
        {
            StatusText.Text =
                $"Firebase is not configured. Edit {_configService.ConfigPath}";
            return;
        }

        try
        {
            StatusText.Text = createAccount
                ? "Creating account securely..."
                : "Signing in securely...";

            var auth = new FirebaseAuthService(config);

            AuthSession session = createAccount
                ? await auth.CreateAccountAsync(email, password)
                : await auth.SignInAsync(email, password);

            // The client does NOT calculate or grant the trial.
            // It asks the trusted Cloud Function for the authoritative state.
            var backend = new SecureBackendService(config);

            BackendAccountState state =
                await backend.BootstrapAccountAsync(session);

            StatusText.Text = state.EntitlementState switch
            {
                "trial" =>
                    $"Signed in. Trial: {state.TrialDaysRemaining} day(s) remaining.",

                "active" =>
                    "Signed in. Subscription active.",

                "grace" =>
                    "Signed in. Grace period active.",

                "expired" =>
                    "Signed in. Trial or subscription has expired. Your local notes remain safe.",

                _ =>
                    "Signed in. Account state received."
            };
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
