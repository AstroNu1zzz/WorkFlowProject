using System.Text.RegularExpressions;
using Examen2.Data;
using Examen2.Services;
using Examen2.ViewModels;

namespace Examen2;

public partial class EditProfilePage : ContentPage
{
    private readonly DatabaseService _databaseService;
    private readonly EmailService _emailService;
    private readonly MainViewModel _mainViewModel;
    private bool _isEmailVerified = false;

    public EditProfilePage(DatabaseService databaseService, EmailService emailService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _emailService = emailService;
        _mainViewModel = mainViewModel;

        LoadCurrentProfile();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadCurrentProfile();
    }

    private void LoadCurrentProfile()
    {
        var user = _mainViewModel?.CurrentUser;
        if (user != null)
        {
            TxtUsername.Text = user.Username;
            LblCurrentEmail.Text = user.Email;
        }
    }

    // 1. ACTUALIZAR NOMBRE DE USUARIO
    private async void OnUpdateUsernameClicked(object sender, EventArgs e)
    {
        string newUsername = TxtUsername.Text?.Trim();

        if (string.IsNullOrWhiteSpace(newUsername) || newUsername.Length < 3 || newUsername.Length > 20)
        {
            await DisplayAlert("Error", "El nombre de usuario debe contener entre 3 y 20 caracteres.", "OK");
            return;
        }

        if (!Regex.IsMatch(newUsername, @"^[a-zA-Z0-9_]+$"))
        {
            await DisplayAlert("Error", "El usuario solo puede contener letras, números y guiones bajos.", "OK");
            return;
        }

        var user = _mainViewModel.CurrentUser;
        user.Username = newUsername;

        bool ok = await _databaseService.UpdateUserAsync(user);
        if (ok)
        {
            await DisplayAlert("Éxito", "Nombre de usuario actualizado correctamente.", "OK");
        }
        else
        {
            await DisplayAlert("Error", "No se pudo actualizar el nombre de usuario.", "OK");
        }
    }

    // 2. VERIFICAR CORREO MEDIANTE EMAILSERVICE Y TWOFACTORPAGE
    private async void OnVerifyEmailClicked(object sender, EventArgs e)
    {
        if (_isEmailVerified)
        {
            await DisplayAlert("Información", "Tu correo ya se encuentra verificado.", "OK");
            return;
        }

        var user = _mainViewModel.CurrentUser;
        if (user == null) return;

        string generatedCode = Random.Shared.Next(100000, 999999).ToString();

        // CORRECCIÓN CS0029: Desestructurar la tupla (bool Success, string ErrorDetail)
        var (sent, errorMsg) = await _emailService.Send2FACodeAsync(user.Email, generatedCode);
        if (!sent)
        {
            await DisplayAlert("Error de Envío", $"No se pudo enviar el correo de verificación: {errorMsg}", "OK");
            return;
        }

        await DisplayAlert("Correo Enviado", $"Hemos enviado un código de verificación de 6 dígitos a {user.Email}.", "OK");

        await Navigation.PushAsync(new TwoFactorPage(
            user,
            generatedCode,
            _databaseService,
            _mainViewModel,
            onSuccessCallback: async () =>
            {
                _isEmailVerified = true;
                LblEmailVerifiedStatus.Text = "✓ Verificado";
                LblEmailVerifiedStatus.TextColor = Color.FromArgb("#5EEAD4");
                await DisplayAlert("Éxito", "Correo electrónico verificado correctamente.", "OK");
            }));
    }

    // 3. CAMBIAR CORREO (SOLICITA CONTRASEÑA ACTUAL)
    private async void OnChangeEmailClicked(object sender, EventArgs e)
    {
        var user = _mainViewModel.CurrentUser;
        if (user == null) return;

        string currentPasswordInput = await DisplayPromptAsync(
            "Seguridad Requerida",
            "Ingresa tu contraseña actual para cambiar el correo:",
            accept: "Continuar",
            cancel: "Cancelar",
            placeholder: "••••••••");

        if (string.IsNullOrEmpty(currentPasswordInput)) return;

        if (currentPasswordInput != user.Password)
        {
            await DisplayAlert("Acceso Denegado", "La contraseña ingresada es incorrecta.", "OK");
            return;
        }

        string newEmail = await DisplayPromptAsync(
            "Nuevo Correo",
            "Ingresa tu nuevo correo electrónico:",
            accept: "Guardar",
            cancel: "Cancelar",
            placeholder: "ejemplo@correo.com",
            keyboard: Keyboard.Email);

        if (string.IsNullOrWhiteSpace(newEmail)) return;

        if (!Regex.IsMatch(newEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            await DisplayAlert("Error", "El correo ingresado no es válido.", "OK");
            return;
        }

        user.Email = newEmail;
        bool ok = await _databaseService.UpdateUserAsync(user);

        if (ok)
        {
            LblCurrentEmail.Text = newEmail;
            _isEmailVerified = false;
            LblEmailVerifiedStatus.Text = "Sin verificar";
            LblEmailVerifiedStatus.TextColor = Color.FromArgb("#F87171");
            await DisplayAlert("Éxito", "Correo electrónico actualizado correctamente.", "OK");
        }
        else
        {
            await DisplayAlert("Error", "No se pudo actualizar el correo.", "OK");
        }
    }

    // 4. CAMBIAR CONTRASEÑA ENVIANDO CÓDIGO REAL CON EMAILSERVICE
    private async void OnChangePassword2FAClicked(object sender, EventArgs e)
    {
        var user = _mainViewModel.CurrentUser;
        if (user == null) return;

        string generatedCode = Random.Shared.Next(100000, 999999).ToString();

        // CORRECCIÓN CS0029: Desestructurar la tupla (bool Success, string ErrorDetail)
        var (sent, errorMsg) = await _emailService.Send2FACodeAsync(user.Email, generatedCode);
        if (!sent)
        {
            await DisplayAlert("Error de Envío", $"No se pudo enviar el código 2FA: {errorMsg}", "OK");
            return;
        }

        await DisplayAlert("Código Enviado", $"Se ha enviado un código de autenticación de 6 dígitos a tu correo ({user.Email}).", "OK");

        await Navigation.PushAsync(new TwoFactorPage(
            user,
            generatedCode,
            _databaseService,
            _mainViewModel,
            onSuccessCallback: async () =>
            {
                await PromptNewPasswordAsync(user);
            }));
    }

    private async Task PromptNewPasswordAsync(User user)
    {
        string newPassword = await DisplayPromptAsync(
            "Nueva Contraseña",
            "Ingresa tu nueva contraseña (mínimo 8 caracteres, mayúscula, número y símbolo):",
            accept: "Siguiente",
            cancel: "Cancelar",
            placeholder: "••••••••");

        if (string.IsNullOrEmpty(newPassword)) return;

        bool isLengthOk = newPassword.Length >= 8;
        bool isUpperOk = Regex.IsMatch(newPassword, @"[A-Z]");
        bool isNumberOk = Regex.IsMatch(newPassword, @"[0-9]");
        bool isSpecialOk = Regex.IsMatch(newPassword, @"[@$!%*?&#._\-]");

        if (!isLengthOk || !isUpperOk || !isNumberOk || !isSpecialOk)
        {
            await DisplayAlert("Contraseña Débil", "La contraseña debe contener mínimo 8 caracteres, una mayúscula, un número y un símbolo.", "OK");
            return;
        }

        string confirmPassword = await DisplayPromptAsync(
            "Confirmar Contraseña",
            "Vuelve a ingresar la nueva contraseña:",
            accept: "Cambiar",
            cancel: "Cancelar",
            placeholder: "••••••••");

        if (newPassword != confirmPassword)
        {
            await DisplayAlert("Error", "Las contraseñas no coinciden.", "OK");
            return;
        }

        user.Password = newPassword;
        bool ok = await _databaseService.UpdateUserAsync(user);

        if (ok)
        {
            await DisplayAlert("Éxito", "Tu contraseña ha sido actualizada correctamente.", "OK");
        }
        else
        {
            await DisplayAlert("Error", "No se pudo actualizar la contraseña en la base de datos.", "OK");
        }
    }
}