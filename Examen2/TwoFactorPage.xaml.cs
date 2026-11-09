using Examen2.Data;
using Examen2.ViewModels;
using User = Examen2.Data.User;

namespace Examen2;

public partial class TwoFactorPage : ContentPage
{
    private readonly User _user;
    private readonly string _expectedCode;
    private readonly DatabaseService _databaseService;
    private readonly MainViewModel _mainViewModel;
    private readonly Func<Task> _onSuccessCallback;

    public TwoFactorPage(
        User user,
        string verificationCode,
        DatabaseService databaseService,
        MainViewModel mainViewModel,
        Func<Task> onSuccessCallback = null)
    {
        InitializeComponent();
        _user = user;
        _expectedCode = verificationCode;
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;
        _onSuccessCallback = onSuccessCallback;
    }

    private async void OnVerifyClicked(object sender, EventArgs e)
    {
        if (TxtCode.Text?.Trim() == _expectedCode)
        {
            Preferences.Default.Set("IsLoggedIn", true);
            Preferences.Default.Set("CurrentUserId", _user.Id);
            Preferences.Default.Set("Last2FAVerifiedAt", DateTime.UtcNow);

            _mainViewModel.CurrentUser = _user;

            // Si proviene de un flujo específico (ej. Cambiar contraseña / Verificar correo)
            if (_onSuccessCallback != null)
            {
                await Navigation.PopAsync(); // Cierra TwoFactorPage
                await _onSuccessCallback.Invoke(); // Ejecuta la acción pendiente
            }
            else
            {
                // Flujo normal de Inicio de Sesión
                await Navigation.PushAsync(new MainPage(_databaseService, _mainViewModel));
            }
        }
        else
        {
            await DisplayAlert("Error", "Código de verificación incorrecto.", "OK");
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}