using System;
using Examen2.Data;
using Examen2.Services;
using Examen2.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Examen2;

public partial class App : Application
{
    private readonly DatabaseService _databaseService;
    private readonly EmailService _emailService;
    private readonly MainViewModel _mainViewModel;

    public App(DatabaseService databaseService, EmailService emailService, MainViewModel mainViewModel)
    {
        InitializeComponent();

        _databaseService = databaseService;
        _emailService = emailService;
        _mainViewModel = mainViewModel;

        // 1. Asignación inicial para evitar bloqueos en Android
        MainPage = new NavigationPage(new LoginPage(_databaseService, _emailService, _mainViewModel));

        // 2. Verificación asíncrona de sesión activa
        CheckSessionAsync();
    }

    private async void CheckSessionAsync()
    {
        try
        {
            bool isLoggedIn = Preferences.Default.Get("IsLoggedIn", false);
            int userId = Preferences.Default.Get("CurrentUserId", -1);
            DateTime last2FA = Preferences.Default.Get("Last2FAVerifiedAt", DateTime.MinValue);

            // CORRECCIÓN: Solo se considera expirado si se registró una fecha previa y pasaron más de 30 días
            bool is2FAExpired = last2FA != DateTime.MinValue && (DateTime.UtcNow - last2FA).TotalDays > 30;

            if (isLoggedIn && userId != -1 && !is2FAExpired)
            {
                var user = await _databaseService.GetUserByIdAsync(userId);

                if (user != null)
                {
                    _mainViewModel.CurrentUser = user;

                    // Redirección a la pantalla principal en el hilo de interfaz de usuario
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        MainPage = new NavigationPage(new MainPage(_databaseService, _mainViewModel));
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App Session Check Error]: {ex.Message}");
        }
    }
}