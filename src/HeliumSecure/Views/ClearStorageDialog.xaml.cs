using System.Windows;
using HeliumSecure.Core.Profiles;
using HeliumSecure.Core.Storage;
using HeliumSecure.Helpers;

namespace HeliumSecure.Views;

public partial class ClearStorageDialog : Window
{
    private readonly ProfileManager _profileManager;
    private readonly Guid _profileId;

    public bool WasCleared { get; private set; }

    public ClearStorageDialog(ProfileManager profileManager, Guid profileId, string profileName)
    {
        InitializeComponent();
        _profileManager = profileManager;
        _profileId = profileId;

        WindowThemeHelper.EnableDarkTitleBar(this);

        ProfileSubtitle.Text = $"Targeting Profile: {profileName}";
        PasswordBox.Focus();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        string password = PasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            ShowError("Please enter your master password to decrypt and re-encrypt the vault.");
            return;
        }

        var options = new ClearStorageOptions(
            ClearCache: CacheCheckbox.IsChecked == true,
            ClearHistory: HistoryCheckbox.IsChecked == true,
            ClearCookiesAndSiteData: CookiesCheckbox.IsChecked == true,
            ClearDownloads: DownloadsCheckbox.IsChecked == true
        );

        if (!options.ClearCache && !options.ClearHistory && !options.ClearCookiesAndSiteData && !options.ClearDownloads)
        {
            ShowError("Please select at least one storage category to clear.");
            return;
        }

        ClearButton.IsEnabled = false;

        try
        {
            StorageService.ClearProfileStorage(_profileManager, _profileId, password, options);
            WasCleared = true;
            DialogResult = true;
            Close();
        }
        catch (ProfileAlreadyLockedException ex)
        {
            ShowError(ex.Message);
            ClearButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ShowError($"Failed to clear storage: {ex.Message}");
            ClearButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorContainer.Visibility = Visibility.Visible;
    }
}
