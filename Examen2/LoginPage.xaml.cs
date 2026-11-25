using Examen2.Data;
using Examen2.Services;
using Examen2.ViewModels;

namespace Examen2;

public partial class LoginPage : ContentPage
{
    private readonly DatabaseService _databaseService;
    private readonly EmailService _emailService;
    private readonly MainViewModel _mainViewModel;
    private bool _isLoggingIn;

    public LoginPage(DatabaseService databaseService, EmailService emailService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _emailService = emailService;
        _mainViewModel = mainViewModel;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        if (_isLoggingIn) return;

        string? usernameOrEmail = TxtUsernameOrEmail.Text?.Trim();
        string? password = TxtPassword.Text;

        if (string.IsNullOrEmpty(usernameOrEmail) || string.IsNullOrEmpty(password))
        {
            await DisplayAlert("Campos incompletos", "Por favor ingresa tu usuario/correo y contraseña.", "OK");
            return;
        }

        _isLoggingIn = true;
        try
        {
            var (ok, requires2FA, token, user, errorMsg) = await _emailService.LoginAsync(usernameOrEmail, password);

            if (!ok || user == null)
            {
                await DisplayAlert(
                    "Error de inicio de sesión",
                    string.IsNullOrWhiteSpace(errorMsg) ? "Usuario, correo o contraseña incorrectos." : errorMsg,
                    "OK");
                return;
            }

            if (requires2FA)
            {
                var (sent, sendErr) = await _emailService.Send2FACodeAsync(user.Email);
                if (!sent)
                {
                    await DisplayAlert("Error de envío", $"No se pudo enviar el código 2FA a tu correo: {sendErr}", "OK");
                    return;
                }

                await DisplayAlert("Autenticación 2FA", $"Hemos enviado un código de acceso de 6 dígitos a tu correo electrónico ({user.Email}).", "OK");
                await Navigation.PushAsync(new TwoFactorPage(user, _emailService, _databaseService, _mainViewModel));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    await DisplayAlert("Error de sesión", "El servidor no devolvió un token de autenticación válido.", "OK");
                    return;
                }

                _databaseService.SetAuthToken(token);
                await SecureStorage.Default.SetAsync("AuthJwtToken", token);

                Preferences.Default.Set("IsLoggedIn", true);
                Preferences.Default.Set("CurrentUserId", user.Id);
                Preferences.Default.Set("Last2FAVerifiedAt", DateTime.UtcNow);

                _mainViewModel.SetCurrentUser(user);
                if (Application.Current != null)
                {
                    Application.Current.MainPage = new NavigationPage(new MainPage(_databaseService, _mainViewModel));
                }
            }
        }
        finally
        {
            _isLoggingIn = false;
        }
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new RegisterPage(_databaseService));
    }
}