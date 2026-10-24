using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HeliumSecure.Core.Guest;
using HeliumSecure.Core.Models;
using HeliumSecure.Core.Process;
using HeliumSecure.Core.Profiles;
using HeliumSecure.Views;

namespace HeliumSecure;

public sealed class ProfileDisplayItem
{
    public ProfileInfo Profile { get; }
    public Guid Id => Profile.Id;
    public string Name => Profile.Name;
    public string LastUsedText => $"Last used: {Profile.LastUsedAt.ToLocalTime():yyyy-MM-dd HH:mm}";

    public ProfileDisplayItem(ProfileInfo profile)
    {
        Profile = profile;
    }
}

public partial class MainWindow : Window
{
    private readonly ProfileManager _profileManager;
    private readonly GuestSessionManager _guestManager;
    private string? _heliumExecutablePath;
    private bool _isPasswordRevealed;
    private ProfileDisplayItem? _selectedItem;

    public MainWindow()
    {
        InitializeComponent();

        _profileManager = new ProfileManager();
        _guestManager = new GuestSessionManager();

        DetectHeliumExecutable();
        RefreshProfileList();

        // If no profiles exist, prompt to create first one
        if (_profileManager.ListProfiles().Count == 0)
        {
            Dispatcher.InvokeAsync(PromptCreateFirstProfile);
        }
    }

    private void DetectHeliumExecutable()
    {
        _heliumExecutablePath = BrowserProcessManager.ResolveHeliumExecutable();
        if (!string.IsNullOrEmpty(_heliumExecutablePath) && File.Exists(_heliumExecutablePath))
        {
            BrowserPathStatus.Text = $"✅ Helium Browser: {_heliumExecutablePath}";
            BrowserPathStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Emerald green
        }
        else
        {
            BrowserPathStatus.Text = "⚠️ Helium executable not detected in default location.";
            BrowserPathStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)); // Amber
        }
    }

    private void PromptCreateFirstProfile()
    {
        var createWin = new CreateProfileWindow(_profileManager) { Owner = this };
        if (createWin.ShowDialog() == true && createWin.CreatedProfileId.HasValue)
        {
            RefreshProfileList(createWin.CreatedProfileId.Value);
        }
    }

    private void RefreshProfileList(Guid? selectId = null)
    {
        var profiles = _profileManager.ListProfiles()
            .OrderByDescending(p => p.LastUsedAt)
            .Select(p => new ProfileDisplayItem(p))
            .ToList();

        ProfilesListBox.ItemsSource = profiles;

        if (profiles.Count > 0)
        {
            if (selectId.HasValue)
            {
                var match = profiles.FirstOrDefault(p => p.Id == selectId.Value);
                ProfilesListBox.SelectedItem = match ?? profiles[0];
            }
            else
            {
                ProfilesListBox.SelectedIndex = 0;
            }
        }
        else
        {
            _selectedItem = null;
            UpdateDetailsView();
        }
    }

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedItem = ProfilesListBox.SelectedItem as ProfileDisplayItem;
        UpdateDetailsView();
    }

    private void UpdateDetailsView()
    {
        ClearStatus();
        ProfilePasswordBox.Password = string.Empty;
        ProfilePasswordTextBox.Text = string.Empty;

        if (_selectedItem == null)
        {
            ProfileDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ProfileDetailsPanel.Visibility = Visibility.Visible;
        SelectedProfileName.Text = _selectedItem.Name;

        bool isLocked = _profileManager.IsProfileLocked(_selectedItem.Id);
        if (isLocked)
        {
            SelectedProfileStatus.Text = "🔒 Active Session Running";
            SelectedProfileStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            UnlockButton.IsEnabled = false;
            UnlockButton.Content = "Profile Active in Browser";
        }
        else
        {
            SelectedProfileStatus.Text = "Encrypted AES-256-GCM Vault";
            SelectedProfileStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            UnlockButton.IsEnabled = true;
            UnlockButton.Content = "Unlock & Launch Browser";
        }
    }

    private void OnTogglePasswordVisibility(object sender, RoutedEventArgs e)
    {
        _isPasswordRevealed = !_isPasswordRevealed;
        if (_isPasswordRevealed)
        {
            ProfilePasswordTextBox.Text = ProfilePasswordBox.Password;
            ProfilePasswordBox.Visibility = Visibility.Collapsed;
            ProfilePasswordTextBox.Visibility = Visibility.Visible;
            ProfilePasswordTextBox.Focus();
            ProfilePasswordTextBox.CaretIndex = ProfilePasswordTextBox.Text.Length;
            TogglePasswordButton.Content = "🙈";
        }
        else
        {
            ProfilePasswordBox.Password = ProfilePasswordTextBox.Text;
            ProfilePasswordTextBox.Visibility = Visibility.Collapsed;
            ProfilePasswordBox.Visibility = Visibility.Visible;
            ProfilePasswordBox.Focus();
            TogglePasswordButton.Content = "👁️";
        }
    }

    private string GetEnteredPassword() =>
        _isPasswordRevealed ? ProfilePasswordTextBox.Text : ProfilePasswordBox.Password;

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && UnlockButton.IsEnabled)
        {
            OnUnlockAndLaunchClick(sender, e);
        }
    }

    private async void OnUnlockAndLaunchClick(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;

        string password = GetEnteredPassword();
        if (string.IsNullOrEmpty(password))
        {
            ShowStatus("Please enter your profile password.", isError: true);
            return;
        }

        Guid profileId = _selectedItem.Id;

        SetBusyState(true, "Deriving Argon2 key and authenticating vault...");

        try
        {
            var session = await _profileManager.UnlockAndLaunchAsync(
                profileId,
                password,
                _heliumExecutablePath,
                onSessionCompleted: () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ShowStatus("Browser session ended. Vault securely encrypted.", isError: false);
                        UpdateDetailsView();
                    });
                });

            ShowStatus("Browser running. Vault will re-encrypt on exit.", isError: false);
            UpdateDetailsView();
        }
        catch (CryptographicException)
        {
            ShowStatus("Authentication failed: Incorrect password or corrupted vault.", isError: true);
            SetBusyState(false);
        }
        catch (ProfileAlreadyLockedException ex)
        {
            ShowStatus(ex.Message, isError: true);
            SetBusyState(false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error launching profile: {ex.Message}", isError: true);
            SetBusyState(false);
        }
    }

    private void SetBusyState(bool isBusy, string? message = null)
    {
        UnlockButton.IsEnabled = !isBusy;
        if (isBusy && !string.IsNullOrEmpty(message))
        {
            ShowStatus(message, isError: false);
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusMessage.Text = message;
        StatusMessage.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(248, 113, 113)) // Light red
            : new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Emerald green
        StatusMessage.Visibility = Visibility.Visible;
    }

    private void ClearStatus()
    {
        StatusMessage.Text = string.Empty;
        StatusMessage.Visibility = Visibility.Collapsed;
    }

    private void OnNewProfileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateProfileWindow(_profileManager) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.CreatedProfileId.HasValue)
        {
            RefreshProfileList(dialog.CreatedProfileId.Value);
        }
    }

    private void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;

        var dialog = new RenameProfileWindow(_profileManager, _selectedItem.Id, _selectedItem.Name) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            RefreshProfileList(_selectedItem.Id);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;

        var dialog = new DeleteProfileWindow(_profileManager, _selectedItem.Id, _selectedItem.Name) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            RefreshProfileList();
        }
    }

    private void OnGuestClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var guestSession = _guestManager.StartSession(_heliumExecutablePath);
            ShowStatus("Disposable Guest session active. No history or cookies will be saved.", isError: false);

            _ = Task.Run(async () =>
            {
                await guestSession.WaitForExitAndCleanupAsync();
                Dispatcher.Invoke(() =>
                {
                    ShowStatus("Guest session terminated. All temporary data shredded.", isError: false);
                });
            });
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to start guest session: {ex.Message}", isError: true);
        }
    }
}
