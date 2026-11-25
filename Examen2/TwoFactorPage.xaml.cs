using Examen2.Data;
using Examen2.Services;
using Examen2.ViewModels;
using User = Examen2.Data.User;

namespace Examen2;

public partial class TwoFactorPage : ContentPage
{
    private readonly User _user;
    private readonly EmailService _emailService;
    private readonly DatabaseService _databaseService;
    private readonly MainViewModel _mainViewModel;
    private readonly Func<Task>? _onSuccessCallback;
    private bool _isVerifying;

    public TwoFactorPage(
        User user,
        EmailService emailService,
        DatabaseService databaseService,
        MainViewModel mainViewModel,
        Func<Task>? onSuccessCallback = null)
    {
        InitializeComponent();
        _user = user;
        _emailService = emailService;
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;
        _onSuccessCallback = onSuccessCallback;
    }

    private async void OnVerifyClicked(object sender, EventArgs e)
    {
        if (_isVerifying) return;

        string? enteredCode = TxtCode.Text?.Trim();
        if (string.IsNullOrWhiteSpace(enteredCode))
        {
            await DisplayAlert("Campo vacío", "Ingresa el código de 6 dígitos enviado a tu correo.", "OK");
            return;
        }

        _isVerifying = true;
        try
        {
            var (isValid, token, errorMsg) = await _emailService.Verify2FACodeAsync(_user.Email, enteredCode);

            if (!isValid)
            {
                await DisplayAlert("Error", string.IsNullOrWhiteSpace(errorMsg) ? "Código de verificación incorrecto." : errorMsg, "OK");
                return;
            }

            if (_onSuccessCallback == null && string.IsNullOrWhiteSpace(token))
            {
                await DisplayAlert("Error de sesión", "El servidor no devolvió un token de autenticación válido.", "OK");
                return;
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                _databaseService.SetAuthToken(token);
                await SecureStorage.Default.SetAsync("AuthJwtToken", token);
            }

            Preferences.Default.Set("IsLoggedIn", true);
            Preferences.Default.Set("CurrentUserId", _user.Id);
            Preferences.Default.Set("Last2FAVerifiedAt", DateTime.UtcNow);

            _mainViewModel.SetCurrentUser(_user);

            if (_onSuccessCallback != null)
            {
                await Navigation.PopAsync();
                await _onSuccessCallback.Invoke();
            }
            else if (Application.Current != null)
            {
                Application.Current.MainPage = new NavigationPage(new MainPage(_databaseService, _mainViewModel));
            }
        }
        finally
        {
            _isVerifying = false;
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}