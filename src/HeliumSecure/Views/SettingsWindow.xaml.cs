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

    public string StatusText => IsLocked ? "Active Session" : "Secured at Rest";

    public Brush StatusBgBrush => IsLocked
        ? new SolidColorBrush(Color.FromRgb(38, 30, 16))
        : new SolidColorBrush(Color.FromRgb(22, 32, 53));

    public Brush StatusBorderBrush => IsLocked
        ? new SolidColorBrush(Color.FromRgb(95, 68, 20))
        : new SolidColorBrush(Color.FromRgb(35, 53, 85));

    public Brush StatusFgBrush => IsLocked
        ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
        : new SolidColorBrush(Color.FromRgb(147, 197, 253));

    public ProfileStorageDisplayItem(ProfileStorageModel model)
    {
        Model = model;
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
                        list.Add(new ProfileStorageDisplayItem(details));
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
