using Examen2.Data;
using Examen2.Services;
using Examen2.ViewModels;

namespace Examen2;

public partial class SettingsPage : ContentPage
{
    private DatabaseService _databaseService;
    private EmailService _emailService;
    private MainViewModel _mainViewModel;
    private bool _isLoadingTheme;
    private bool _isLoading2FA;

    public SettingsPage(DatabaseService databaseService = null, EmailService emailService = null, MainViewModel mainViewModel = null)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _emailService = emailService;
        _mainViewModel = mainViewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        EnsureServices();
        LoadUserData();
        LoadCurrentThemeSelection();
    }

    private void EnsureServices()
    {
        var services = Handler?.MauiContext?.Services ?? Application.Current?.Handler?.MauiContext?.Services;
        _databaseService ??= services?.GetService<DatabaseService>();
        _emailService ??= services?.GetService<EmailService>();
        _mainViewModel ??= services?.GetService<MainViewModel>();
    }

    private void LoadUserData()
    {
        var user = _mainViewModel?.CurrentUser;
        if (user != null)
        {
            LblUsername.Text = user.Username;
            LblEmail.Text = user.Email;

            _isLoading2FA = true;
            Switch2FA.IsToggled = user.Is2FAEnabled;
            _isLoading2FA = false;

            if (!string.IsNullOrWhiteSpace(user.ProfilePicturePath))
            {
                ImgAvatar.Source = ImageSource.FromUri(new Uri(user.ProfilePicturePath));
                ImgAvatar.IsVisible = true;
                LblAvatarInitial.IsVisible = false;
            }
            else
            {
                ImgAvatar.IsVisible = false;
                LblAvatarInitial.IsVisible = true;

                if (!string.IsNullOrWhiteSpace(user.Username))
                {
                    LblAvatarInitial.Text = user.Username.Substring(0, 1).ToUpper();
                }
            }
        }
    }

    private async void On2FAToggled(object sender, ToggledEventArgs e)
    {
        if (_isLoading2FA) return;

        EnsureServices();
        var user = _mainViewModel?.CurrentUser;
        if (user == null || _databaseService == null) return;

        bool newState = e.Value;

        if (newState && !user.IsEmailVerified)
        {
            _isLoading2FA = true;
            Switch2FA.IsToggled = false;
            _isLoading2FA = false;

            await DisplayAlert(
                "Correo sin verificar",
                "Primero verifica tu correo electrónico en la sección Perfil antes de activar la verificación en dos pasos.",
                "OK");
            return;
        }

        Switch2FA.IsEnabled = false;
        bool updated = await _databaseService.Update2FAStatusAsync(user.Id, newState);
        Switch2FA.IsEnabled = true;

        if (updated)
        {
            user.Is2FAEnabled = newState;
        }
        else
        {
            _isLoading2FA = true;
            Switch2FA.IsToggled = !newState;
            _isLoading2FA = false;

            await DisplayAlert("Error", "No se pudo actualizar la configuración de 2FA en Supabase.", "OK");
        }
    }

    private void LoadCurrentThemeSelection()
    {
        _isLoadingTheme = true;

        string savedTheme = _mainViewModel?.CurrentUser?.AppThemePreference
                            ?? Preferences.Default.Get("UserAppTheme", "Sistema");

        if (savedTheme == "Claro")
            PickerTheme.SelectedIndex = 1;
        else if (savedTheme == "Oscuro")
            PickerTheme.SelectedIndex = 2;
        else
            PickerTheme.SelectedIndex = 0;

        _isLoadingTheme = false;
    }

    private async void OnThemeSelectedChanged(object sender, EventArgs e)
    {
        if (_isLoadingTheme || Application.Current == null) return;

        string selected = PickerTheme.SelectedItem as string ?? "Sistema";

        Application.Current.UserAppTheme = selected switch
        {
            "Claro" => AppTheme.Light,
            "Oscuro" => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };

        Preferences.Default.Set("UserAppTheme", selected);

        var user = _mainViewModel?.CurrentUser;
        if (user != null && _databaseService != null)
        {
            user.AppThemePreference = selected;
            await _databaseService.UpdateUserAsync(user);
        }
    }

    private async void OnBackButtonClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnProfileTapped(object sender, EventArgs e)
    {
        EnsureServices();
        await Navigation.PushAsync(new EditProfilePage(_databaseService, _emailService, _mainViewModel));
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        bool confirm = await DisplayAlert("Cerrar Sesión", "¿Estás seguro de que deseas salir?", "Sí", "Cancelar");
        if (confirm)
        {
            Preferences.Default.Remove("IsLoggedIn");
            Preferences.Default.Remove("CurrentUserId");
            Preferences.Default.Remove("Last2FAVerifiedAt");

            if (_mainViewModel != null)
            {
                _mainViewModel.CurrentUser = null;
            }

            Application.Current.MainPage = new NavigationPage(new LoginPage(_databaseService, _emailService, _mainViewModel));
        }
    }
}