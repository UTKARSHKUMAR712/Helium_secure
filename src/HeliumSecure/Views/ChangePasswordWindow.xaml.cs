using System.Windows;
using HeliumSecure.Core.Crypto;
using HeliumSecure.Core.Profiles;
using HeliumSecure.Helpers;

namespace HeliumSecure.Views;

public partial class ChangePasswordWindow : Window
{
    private readonly ProfileManager _profileManager;
    private readonly Guid _profileId;

    public ChangePasswordWindow(ProfileManager profileManager, Guid profileId, string profileName)
    {
        InitializeComponent();
        _profileManager = profileManager;
        _profileId = profileId;

        VaultSubtitleText.Text = $"Re-encrypts vault '{profileName}' with a fresh Argon2id key and salt.";

        WindowThemeHelper.EnableDarkTitleBar(this);
        Loaded += OnLoaded;
        CurrentPasswordBox.Focus();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        double availableHeight = SystemParameters.WorkArea.Height;
        if (Owner != null && Owner.ActualHeight > 100)
        {
            availableHeight = Owner.ActualHeight;
        }

        MaxHeight = Math.Max(360, availableHeight * 0.90);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void OnChangePasswordClick(object sender, RoutedEventArgs e)
    {
        string currentPassword = CurrentPasswordBox.Password;
        string newPassword = NewPasswordBox.Password;
        string confirmPassword = ConfirmPasswordBox.Password;

        if (string.IsNullOrEmpty(currentPassword))
        {
            ShowError("Please enter your current master password.");
            CurrentPasswordBox.Focus();
            return;
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            ShowError("New master password cannot be empty.");
            NewPasswordBox.Focus();
            return;
        }

        if (newPassword != confirmPassword)
        {
            ShowError("New master passwords do not match.");
            ConfirmPasswordBox.Focus();
            return;
        }

        if (currentPassword == newPassword)
        {
            ShowError("New master password must be different from your current password.");
            NewPasswordBox.Focus();
            return;
        }

        HideError();
        SetBusyState(true);

        try
        {
            await Task.Run(() =>
            {
                _profileManager.ChangePassword(_profileId, currentPassword, newPassword);
            });

            DialogResult = true;
            Close();
        }
        catch (ProfileLockedOutException ex)
        {
            ShowError($"Profile is temporarily locked out. Remaining time: {(int)ex.RemainingLockout.TotalMinutes}m {ex.RemainingLockout.Seconds}s.");
        }
        catch (TerminalDestructionException)
        {
            ShowError("Profile was permanently destroyed due to exceeding 5 second-stage failed attempts.");
        }
        catch (AuthenticationFailedException ex)
        {
            ShowError(ex.Message);
            CurrentPasswordBox.SelectAll();
            CurrentPasswordBox.Focus();
        }
        catch (ProfileAlreadyLockedException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to change password: {ex.Message}");
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void SetBusyState(bool isBusy)
    {
        SubmitButton.IsEnabled = !isBusy;
        SubmitButton.Content = isBusy ? "Re-encrypting..." : "Change Password";
        CurrentPasswordBox.IsEnabled = !isBusy;
        NewPasswordBox.IsEnabled = !isBusy;
        ConfirmPasswordBox.IsEnabled = !isBusy;
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorContainer.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorMessage.Text = string.Empty;
        ErrorContainer.Visibility = Visibility.Collapsed;
    }
}
