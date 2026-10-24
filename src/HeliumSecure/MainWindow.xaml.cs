using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HeliumSecure.Core.Guest;
using HeliumSecure.Core.Hardware;
using HeliumSecure.Core.Models;
using HeliumSecure.Core.Process;
using HeliumSecure.Core.Profiles;
using HeliumSecure.Helpers;
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
    private readonly ISystemMetricsProvider _metricsProvider;
    private readonly System.Windows.Threading.DispatcherTimer _lockoutCountdownTimer;
    private readonly System.Windows.Threading.DispatcherTimer _metricsTimer;
    private string? _heliumExecutablePath;
    private bool _isPasswordRevealed;
    private ProfileDisplayItem? _selectedItem;

    private static readonly Geometry EyeOpenGeometry = Geometry.Parse("M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5c-1.73-4.39-6-7.5-11-7.5zM12 17c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z");
    private static readonly Geometry EyeClosedGeometry = Geometry.Parse("M12 7c2.76 0 5 2.24 5 5 0 .65-.13 1.26-.36 1.83l2.92 2.92c1.51-1.26 2.7-2.89 3.44-4.75-1.73-4.39-6-7.5-11-7.5-1.4 0-2.74.25-3.98.7l2.16 2.16C10.74 7.13 11.35 7 12 7zM2 4.27l2.28 2.28.46.46C3.08 8.3 1.78 10.02 1 12c1.73 4.39 6 7.5 11 7.5 1.55 0 3.03-.3 4.38-.84l.42.42L19.73 22 21 20.73 3.27 3 2 4.27zM7.53 9.8l1.55 1.55c-.05.21-.08.43-.08.65 0 1.66 1.34 3 3 3 .22 0 .44-.03.65-.08l1.55 1.55c-.67.33-1.41.53-2.2.53-2.76 0-5-2.24-5-5 0-.79.2-1.53.53-2.2zm4.31-.78l3.15 3.15.02-.16c0-1.66-1.34-3-3-3l-.17.01z");

    public MainWindow()
    {
        InitializeComponent();

        WindowThemeHelper.EnableDarkTitleBar(this);

        _profileManager = new ProfileManager();
        _guestManager = new GuestSessionManager();
        _metricsProvider = new WindowsSystemMetricsProvider();

        _lockoutCountdownTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _lockoutCountdownTimer.Tick += OnLockoutTimerTick;

        _metricsTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _metricsTimer.Tick += OnMetricsTimerTick;
        _metricsTimer.Start();

        DetectHeliumExecutable();
        RefreshProfileList();
        UpdateMetricsDisplay();

        // If no profiles exist, prompt to create first one
        if (_profileManager.ListProfiles().Count == 0)
        {
            Dispatcher.InvokeAsync(PromptCreateFirstProfile);
        }
    }

    private void OnMetricsTimerTick(object? sender, EventArgs e)
    {
        UpdateMetricsDisplay();
    }

    private void UpdateMetricsDisplay()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var metrics = _metricsProvider.GetCurrentMetrics();
                string overlayStr = metrics.FormatOverlayString();
                string tooltipStr = metrics.FormatTooltip();

                Dispatcher.Invoke(() =>
                {
                    MetricsOverlayText.Text = overlayStr;
                    MetricsOverlayContainer.ToolTip = tooltipStr;
                });
            }
            catch { }
        });
    }

    private void OnLockoutTimerTick(object? sender, EventArgs e)
    {
        if (_selectedItem == null)
        {
            _lockoutCountdownTimer.Stop();
            return;
        }

        if (_profileManager.IsProfileLockedOut(_selectedItem.Id, out var remaining, out var inSecondStage))
        {
            LockoutCountdownText.Text = $"{(int)remaining.TotalMinutes}m {remaining.Seconds:D2}s remaining";
        }
        else
        {
            _lockoutCountdownTimer.Stop();
            UpdateDetailsView();
        }
    }

    private void DetectHeliumExecutable()
    {
        _heliumExecutablePath = BrowserProcessManager.ResolveHeliumExecutable();
        if (!string.IsNullOrEmpty(_heliumExecutablePath) && File.Exists(_heliumExecutablePath))
        {
            BrowserPathStatus.Text = $"Helium Browser: {_heliumExecutablePath}";
            BrowserPathStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Emerald green
        }
        else
        {
            BrowserPathStatus.Text = "Helium executable not detected in default location.";
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
            _lockoutCountdownTimer.Stop();
            ProfileDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ProfileDetailsPanel.Visibility = Visibility.Visible;
        SelectedProfileName.Text = _selectedItem.Name;

        bool isLocked = _profileManager.IsProfileLocked(_selectedItem.Id);
        if (isLocked)
        {
            _lockoutCountdownTimer.Stop();
            SelectedProfileStatus.Text = "Active Session Running";
            SelectedProfileStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(38, 30, 16));
            StatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(95, 68, 20));

            LockoutBanner.Visibility = Visibility.Collapsed;
            Stage2Banner.Visibility = Visibility.Collapsed;
            Stage1AttemptsBanner.Visibility = Visibility.Collapsed;

            ProfilePasswordBox.IsEnabled = false;
            ProfilePasswordTextBox.IsEnabled = false;
            TogglePasswordButton.IsEnabled = false;
            UnlockButton.IsEnabled = false;
            UnlockButtonText.Text = "Profile Active in Browser";
            return;
        }

        bool isLockedOut = _profileManager.IsProfileLockedOut(_selectedItem.Id, out var remainingLockout, out var inSecondStage);
        if (isLockedOut)
        {
            SelectedProfileStatus.Text = "Temporarily Locked Out (30m)";
            SelectedProfileStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 113, 133));
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(244, 63, 94));
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(43, 18, 24));
            StatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(225, 29, 72));

            LockoutBanner.Visibility = Visibility.Visible;
            LockoutCountdownText.Text = $"{(int)remainingLockout.TotalMinutes}m {remainingLockout.Seconds:D2}s remaining";
            Stage2Banner.Visibility = Visibility.Collapsed;
            Stage1AttemptsBanner.Visibility = Visibility.Collapsed;

            ProfilePasswordBox.IsEnabled = false;
            ProfilePasswordTextBox.IsEnabled = false;
            TogglePasswordButton.IsEnabled = false;
            UnlockButton.IsEnabled = false;
            UnlockButtonText.Text = "Locked Out (Countdown Active)";

            _lockoutCountdownTimer.Start();
        }
        else if (inSecondStage)
        {
            _lockoutCountdownTimer.Stop();
            int remainingAttempts = _profileManager.GetRemainingAttempts(_selectedItem.Id, out _);

            SelectedProfileStatus.Text = $"Stage 2 Security: {remainingAttempts} Attempts Left";
            SelectedProfileStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(43, 24, 8));
            StatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6));

            LockoutBanner.Visibility = Visibility.Collapsed;
            Stage2Banner.Visibility = Visibility.Visible;
            Stage2AttemptsText.Text = $"{remainingAttempts} attempt(s) remaining before PERMANENT DELETION";
            Stage1AttemptsBanner.Visibility = Visibility.Collapsed;

            ProfilePasswordBox.IsEnabled = true;
            ProfilePasswordTextBox.IsEnabled = true;
            TogglePasswordButton.IsEnabled = true;
            UnlockButton.IsEnabled = true;
            UnlockButtonText.Text = $"Unlock Profile ({remainingAttempts} Left)";
        }
        else
        {
            _lockoutCountdownTimer.Stop();
            int remainingAttempts = _profileManager.GetRemainingAttempts(_selectedItem.Id, out _);

            SelectedProfileStatus.Text = "AES-256-GCM Vault Secured";
            SelectedProfileStatus.Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253));
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(96, 165, 250));
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(22, 32, 53));
            StatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(35, 53, 85));

            LockoutBanner.Visibility = Visibility.Collapsed;
            Stage2Banner.Visibility = Visibility.Collapsed;

            if (remainingAttempts < 3)
            {
                Stage1AttemptsBanner.Visibility = Visibility.Visible;
                Stage1AttemptsText.Text = $"{remainingAttempts} attempt(s) remaining before a 30-minute lockout.";
            }
            else
            {
                Stage1AttemptsBanner.Visibility = Visibility.Collapsed;
            }

            ProfilePasswordBox.IsEnabled = true;
            ProfilePasswordTextBox.IsEnabled = true;
            TogglePasswordButton.IsEnabled = true;
            UnlockButton.IsEnabled = true;
            UnlockButtonText.Text = "Unlock & Launch Browser";
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
            TogglePasswordIcon.Data = EyeClosedGeometry;
        }
        else
        {
            ProfilePasswordBox.Password = ProfilePasswordTextBox.Text;
            ProfilePasswordTextBox.Visibility = Visibility.Collapsed;
            ProfilePasswordBox.Visibility = Visibility.Visible;
            ProfilePasswordBox.Focus();
            TogglePasswordIcon.Data = EyeOpenGeometry;
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

        SetBusyState(true, "Deriving Argon2id key and authenticating vault...");

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

            ShowStatus("Browser active. Vault will securely re-encrypt on exit.", isError: false);
            UpdateDetailsView();
        }
        catch (ProfileLockedOutException ex)
        {
            ShowStatus(ex.Message, isError: true);
            SetBusyState(false);
            UpdateDetailsView();
        }
        catch (TerminalDestructionException)
        {
            SetBusyState(false);
            MessageBox.Show(
                "CRITICAL SECURITY ENFORCEMENT\n\n" +
                $"Profile '{_selectedItem.Name}' exceeded the maximum 5 authentication attempts in Stage 2.\n\n" +
                "All encrypted vault files, recovery backups, and local session files have been PERMANENTLY AND IRRECOVERABLY DESTROYED.\n\n" +
                "Helium Pro enforces a strict zero-knowledge security policy. No backdoor, recovery key, or administrative bypass exists.",
                "Helium Pro — Profile Terminally Destroyed",
                MessageBoxButton.OK,
                MessageBoxImage.Stop);
            RefreshProfileList();
        }
        catch (AuthenticationFailedException ex)
        {
            ShowStatus(ex.Message, isError: true);
            SetBusyState(false);
            UpdateDetailsView();
        }
        catch (CryptographicException)
        {
            ShowStatus("Authentication failed: Incorrect password or corrupted vault.", isError: true);
            SetBusyState(false);
            UpdateDetailsView();
        }
        catch (ProfileAlreadyLockedException ex)
        {
            ShowStatus(ex.Message, isError: true);
            SetBusyState(false);
            UpdateDetailsView();
        }
        catch (Exception ex)
        {
            ShowStatus($"Error launching profile: {ex.Message}", isError: true);
            SetBusyState(false);
            UpdateDetailsView();
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
        if (isError)
        {
            StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(253, 164, 175));
            StatusMessageContainer.Background = new SolidColorBrush(Color.FromRgb(59, 18, 25));
            StatusMessageContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(225, 29, 72));
        }
        else
        {
            StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253));
            StatusMessageContainer.Background = new SolidColorBrush(Color.FromRgb(20, 33, 56));
            StatusMessageContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        }
        StatusMessageContainer.Visibility = Visibility.Visible;
    }

    private void ClearStatus()
    {
        StatusMessage.Text = string.Empty;
        StatusMessageContainer.Visibility = Visibility.Collapsed;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var settingsWin = new SettingsWindow(_profileManager) { Owner = this };
        settingsWin.ShowDialog();
        RefreshProfileList(_selectedItem?.Id);
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
