using Examen2.Data;
using Examen2.Services;
using Examen2.ViewModels;

namespace Examen2;

public partial class LoginPage : ContentPage
{
    private readonly DatabaseService _databaseService;
    private readonly EmailService _emailService;
    private readonly MainViewModel _mainViewModel;

    public LoginPage(DatabaseService databaseService, EmailService emailService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _emailService = emailService;
        _mainViewModel = mainViewModel;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        string usernameOrEmail = TxtUsernameOrEmail.Text?.Trim();
        string password = TxtPassword.Text;

        if (string.IsNullOrEmpty(usernameOrEmail) || string.IsNullOrEmpty(password))
        {
            await DisplayAlert("Campos incompletos", "Por favor ingresa tu usuario/correo y contraseña.", "OK");
            return;
        }

        var user = await _databaseService.ValidateUserAsync(usernameOrEmail, password);

        if (user == null)
        {
            await DisplayAlert("Error de inicio de sesión", "Usuario, correo o contraseña incorrectos.", "OK");
            return;
        }

        if (user.Is2FAEnabled)
        {
            string verificationCode = Random.Shared.Next(100000, 999999).ToString();

            // CORRECCIÓN CS0029: Desestructurar la tupla (bool Success, string ErrorDetail)
            var (sent, errorMsg) = await _emailService.Send2FACodeAsync(user.Email, verificationCode);
            if (!sent)
            {
                await DisplayAlert("Error de envío", $"No se pudo enviar el código 2FA a tu correo: {errorMsg}", "OK");
                return;
            }

            await DisplayAlert("Autenticación 2FA", $"Hemos enviado un código de acceso de 6 dígitos a tu correo electrónico ({user.Email}).", "OK");
            await Navigation.PushAsync(new TwoFactorPage(user, verificationCode, _databaseService, _mainViewModel));
        }
        else
        {
            Preferences.Default.Set("IsLoggedIn", true);
            Preferences.Default.Set("CurrentUserId", user.Id);
            Preferences.Default.Set("Last2FAVerifiedAt", DateTime.UtcNow);

            _mainViewModel.CurrentUser = user;
            Application.Current.MainPage = new NavigationPage(new MainPage(_databaseService, _mainViewModel));
        }
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new RegisterPage(_databaseService));
    }
}