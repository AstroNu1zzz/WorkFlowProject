using Examen2.Data;
using Examen2.Services;
using Examen2.ViewModels;

namespace Examen2;

public partial class SettingsPage : ContentPage
{
    private DatabaseService _databaseService;
    private EmailService _emailService;
    private MainViewModel _mainViewModel;

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
            LblEmail.Text = user.Email; // Correo fijo (solo lectura)

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
        if (Application.Current != null)
        {
            if (Application.Current.UserAppTheme == AppTheme.Light)
                PickerTheme.SelectedIndex = 1; // Claro
            else if (Application.Current.UserAppTheme == AppTheme.Dark)
                PickerTheme.SelectedIndex = 2; // Oscuro
            else
                PickerTheme.SelectedIndex = 0; // Sistema
        }
    }

    private void OnThemeSelectedChanged(object sender, EventArgs e)
    {
        if (Application.Current == null) return;

        string selected = PickerTheme.SelectedItem as string;
        if (selected == "Claro")
        {
            Application.Current.UserAppTheme = AppTheme.Light;
        }
        else if (selected == "Oscuro")
        {
            Application.Current.UserAppTheme = AppTheme.Dark;
        }
        else
        {
            Application.Current.UserAppTheme = AppTheme.Unspecified; // Sistema
        }
    }

    // ACCIÓN AL TOCAR LA TARJETA DE PERFIL (Permite editar nombre de usuario o cambiar la foto de perfil)
    private async void OnEditProfileTapped(object sender, EventArgs e)
    {
        var user = _mainViewModel?.CurrentUser;
        if (user == null) return;

        string action = await DisplayActionSheet("Editar Perfil", "Cancelar", null, "Cambiar nombre de usuario", "Cambiar foto de perfil");

        if (action == "Cambiar nombre de usuario")
        {
            string newUsername = await DisplayPromptAsync("Editar Nombre", "Actualiza tu nombre de usuario:", initialValue: user.Username, maxLength: 20);

            if (!string.IsNullOrWhiteSpace(newUsername) && newUsername.Trim() != user.Username)
            {
                user.Username = newUsername.Trim();
                bool success = await _databaseService.UpdateUserAsync(user);
                if (success)
                {
                    LoadUserData();
                    await DisplayAlert("Éxito", "Nombre de usuario actualizado correctamente.", "OK");
                }
                else
                {
                    await DisplayAlert("Error", "No se pudo actualizar el nombre de usuario.", "OK");
                }
            }
        }
        else if (action == "Cambiar foto de perfil")
        {
            try
            {
                var result = await MediaPicker.PickPhotoAsync(new MediaPickerOptions
                {
                    Title = "Selecciona tu nueva foto de perfil"
                });

                if (result != null)
                {
                    var localFilePath = result.FullPath;
                    user.ProfilePicturePath = localFilePath;
                    bool success = await _databaseService.UpdateUserAsync(user);

                    if (success)
                    {
                        LoadUserData();
                        await DisplayAlert("Éxito", "Foto de perfil actualizada correctamente.", "OK");
                    }
                    else
                    {
                        await DisplayAlert("Error", "No se pudo guardar la foto en la base de datos.", "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"No se pudo cargar la imagen: {ex.Message}", "OK");
            }
        }
    }

    private async void OnChangePasswordTapped(object sender, EventArgs e)
    {
        await DisplayAlert("Cambiar contraseña", "Te enviaremos un código de verificación a tu correo para restablecer tu contraseña.", "OK");
    }

    private async void OnBackButtonClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
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