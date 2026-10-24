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
using HeliumSecure.Core.Storage;
using HeliumSecure.Helpers;
using HeliumSecure.Views;

namespace HeliumSecure;

public sealed class ProfileDisplayItem
{
    private readonly ProfileManager _profileManager;

    public ProfileInfo Profile { get; }
    public Guid Id => Profile.Id;
    public string Name => Profile.Name;
    public string LastUsedText => $"Last used: {Profile.LastUsedAt.ToLocalTime():yyyy-MM-dd HH:mm}";

    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return "VP";
            var parts = Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                return parts[0].Length >= 2
                    ? parts[0].Substring(0, 2).ToUpperInvariant()
                    : parts[0].ToUpperInvariant();
            }
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
        }
    }

    public string StatusBadgeText
    {
        get
        {
            if (_profileManager.IsProfileLocked(Id)) return "Active";
            if (_profileManager.IsProfileLockedOut(Id, out var remaining, out _))
                return $"Locked ({(int)remaining.TotalMinutes}m)";
            _profileManager.IsProfileLockedOut(Id, out _, out bool stage2);
            if (stage2) return "Stage 2";
            return "Secured";
        }
    }

    public Brush StatusBadgeBg
    {
        get
        {
            if (_profileManager.IsProfileLocked(Id)) return new SolidColorBrush(Color.FromRgb(38, 30, 16));
            if (_profileManager.IsProfileLockedOut(Id, out _, out _)) return new SolidColorBrush(Color.FromRgb(43, 18, 24));
            _profileManager.IsProfileLockedOut(Id, out _, out bool stage2);
            if (stage2) return new SolidColorBrush(Color.FromRgb(43, 24, 8));
            return new SolidColorBrush(Color.FromRgb(18, 24, 38));
        }
    }

    public Brush StatusBadgeBorder
    {
        get
        {
            if (_profileManager.IsProfileLocked(Id)) return new SolidColorBrush(Color.FromRgb(95, 68, 20));
            if (_profileManager.IsProfileLockedOut(Id, out _, out _)) return new SolidColorBrush(Color.FromRgb(225, 29, 72));
            _profileManager.IsProfileLockedOut(Id, out _, out bool stage2);
            if (stage2) return new SolidColorBrush(Color.FromRgb(217, 119, 6));
            return new SolidColorBrush(Color.FromRgb(30, 41, 66));
        }
    }

    public Brush StatusBadgeFg
    {
        get
        {
            if (_profileManager.IsProfileLocked(Id)) return new SolidColorBrush(Color.FromRgb(251, 191, 36));
            if (_profileManager.IsProfileLockedOut(Id, out _, out _)) return new SolidColorBrush(Color.FromRgb(251, 113, 133));
            _profileManager.IsProfileLockedOut(Id, out _, out bool stage2);
            if (stage2) return new SolidColorBrush(Color.FromRgb(251, 191, 36));
            return new SolidColorBrush(Color.FromRgb(129, 140, 248));
        }
    }

    public ProfileDisplayItem(ProfileInfo profile, ProfileManager profileManager)
    {
        Profile = profile;
        _profileManager = profileManager;
    }
}

public partial class MainWindow : Window
{
    private readonly ProfileManager _profileManager;
    private readonly GuestSessionManager _guestManager;
    private readonly System.Windows.Threading.DispatcherTimer _lockoutCountdownTimer;
    private string? _heliumExecutablePath;
    private bool _isPasswordRevealed;
    private ProfileDisplayItem? _selectedItem;
    private List<ProfileDisplayItem> _allProfiles = new();

    private static readonly Geometry EyeOpenGeometry = Geometry.Parse("M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5c-1.73-4.39-6-7.5-11-7.5zM12 17c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z");
    private static readonly Geometry EyeClosedGeometry = Geometry.Parse("M12 7c2.76 0 5 2.24 5 5 0 .65-.13 1.26-.36 1.83l2.92 2.92c1.51-1.26 2.7-2.89 3.44-4.75-1.73-4.39-6-7.5-11-7.5-1.4 0-2.74.25-3.98.7l2.16 2.16C10.74 7.13 11.35 7 12 7zM2 4.27l2.28 2.28.46.46C3.08 8.3 1.78 10.02 1 12c1.73 4.39 6 7.5 11 7.5 1.55 0 3.03-.3 4.38-.84l.42.42L19.73 22 21 20.73 3.27 3 2 4.27zM7.53 9.8l1.55 1.55c-.05.21-.08.43-.08.65 0 1.66 1.34 3 3 3 .22 0 .44-.03.65-.08l1.55 1.55c-.67.33-1.41.53-2.2.53-2.76 0-5-2.24-5-5 0-.79.2-1.53.53-2.2zm4.31-.78l3.15 3.15.02-.16c0-1.66-1.34-3-3-3l-.17.01z");

    public MainWindow()
    {
        InitializeComponent();

        WindowThemeHelper.EnableDarkTitleBar(this);

        _profileManager = new ProfileManager();
        _guestManager = new GuestSessionManager();

        _lockoutCountdownTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _lockoutCountdownTimer.Tick += OnLockoutTimerTick;

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
            BrowserStatusPill.Text = "Helium 0.20.1.1 Linked";
            BrowserStatusPill.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Emerald green
            BrowserStatusDot.Fill = new SolidColorBrush(Color.FromRgb(52, 211, 153));
        }
        else
        {
            BrowserStatusPill.Text = "Helium Not Linked";
            BrowserStatusPill.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)); // Amber
            BrowserStatusDot.Fill = new SolidColorBrush(Color.FromRgb(251, 191, 36));
        }
    }

    private void PromptCreateFirstProfile()
    {
        var createWin = new CreateProfileWindow(_profileManager) { Owner = this };
        if (createWin.ShowDialog() == true && createWin.CreatedProfileId.HasValue)
        {
            RefreshProfileList(createWin.CreatedProfileId.Value);
            NavVaultsButton.IsChecked = true;
        }
    }

    private void RefreshProfileList(Guid? selectId = null)
    {
        var profiles = _profileManager.ListProfiles()
            .OrderByDescending(p => p.LastUsedAt)
            .Select(p => new ProfileDisplayItem(p, _profileManager))
            .ToList();

        _allProfiles = profiles;
        FilterProfiles(VaultSearchBox?.Text);

        UpdateDashboardMetrics();

        if (profiles.Count > 0)
        {
            if (selectId.HasValue)
            {
                var match = profiles.FirstOrDefault(p => p.Id == selectId.Value);
                ProfilesListBox.SelectedItem = match ?? profiles[0];
            }
            else if (ProfilesListBox.SelectedItem == null)
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

    private void FilterProfiles(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ProfilesListBox.ItemsSource = _allProfiles;
            VaultsEmptyState.Visibility = _allProfiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            var filtered = _allProfiles
                .Where(p => p.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            ProfilesListBox.ItemsSource = filtered;
            VaultsEmptyState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateDashboardMetrics()
    {
        int count = _allProfiles.Count;
        DashVaultCountText.Text = count == 1 ? "1 Profile" : $"{count} Profiles";

        bool anyActive = _allProfiles.Any(p => _profileManager.IsProfileLocked(p.Id));
        if (anyActive)
        {
            DashActiveSessionText.Text = "1 Active";
            DashActiveSessionText.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)); // Amber
        }
        else
        {
            DashActiveSessionText.Text = "All Locked";
            DashActiveSessionText.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Emerald
        }

        var recentList = _allProfiles.Take(3).ToList();
        DashRecentVaultsList.ItemsSource = recentList;
        DashEmptyVaultsPrompt.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNavTabChecked(object sender, RoutedEventArgs e)
    {
        if (DashboardPage == null || VaultsPage == null || SecurityPage == null || SettingsPage == null)
            return;

        DashboardPage.Visibility = Visibility.Collapsed;
        VaultsPage.Visibility = Visibility.Collapsed;
        SecurityPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;

        if (NavDashboardButton.IsChecked == true)
        {
            DashboardPage.Visibility = Visibility.Visible;
            HeaderTitleText.Text = "Dashboard";
            HeaderSubtitleText.Text = "Overview of hardware-isolated browser vaults and session status";
            UpdateDashboardMetrics();
        }
        else if (NavVaultsButton.IsChecked == true)
        {
            VaultsPage.Visibility = Visibility.Visible;
            HeaderTitleText.Text = "Profile Vaults";
            HeaderSubtitleText.Text = "Unlock, authenticate, and manage AES-256-GCM encrypted profiles";
            UpdateDetailsView();
        }
        else if (NavSecurityButton.IsChecked == true)
        {
            SecurityPage.Visibility = Visibility.Visible;
            HeaderTitleText.Text = "Security Architecture";
            HeaderSubtitleText.Text = "Factual specifications: Argon2id, AES-256-GCM, and attempt state machine";
        }
        else if (NavSettingsButton.IsChecked == true)
        {
            SettingsPage.Visibility = Visibility.Visible;
            HeaderTitleText.Text = "Storage & Settings";
            HeaderSubtitleText.Text = "Accurate filesystem measurements, inventory breakdown, and cache pruning";
            _ = LoadSettingsStorageDataAsync();
        }
    }

    private void OnVaultSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        FilterProfiles(VaultSearchBox.Text);
    }

    private void OnViewAllVaultsClick(object sender, RoutedEventArgs e)
    {
        NavVaultsButton.IsChecked = true;
    }

    private void OnQuickUnlockClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid profileId)
        {
            NavVaultsButton.IsChecked = true;
            RefreshProfileList(profileId);
            ProfilePasswordBox.Focus();
        }
    }

    private void OnLockoutTimerTick(object? sender, EventArgs e)
    {
        if (_selectedItem == null)
        {
            _lockoutCountdownTimer.Stop();
            return;
        }

        if (_profileManager.IsProfileLockedOut(_selectedItem.Id, out var remaining, out _))
        {
            LockoutCountdownText.Text = $"{(int)remaining.TotalMinutes}m {remaining.Seconds:D2}s remaining";
        }
        else
        {
            _lockoutCountdownTimer.Stop();
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
            NoSelectionPrompt.Visibility = Visibility.Visible;
            return;
        }

        NoSelectionPrompt.Visibility = Visibility.Collapsed;
        ProfileDetailsPanel.Visibility = Visibility.Visible;
        SelectedProfileName.Text = _selectedItem.Name;
        SelectedProfileInitials.Text = _selectedItem.Initials;
        SelectedProfileIdSnippet.Text = $"ID: {_selectedItem.Id:D}";

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
            SelectedProfileStatus.Foreground = new SolidColorBrush(Color.FromRgb(129, 140, 248));
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(99, 102, 241));
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(20, 26, 44));
            StatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(35, 48, 80));

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
                        UpdateDashboardMetrics();
                    });
                });

            ShowStatus("Browser active. Vault will securely re-encrypt on exit.", isError: false);
            UpdateDetailsView();
            UpdateDashboardMetrics();
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

    private void OnNewProfileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateProfileWindow(_profileManager) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.CreatedProfileId.HasValue)
        {
            RefreshProfileList(dialog.CreatedProfileId.Value);
            NavVaultsButton.IsChecked = true;
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

    private void OnChangePasswordClick(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;

        if (_profileManager.IsProfileLocked(_selectedItem.Id))
        {
            ShowStatus("Cannot change password while the browser session is active.", isError: true);
            return;
        }

        if (_profileManager.IsProfileLockedOut(_selectedItem.Id, out var remaining, out _))
        {
            ShowStatus($"Cannot change password while profile is locked out ({(int)remaining.TotalMinutes}m remaining).", isError: true);
            return;
        }

        var dialog = new ChangePasswordWindow(_profileManager, _selectedItem.Id, _selectedItem.Name) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ShowStatus("Master password updated successfully. Vault re-encrypted with fresh salt.", isError: false);
            ProfilePasswordBox.Password = string.Empty;
            ProfilePasswordTextBox.Text = string.Empty;
            UpdateDetailsView();
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

    private void OnClearStorageClick(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;

        var dialog = new ClearStorageDialog(_profileManager, _selectedItem.Id, _selectedItem.Name) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.WasCleared)
        {
            ShowStatus("Selected storage categories pruned successfully.", isError: false);
            RefreshProfileList(_selectedItem.Id);
        }
    }

    private void OnSettingsClearStorageClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid profileId)
        {
            var profile = _allProfiles.FirstOrDefault(p => p.Id == profileId);
            string name = profile?.Name ?? "Selected Profile";
            var dialog = new ClearStorageDialog(_profileManager, profileId, name) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.WasCleared)
            {
                _ = LoadSettingsStorageDataAsync();
            }
        }
    }

    private void OnSettingsRefreshClick(object sender, RoutedEventArgs e)
    {
        _ = LoadSettingsStorageDataAsync();
    }

    private async Task LoadSettingsStorageDataAsync()
    {
        SettingsRefreshBtn.IsEnabled = false;

        try
        {
            var overview = await Task.Run(() => StorageService.GetStorageOverview());

            SettingsInstalledAppText.Text = overview.InstalledAppFormatted;
            SettingsBundledBrowserText.Text = overview.BundledBrowserFormatted;
            SettingsLauncherText.Text = overview.LauncherFormatted;

            SettingsTotalProfilesText.Text = overview.TotalProfilesFormatted;
            SettingsVaultsBackupsText.Text = overview.TotalVaultsAndBackupsFormatted;
            SettingsCrashTempText.Text = overview.TempAndCrashRecoveryFormatted;

            SettingsCombinedFootprintText.Text = overview.TotalFootprintFormatted;

            if (overview.IsMeasurementComplete)
            {
                SettingsAccountingStatusText.Text = "Verified & Complete";
                SettingsAccountingStatusText.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            }
            else
            {
                SettingsAccountingStatusText.Text = "Incomplete (Partial)";
                SettingsAccountingStatusText.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            }

            var profiles = _profileManager.ListProfiles();
            var profileDetailsList = await Task.Run(() =>
            {
                var list = new List<ProfileStorageDisplayItem>();
                foreach (var p in profiles)
                {
                    try
                    {
                        var details = StorageService.GetProfileStorageDetails(_profileManager, p.Id);
                        list.Add(new ProfileStorageDisplayItem(details, _profileManager));
                    }
                    catch { }
                }
                return list;
            });

            SettingsProfilesList.ItemsSource = profileDetailsList;
        }
        catch (Exception ex)
        {
            ShowStatus($"Error updating storage overview: {ex.Message}", isError: true);
        }
        finally
        {
            SettingsRefreshBtn.IsEnabled = true;
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
                    UpdateDashboardMetrics();
                });
            });
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to start guest session: {ex.Message}", isError: true);
        }
    }
}
