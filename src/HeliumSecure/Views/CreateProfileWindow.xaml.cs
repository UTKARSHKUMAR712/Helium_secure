using System.Windows;
using HeliumSecure.Core.Profiles;

namespace HeliumSecure.Views;

public partial class CreateProfileWindow : Window
{
    private readonly ProfileManager _profileManager;
    public Guid? CreatedProfileId { get; private set; }

    public CreateProfileWindow(ProfileManager profileManager)
    {
        InitializeComponent();
        _profileManager = profileManager;

        Loaded += OnLoaded;
        ProfileNameBox.Focus();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        double availableHeight = SystemParameters.WorkArea.Height;
        if (Owner != null && Owner.ActualHeight > 100)
        {
            availableHeight = Owner.ActualHeight;
        }

        // Keep maximum dialog height bounded by available screen or parent window height
        MaxHeight = Math.Max(260, availableHeight * 0.90);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        string name = ProfileNameBox.Text.Trim();
        string password = PasswordBox.Password;
        string confirm = ConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Please enter a profile name.");
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowError("Password cannot be empty.");
            return;
        }

        if (password != confirm)
        {
            ShowError("Passwords do not match.");
            return;
        }

        try
        {
            var profile = _profileManager.CreateProfile(name, password);
            CreatedProfileId = profile.Id;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorMessage.Visibility = Visibility.Visible;
    }
}
