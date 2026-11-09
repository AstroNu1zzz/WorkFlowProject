using System.Text.RegularExpressions;
using Examen2.Data;
using Examen2.ViewModels;

namespace Examen2;

public partial class RegisterPage : ContentPage
{
    private readonly DatabaseService _databaseService;

    // Colores para el semáforo
    private readonly Color _colorRed = Color.FromArgb("#EF4444");
    private readonly Color _colorGreen = Color.FromArgb("#10B981");

    public RegisterPage(DatabaseService databaseService)
    {
        InitializeComponent();
        _databaseService = databaseService;
    }

    // EVENTO DE EVALUACIÓN EN TIEMPO REAL (SEMÁFORO)
    private void OnPasswordTextChanged(object sender, TextChangedEventArgs e)
    {
        string pwd = e.NewTextValue ?? string.Empty;

        // 1. Mínimo 8 caracteres
        bool hasMinLength = pwd.Length >= 8;
        LblReqLength.TextColor = hasMinLength ? _colorGreen : _colorRed;

        // 2. Al menos una mayúscula
        bool hasUpper = Regex.IsMatch(pwd, @"[A-Z]");
        LblReqUpper.TextColor = hasUpper ? _colorGreen : _colorRed;

        // 3. Al menos un número
        bool hasNumber = Regex.IsMatch(pwd, @"[0-9]");
        LblReqNumber.TextColor = hasNumber ? _colorGreen : _colorRed;

        // 4. Al menos un carácter especial
        bool hasSpecial = Regex.IsMatch(pwd, @"[@$!%*?&#._\-]");
        LblReqSpecial.TextColor = hasSpecial ? _colorGreen : _colorRed;
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        string username = TxtUsername.Text?.Trim();
        string email = TxtEmail.Text?.Trim();
        string password = TxtPassword.Text ?? string.Empty;
        string confirmPassword = TxtConfirmPassword.Text ?? string.Empty;

        // 1. Campos vacíos
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(email) ||
            string.IsNullOrEmpty(password) || string.IsNullOrEmpty(confirmPassword))
        {
            await DisplayAlert("Campos incompletos", "Por favor completa todos los campos requeridos.", "OK");
            return;
        }

        // 2. Filtro Nombre de Usuario (3 a 20 caracteres)
        if (username.Length < 3 || username.Length > 20 || !Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$"))
        {
            await DisplayAlert("Usuario inválido", "El usuario debe tener entre 3 y 20 caracteres sin espacios ni símbolos especiales.", "OK");
            return;
        }

        // 3. Filtro Correo Electrónico
        if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            await DisplayAlert("Correo inválido", "Ingresa una dirección de correo válida.", "OK");
            return;
        }

        // 4. Validación completa de los 4 criterios de contraseña
        bool isLengthOk = password.Length >= 8;
        bool isUpperOk = Regex.IsMatch(password, @"[A-Z]");
        bool isNumberOk = Regex.IsMatch(password, @"[0-9]");
        bool isSpecialOk = Regex.IsMatch(password, @"[@$!%*?&#._\-]");

        if (!isLengthOk || !isUpperOk || !isNumberOk || !isSpecialOk)
        {
            await DisplayAlert("Contraseña no válida", "La contraseña debe cumplir con todos los requisitos en verde.", "OK");
            return;
        }

        // 5. Coincidencia de Contraseñas
        if (password != confirmPassword)
        {
            await DisplayAlert("Error de contraseña", "Las contraseñas no coinciden.", "OK");
            return;
        }

        // Crear objeto de usuario
        var newUser = new User
        {
            Username = username,
            Email = email,
            Password = password
        };

        bool success = await _databaseService.RegisterUserAsync(newUser);

        if (success)
        {
            await DisplayAlert("Registro exitoso", "Tu cuenta ha sido creada correctamente. Ya puedes iniciar sesión.", "OK");
            await Navigation.PopAsync();
        }
        else
        {
            await DisplayAlert("Error", "El nombre de usuario o correo electrónico ya se encuentra registrado.", "OK");
        }
    }

    private async void OnBackToLoginClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}