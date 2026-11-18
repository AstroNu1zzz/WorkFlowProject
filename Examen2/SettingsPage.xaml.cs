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

        // 1. Aplicar inmediatamente en la app
        Application.Current.UserAppTheme = selected switch
        {
            "Claro" => AppTheme.Light,
            "Oscuro" => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };

        // 2. Guardar respaldo local inmediato (para el arranque antes de leer Supabase)
        Preferences.Default.Set("UserAppTheme", selected);

        // 3. Guardar en Supabase en el perfil del usuario actual
        var user = _mainViewModel?.CurrentUser;
        if (user != null && _databaseService != null)
        {
            user.AppThemePreference = selected;
            await _databaseService.UpdateUserAsync(user);
        }
    }

    // EVENTO DEL BOTÓN REGRESAR
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