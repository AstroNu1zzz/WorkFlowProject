using Examen2.Data;
using Examen2.ViewModels;

namespace Examen2;

public partial class CreateContentPage : ContentPage
{
    private readonly int _courseId;
    private readonly string _itemType;
    private readonly DatabaseService _databaseService;
    private readonly MainViewModel _mainViewModel;
    private string _selectedFilePath;
    private FileResult _pickedFileResult;
    private CourseUnit _autoDetectedUnit;

    public CreateContentPage(int courseId, string itemType, DatabaseService databaseService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _courseId = courseId;
        _itemType = itemType;
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;

        LblHeaderTitle.Text = $"Crear {_itemType}";
        bool isTarea = (_itemType == "Tarea");

        LayoutDueDate.IsVisible = isTarea;
        LayoutCriterionPicker.IsVisible = isTarea;
        DpDueDate.MinimumDate = DateTime.Today;

        _ = AutoDetectUnitAndLoadCriteriaAsync();
    }

    private async Task AutoDetectUnitAndLoadCriteriaAsync()
    {
        // Resuelve automáticamente la unidad vigente según la fecha actual
        _autoDetectedUnit = await _databaseService.GetActiveUnitForCourseAsync(_courseId, DateTime.Now);

        if (_autoDetectedUnit != null)
        {
            LblActiveUnitName.Text = $"{_autoDetectedUnit.Title} (Del {_autoDetectedUnit.StartDate:dd/MM} al {_autoDetectedUnit.EndDate:dd/MM})";
            LblActiveUnitName.TextColor = Color.FromArgb("#5EEAD4");

            // Cargar los criterios asociados a la unidad activa
            var criteria = await _databaseService.GetUnitCriteriaAsync(_autoDetectedUnit.Id);
            PickerCriteria.ItemsSource = criteria;

            if (criteria.Any())
            {
                LblCriterionWarning.IsVisible = false;
                PickerCriteria.SelectedIndex = 0;
                PickerCriteria.IsEnabled = true;
            }
            else
            {
                LblCriterionWarning.Text = "⚠️ Esta unidad no tiene criterios de evaluación configurados aún.";
                LblCriterionWarning.IsVisible = true;
                PickerCriteria.ItemsSource = null;
                PickerCriteria.IsEnabled = false;
            }
        }
        else
        {
            LblActiveUnitName.Text = "⚠️ No hay ninguna unidad activa para la fecha actual.";
            LblActiveUnitName.TextColor = Color.FromArgb("#EF4444");
            LblCriterionWarning.Text = "Primero debes configurar las unidades y sus fechas en la clase.";
            LblCriterionWarning.IsVisible = true;
            PickerCriteria.IsEnabled = false;
        }
    }

    private async void OnPickFileClicked(object sender, EventArgs e)
    {
        try
        {
            var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, new[] { ".pdf", ".jpg", ".jpeg", ".png" } },
                { DevicePlatform.Android, new[] { "application/pdf", "image/*" } }
            });

            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Selecciona una imagen o PDF",
                FileTypes = customFileType
            });

            if (result != null)
            {
                _pickedFileResult = result;
                _selectedFilePath = result.FullPath;
                LblAttachedFileName.Text = $"📎 {result.FileName}";
                LblAttachedFileName.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo seleccionar el archivo: {ex.Message}", "OK");
        }
    }

    private async void OnPublishClicked(object sender, EventArgs e)
    {
        string title = TxtTitle.Text?.Trim();
        string description = TxtDescription.Text?.Trim();

        if (string.IsNullOrEmpty(title))
        {
            await DisplayAlert("Campo requerido", "Ingresa un título para la publicación.", "OK");
            return;
        }

        var selectedCriterion = PickerCriteria.SelectedItem as UnitEvaluationCriterion;

        if (_itemType == "Tarea")
        {
            if (_autoDetectedUnit == null)
            {
                await DisplayAlert("Sin Unidad Activa", "No se puede publicar la tarea porque la fecha actual no coincide con ningún periodo de evaluación.", "OK");
                return;
            }

            if (selectedCriterion == null)
            {
                await DisplayAlert("Criterio Requerido", "Debes seleccionar un Criterio de Evaluación (ej. Examen, Tareas).", "OK");
                return;
            }
        }

        DateTime? dueDate = null;
        if (_itemType == "Tarea")
        {
            DateTime fechaBase = DpDueDate.Date ?? DateTime.Today;
            TimeSpan horaSeleccionada = TpDueTime.Time ?? new TimeSpan(23, 59, 0);
            dueDate = fechaBase.Date.Add(horaSeleccionada);
        }

        BtnPublish.IsEnabled = false;

        string attachedFilePublicUrl = null;

        if (_pickedFileResult != null || !string.IsNullOrEmpty(_selectedFilePath))
        {
            try
            {
                Stream fileStream = null;
                string fileName = "adjunto_profesor";

                if (_pickedFileResult != null)
                {
                    fileStream = await _pickedFileResult.OpenReadAsync();
                    fileName = _pickedFileResult.FileName;
                }
                else if (File.Exists(_selectedFilePath))
                {
                    fileStream = File.OpenRead(_selectedFilePath);
                    fileName = Path.GetFileName(_selectedFilePath);
                }

                if (fileStream != null)
                {
                    var (publicUrl, errorMessage) = await _databaseService.UploadFileStreamAsync(fileStream, fileName);
                    fileStream.Dispose();

                    if (!string.IsNullOrEmpty(errorMessage) || string.IsNullOrEmpty(publicUrl))
                    {
                        BtnPublish.IsEnabled = true;
                        await DisplayAlert("Error de carga", $"No se pudo subir el archivo adjunto: {errorMessage}", "OK");
                        return;
                    }

                    attachedFilePublicUrl = publicUrl;
                }
            }
            catch (Exception ex)
            {
                BtnPublish.IsEnabled = true;
                await DisplayAlert("Error", $"Ocurrió un error al procesar el archivo: {ex.Message}", "OK");
                return;
            }
        }

        var newItem = new ClassContentItem
        {
            CourseId = _courseId,
            ItemType = _itemType,
            Title = title,
            Description = description,
            DueDate = dueDate,
            UnitId = _autoDetectedUnit?.Id,
            CriterionId = selectedCriterion?.Id,
            AuthorName = _mainViewModel.CurrentUser?.Username ?? "Profesor",
            AttachedFilePath = attachedFilePublicUrl
        };

        bool success = await _databaseService.AddClassContentItemAsync(newItem);
        BtnPublish.IsEnabled = true;

        if (success)
        {
            await _mainViewModel.RefreshCoursesAsync();
            await Navigation.PopAsync();
        }
        else
        {
            await DisplayAlert("Error", "No se pudo crear la publicación en la nube.", "OK");
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}