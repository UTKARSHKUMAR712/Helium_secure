using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HeliumSecure.Core.Profiles;
using HeliumSecure.Core.Storage;
using HeliumSecure.Helpers;

namespace HeliumSecure.Views;

public sealed class ProfileStorageDisplayItem
{
    public ProfileStorageModel Model { get; }
    public ProfileManager ProfileManager { get; }

    public Guid ProfileId => Model.ProfileId;
    public string Name => Model.Name;
    public bool IsLocked => Model.IsLocked;

    public string TotalSizeFormatted => Model.TotalSizeFormatted;
    public string VaultSizeFormatted => Model.VaultSizeFormatted;
    public string BackupSizeFormatted => Model.BackupSizeFormatted;
    public string WorkingDirectoryFormatted => Model.WorkingDirectoryFormatted;
    public string CrashRecoveryFormatted => Model.CrashRecoveryFormatted;
    public string CreatedAtFormatted => Model.CreatedAtFormatted;
    public string LastUsedFormatted => Model.LastUsedFormatted;

    public bool IsLockedOut => ProfileManager.IsProfileLockedOut(ProfileId, out _, out _);
    public bool InSecondStage
    {
        get
        {
            ProfileManager.IsProfileLockedOut(ProfileId, out _, out bool stage2);
            return stage2;
        }
    }

    public string StatusText
    {
        get
        {
            if (IsLocked) return "Active Session";
            if (ProfileManager.IsProfileLockedOut(ProfileId, out var rem, out _))
                return $"Locked Out ({(int)rem.TotalMinutes}m left)";
            if (InSecondStage) return "Stage 2 Active";
            return "Secured at Rest";
        }
    }

    public Brush StatusBgBrush
    {
        get
        {
            if (IsLocked) return new SolidColorBrush(Color.FromRgb(38, 30, 16));
            if (IsLockedOut) return new SolidColorBrush(Color.FromRgb(43, 18, 24));
            if (InSecondStage) return new SolidColorBrush(Color.FromRgb(43, 24, 8));
            return new SolidColorBrush(Color.FromRgb(22, 32, 53));
        }
    }

    public Brush StatusBorderBrush
    {
        get
        {
            if (IsLocked) return new SolidColorBrush(Color.FromRgb(95, 68, 20));
            if (IsLockedOut) return new SolidColorBrush(Color.FromRgb(225, 29, 72));
            if (InSecondStage) return new SolidColorBrush(Color.FromRgb(217, 119, 6));
            return new SolidColorBrush(Color.FromRgb(35, 53, 85));
        }
    }

    public Brush StatusFgBrush
    {
        get
        {
            if (IsLocked) return new SolidColorBrush(Color.FromRgb(251, 191, 36));
            if (IsLockedOut) return new SolidColorBrush(Color.FromRgb(251, 113, 133));
            if (InSecondStage) return new SolidColorBrush(Color.FromRgb(251, 191, 36));
            return new SolidColorBrush(Color.FromRgb(147, 197, 253));
        }
    }

    public ProfileStorageDisplayItem(ProfileStorageModel model, ProfileManager pm)
    {
        Model = model;
        ProfileManager = pm;
    }
}

public partial class SettingsWindow : Window
{
    private readonly ProfileManager _profileManager;

    public SettingsWindow(ProfileManager profileManager)
    {
        InitializeComponent();
        _profileManager = profileManager;

        WindowThemeHelper.EnableDarkTitleBar(this);
        Loaded += async (s, e) => await LoadStorageDataAsync();
    }

    private async Task LoadStorageDataAsync()
    {
        RefreshButton.IsEnabled = false;

        try
        {
            var overview = await Task.Run(() => StorageService.GetStorageOverview());

            InstalledAppText.Text = overview.InstalledAppFormatted;
            BundledBrowserText.Text = overview.BundledBrowserFormatted;
            LauncherText.Text = overview.LauncherFormatted;

            TotalProfilesText.Text = overview.TotalProfilesFormatted;
            VaultsAndBackupsText.Text = overview.TotalVaultsAndBackupsFormatted;
            CrashAndTempText.Text = overview.TempAndCrashRecoveryFormatted;

            CombinedFootprintText.Text = overview.TotalFootprintFormatted;

            if (overview.IsMeasurementComplete)
            {
                AccountingStatusText.Text = "Verified & Complete";
                AccountingStatusText.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            }
            else
            {
                AccountingStatusText.Text = "Incomplete (Partial)";
                AccountingStatusText.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
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

            ProfilesInventoryList.ItemsSource = profileDetailsList;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await LoadStorageDataAsync();
    }

    private async void OnClearStorageItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid profileId)
        {
            var profile = _profileManager.ListProfiles().FirstOrDefault(p => p.Id == profileId);
            if (profile == null) return;

            if (_profileManager.IsProfileLocked(profileId))
            {
                MessageBox.Show(
                    "Cannot clear storage while the browser session is active. Please close Helium first.",
                    "Profile Active",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new ClearStorageDialog(_profileManager, profileId, profile.Name) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.WasCleared)
            {
                await LoadStorageDataAsync();
            }
        }
    }

    private async void OnDeleteProfileItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid profileId)
        {
            var profile = _profileManager.ListProfiles().FirstOrDefault(p => p.Id == profileId);
            if (profile == null) return;

            if (_profileManager.IsProfileLocked(profileId))
            {
                MessageBox.Show(
                    "Cannot delete profile while it is active or locked.",
                    "Profile Active",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new DeleteProfileWindow(_profileManager, profileId, profile.Name) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                // Clean any leftover crash directories for this profile
                StorageService.PurgeCrashDirectories(null, profileId);
                await LoadStorageDataAsync();
            }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
