using Examen2.Data;
using Examen2.Services;
using Examen2.ViewModels;

namespace Examen2;

public partial class EditProfilePage : ContentPage
{
    private readonly DatabaseService _databaseService;
    private readonly EmailService _emailService;
    private readonly MainViewModel _mainViewModel;

    public EditProfilePage(DatabaseService databaseService, EmailService emailService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _emailService = emailService;
        _mainViewModel = mainViewModel;

        LoadUserData();
    }

    private void LoadUserData()
    {
        var user = _mainViewModel?.CurrentUser;
        if (user != null)
        {
            TxtEditUsername.Text = user.Username;
            TxtEditEmail.Text = user.Email;

            if (!string.IsNullOrWhiteSpace(user.Username))
            {
                LblEditInitial.Text = user.Username.Substring(0, 1).ToUpper();
            }

            if (!string.IsNullOrWhiteSpace(user.ProfilePicturePath))
            {
                ImgEditAvatar.Source = ImageSource.FromUri(new Uri(user.ProfilePicturePath));
                ImgEditAvatar.IsVisible = true;
                LblEditInitial.IsVisible = false;
            }
        }
    }

    private async void OnBackButtonClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnChangePhotoClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await MediaPicker.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Selecciona tu foto de perfil"
            });

            if (result != null)
            {
                var localFilePath = result.FullPath;
                ImgEditAvatar.Source = ImageSource.FromFile(localFilePath);
                ImgEditAvatar.IsVisible = true;
                LblEditInitial.IsVisible = false;

                if (_mainViewModel?.CurrentUser != null)
                {
                    _mainViewModel.CurrentUser.ProfilePicturePath = localFilePath;
                    await _databaseService.UpdateUserAsync(_mainViewModel.CurrentUser);
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo cargar la imagen: {ex.Message}", "OK");
        }
    }

    private async void OnSaveProfileClicked(object sender, EventArgs e)
    {
        string newUsername = TxtEditUsername.Text?.Trim();
        string newEmail = TxtEditEmail.Text?.Trim();

        if (string.IsNullOrEmpty(newUsername) || string.IsNullOrEmpty(newEmail))
        {
            await DisplayAlert("Campos vacíos", "Por favor completa todos los campos.", "OK");
            return;
        }

        if (_mainViewModel?.CurrentUser != null)
        {
            _mainViewModel.CurrentUser.Username = newUsername;
            _mainViewModel.CurrentUser.Email = newEmail;

            bool success = await _databaseService.UpdateUserAsync(_mainViewModel.CurrentUser);
            if (success)
            {
                await DisplayAlert("Éxito", "Perfil actualizado correctamente.", "OK");
                await Navigation.PopAsync();
            }
            else
            {
                await DisplayAlert("Error", "No se pudo actualizar el perfil en la base de datos.", "OK");
            }
        }
    }
}