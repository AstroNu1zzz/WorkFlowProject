using Examen2.Data;
using Examen2.ViewModels;

namespace Examen2;

public partial class AddCoursePage : ContentPage
{
    private readonly DatabaseService _databaseService;
    private readonly MainViewModel _mainViewModel;
    private FileResult _pickedBannerResult;

    // Ruta por defecto si el usuario no elige una imagen
    private string _selectedImagePath = "default_course_bg.jpg";

    public AddCoursePage(DatabaseService databaseService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;
    }

    private async void OnPickImageClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await MediaPicker.Default.PickPhotoAsync();

            if (result != null)
            {
                // Guardamos la referencia para subirla al bucket más adelante
                _pickedBannerResult = result;

                // Mostrar la vista previa directamente desde la ruta temporal
                ImgBannerPreview.Source = ImageSource.FromFile(result.FullPath);
                ImgBannerPreview.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo cargar la imagen: {ex.Message}", "OK");
        }
    }

    private async void OnCreateCourseClicked(object sender, EventArgs e)
    {
        string courseName = TxtCourseName.Text?.Trim();

        if (string.IsNullOrEmpty(courseName))
        {
            await DisplayAlert("Campo requerido", "Por favor ingresa el nombre de la clase.", "OK");
            return;
        }

        if (_mainViewModel.CurrentUser == null)
        {
            await DisplayAlert("Error", "No hay una sesión activa de usuario.", "OK");
            return;
        }

        string finalBannerUrl = _selectedImagePath;

        // Subir la imagen a Supabase si se seleccionó una
        if (_pickedBannerResult != null)
        {
            try
            {
                using (var stream = await _pickedBannerResult.OpenReadAsync())
                {
                    var (publicUrl, errorMessage) = await _databaseService.UploadFileStreamAsync(stream, _pickedBannerResult.FileName);

                    if (!string.IsNullOrEmpty(publicUrl))
                    {
                        finalBannerUrl = publicUrl;
                    }
                    else
                    {
                        await DisplayAlert("Advertencia", $"No se pudo subir la imagen: {errorMessage}. Se usará el fondo predeterminado.", "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Fallo al procesar la imagen: {ex.Message}", "OK");
                return;
            }
        }

        // Generar un código único de 6 caracteres para la clase
        string courseCode = Guid.NewGuid().ToString().Substring(0, 6).ToUpper();

        var newCourse = new Course
        {
            CourseName = courseName,
            BannerImagePath = finalBannerUrl, // URL de Supabase o fondo por defecto
            CourseCode = courseCode
        };

        bool success = await _databaseService.AddCourseAsync(newCourse, _mainViewModel.CurrentUser);

        if (success)
        {
            await _mainViewModel.RefreshCoursesAsync();
            var courses = await _databaseService.GetCoursesAsync(_mainViewModel.CurrentUser.Id);
            var createdCourse = courses?.FirstOrDefault(c => c.CourseCode == courseCode);

            if (createdCourse != null)
            {
                await Navigation.PushAsync(new ManageUnitsPage(createdCourse.Id, _databaseService));
            }
            else
            {
                await Navigation.PopAsync();
            }
        }
        else
        {
            await DisplayAlert("Error", "No se pudo crear la clase.", "OK");
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}