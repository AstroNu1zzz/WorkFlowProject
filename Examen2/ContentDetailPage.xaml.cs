using Examen2.Data;
using Examen2.ViewModels;

namespace Examen2;

public partial class ContentDetailPage : ContentPage
{
    private readonly ClassContentItem _item;
    private readonly DatabaseService _databaseService;
    private readonly MainViewModel _mainViewModel;
    private string _submissionFilePath;
    private FileResult _pickedFileResult;

    // Propiedad auxiliar para verificar en tiempo real si la tarea ya venció
    private bool IsTaskOverdue => _item.ItemType == "Tarea"
                                  && _item.DueDate.HasValue
                                  && DateTime.Now > _item.DueDate.Value;

    public ContentDetailPage(ClassContentItem item, DatabaseService databaseService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _item = item;
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;

        LoadDetails();
    }

    private async void LoadDetails()
    {
        LblTopType.Text = _item.ItemType;
        LblTitle.Text = _item.Title;
        LblDescription.Text = string.IsNullOrEmpty(_item.Description) ? "Sin instrucciones." : _item.Description;

        bool esTarea = _item.ItemType == "Tarea";

        StackPoints.IsVisible = esTarea;
        LblPoints.Text = $"{_item.Points} puntos";

        if (esTarea)
        {
            if (_item.DueDate.HasValue)
            {
                LblDueDate.Text = IsTaskOverdue
                    ? $"Venció el: {_item.DueDate.Value:dd/MM/yyyy HH:mm}"
                    : $"Fecha de entrega: {_item.DueDate.Value:dd/MM/yyyy HH:mm}";
                LblDueDate.IsVisible = true;
            }
            BorderStudentSubmission.IsVisible = true;
            LblSubmissionStatusTop.IsVisible = true;
            await LoadStudentSubmissionAsync();
        }
        else
        {
            BorderStudentSubmission.IsVisible = false;
            LblSubmissionStatusTop.IsVisible = false;
        }

        bool isUrl = !string.IsNullOrEmpty(_item.AttachedFilePath) &&
                     (_item.AttachedFilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                      _item.AttachedFilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        bool isLocalFile = !string.IsNullOrEmpty(_item.AttachedFilePath) && File.Exists(_item.AttachedFilePath);

        bool hasTeacherFile = isUrl || isLocalFile;

        LayoutTeacherAttachmentSection.IsVisible = hasTeacherFile;
        LayoutTeacherAttachment.IsVisible = hasTeacherFile;

        if (hasTeacherFile)
        {
            LblTeacherFileName.Text = Path.GetFileName(_item.AttachedFilePath);
        }
    }

    private async Task LoadStudentSubmissionAsync()
    {
        if (_mainViewModel?.CurrentUser == null) return;

        var sub = await _databaseService.GetTaskSubmissionAsync(_item.Id, _mainViewModel.CurrentUser.Id);
        bool hasSubmission = sub != null && !string.IsNullOrEmpty(sub.FilePath);

        if (hasSubmission)
        {
            _submissionFilePath = sub.FilePath;
            LblSubmittedFileName.Text = Path.GetFileName(sub.FilePath);
            LayoutStudentAttachment.IsVisible = true;

            if (sub.Grade.HasValue)
            {
                // CASO 1: Ya entregada y calificada
                LblSubmissionStatus.Text = "Calificado";
                LblSubmissionStatus.TextColor = Color.FromArgb("#5EEAD4");
                LblSubmissionStatusTop.Text = "Calificado";
                LblSubmissionStatusTop.TextColor = Color.FromArgb("#5EEAD4");

                BorderGradeBadge.IsVisible = true;
                LblGradeScore.Text = $"{sub.Grade.Value} / 100";

                if (!string.IsNullOrWhiteSpace(sub.Feedback))
                {
                    LblTeacherFeedback.Text = sub.Feedback;
                    BorderFeedback.IsVisible = true;
                }
                else
                {
                    BorderFeedback.IsVisible = false;
                }

                BtnSubmit.Text = "Tarea calificada";
                BtnSubmit.IsEnabled = false;
                BtnSubmit.BackgroundColor = Color.FromArgb("#2D3748");
                BtnSubmit.TextColor = Color.FromArgb("#94A3B8");
                BtnAddFile.IsVisible = false;
            }
            else if (IsTaskOverdue)
            {
                // CASO 2: Entregada en tiempo, pero el plazo ya cerró (bloquea anular entrega)
                LblSubmissionStatus.Text = "Entregado (Plazo cerrado)";
                LblSubmissionStatus.TextColor = Color.FromArgb("#5EEAD4");
                LblSubmissionStatusTop.Text = "Entregado";
                LblSubmissionStatusTop.TextColor = Color.FromArgb("#5EEAD4");

                BorderGradeBadge.IsVisible = false;
                BorderFeedback.IsVisible = false;

                BtnSubmit.Text = "Plazo finalizado";
                BtnSubmit.IsEnabled = false;
                BtnSubmit.BackgroundColor = Color.FromArgb("#2D3748");
                BtnSubmit.TextColor = Color.FromArgb("#94A3B8");
                BtnAddFile.IsVisible = false;
            }
            else
            {
                // CASO 3: Entregada y aún dentro del plazo (permite anular)
                LblSubmissionStatus.Text = "Entregado";
                LblSubmissionStatus.TextColor = Color.FromArgb("#5EEAD4");
                LblSubmissionStatusTop.Text = "Entregado";
                LblSubmissionStatusTop.TextColor = Color.FromArgb("#5EEAD4");

                BorderGradeBadge.IsVisible = false;
                BorderFeedback.IsVisible = false;

                BtnSubmit.Text = "Anular entrega";
                BtnSubmit.IsEnabled = true;
                BtnSubmit.BackgroundColor = Color.FromArgb("#D93025");
                BtnSubmit.TextColor = Colors.White;
                BtnAddFile.IsVisible = false;
            }
        }
        else
        {
            _submissionFilePath = null;
            LayoutStudentAttachment.IsVisible = false;
            BorderGradeBadge.IsVisible = false;
            BorderFeedback.IsVisible = false;

            if (IsTaskOverdue)
            {
                // CASO 4: NO entregada y fecha vencida (cierra controles)
                LblSubmissionStatus.Text = "Sin entregar • Cerrada";
                LblSubmissionStatus.TextColor = Color.FromArgb("#EF4444");
                LblSubmissionStatusTop.Text = "Vencida";
                LblSubmissionStatusTop.TextColor = Color.FromArgb("#EF4444");

                BorderStudentSubmission.Stroke = Color.FromArgb("#EF4444");

                BtnAddFile.IsVisible = false;
                BtnAddFile.IsEnabled = false;

                BtnSubmit.Text = "Tarea vencida";
                BtnSubmit.IsEnabled = false;
                BtnSubmit.BackgroundColor = Color.FromArgb("#EF4444");
                BtnSubmit.TextColor = Colors.White;
            }
            else
            {
                // CASO 5: Pendiente y vigente
                LblSubmissionStatus.Text = "Sin entregar";
                LblSubmissionStatus.TextColor = Color.FromArgb("#EF4444");
                LblSubmissionStatusTop.Text = "Asignada";
                LblSubmissionStatusTop.TextColor = Color.FromArgb("#5EEAD4");

                BtnSubmit.Text = "Entregar tarea";
                BtnSubmit.IsEnabled = true;
                BtnSubmit.BackgroundColor = Color.FromArgb("#5EEAD4");
                BtnSubmit.TextColor = Color.FromArgb("#0F172A");
                BtnAddFile.IsVisible = true;
                BtnAddFile.IsEnabled = true;
            }
        }
    }

    private async void OnAddFileClicked(object sender, EventArgs e)
    {
        if (IsTaskOverdue)
        {
            await DisplayAlert("Tarea vencida", "El plazo de entrega ha finalizado y ya no se pueden adjuntar archivos.", "OK");
            await LoadStudentSubmissionAsync();
            return;
        }

        try
        {
            var result = await FilePicker.Default.PickAsync();
            if (result != null)
            {
                _pickedFileResult = result;
                _submissionFilePath = result.FullPath;
                LblSubmittedFileName.Text = result.FileName;
                LayoutStudentAttachment.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo seleccionar el archivo: {ex.Message}", "OK");
        }
    }

    private async void OnOpenTeacherFileClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_item.AttachedFilePath)) return;

        try
        {
            if (_item.AttachedFilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                _item.AttachedFilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                await Browser.Default.OpenAsync(new Uri(_item.AttachedFilePath), BrowserLaunchMode.SystemPreferred);
            }
            else if (File.Exists(_item.AttachedFilePath))
            {
                await Launcher.Default.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(_item.AttachedFilePath)
                });
            }
            else
            {
                await DisplayAlert("Atención", "El archivo no se encuentra disponible en este dispositivo.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo abrir el archivo: {ex.Message}", "OK");
        }
    }

    private async void OnOpenSubmittedFileClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_submissionFilePath))
        {
            await DisplayAlert("Atención", "No hay ningún archivo adjunto para visualizar.", "OK");
            return;
        }

        try
        {
            if (_submissionFilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                _submissionFilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                await Browser.Default.OpenAsync(new Uri(_submissionFilePath), BrowserLaunchMode.SystemPreferred);
            }
            else if (File.Exists(_submissionFilePath))
            {
                await Launcher.Default.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(_submissionFilePath)
                });
            }
            else
            {
                await DisplayAlert("Atención", "El archivo local no se encuentra disponible en este dispositivo.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo abrir el archivo: {ex.Message}", "OK");
        }
    }

    private async void OnSubmitTaskClicked(object sender, EventArgs e)
    {
        if (BtnSubmit.Text == "Tarea calificada" || BtnSubmit.Text == "Tarea vencida" || BtnSubmit.Text == "Plazo finalizado")
            return;

        // Validación en tiempo real por si el reloj cruzó la hora límite mientras la pantalla estaba abierta
        if (IsTaskOverdue)
        {
            await DisplayAlert("Plazo vencido", "El tiempo límite para esta tarea ha expirado.", "OK");
            await LoadStudentSubmissionAsync();
            return;
        }

        if (BtnSubmit.Text == "Anular entrega")
        {
            bool confirm = await DisplayAlert("Anular entrega", "¿Quieres anular la entrega de esta tarea?", "Sí, anular", "Cancelar");
            if (!confirm) return;

            _submissionFilePath = null;
            _pickedFileResult = null;
            LayoutStudentAttachment.IsVisible = false;
            LblSubmissionStatus.Text = "Sin entregar";
            LblSubmissionStatus.TextColor = Color.FromArgb("#EF4444");
            LblSubmissionStatusTop.Text = "Asignada";
            LblSubmissionStatusTop.TextColor = Color.FromArgb("#5EEAD4");
            BtnSubmit.Text = "Entregar tarea";
            BtnSubmit.BackgroundColor = Color.FromArgb("#5EEAD4");
            BtnSubmit.TextColor = Color.FromArgb("#0F172A");
            BtnAddFile.IsVisible = true;
            return;
        }

        if (_pickedFileResult == null && string.IsNullOrEmpty(_submissionFilePath))
        {
            await DisplayAlert("Sin archivo", "Primero debes adjuntar un archivo para entregar la tarea.", "OK");
            return;
        }

        BtnSubmit.IsEnabled = false;
        BtnSubmit.Text = "Subiendo archivo...";

        try
        {
            Stream fileStream = null;
            string fileName = "archivo_adjunto";

            if (_pickedFileResult != null)
            {
                fileStream = await _pickedFileResult.OpenReadAsync();
                fileName = _pickedFileResult.FileName;
            }
            else if (!string.IsNullOrEmpty(_submissionFilePath) && File.Exists(_submissionFilePath))
            {
                fileStream = File.OpenRead(_submissionFilePath);
                fileName = Path.GetFileName(_submissionFilePath);
            }

            var (publicUrl, errorMessage) = await _databaseService.UploadFileStreamAsync(fileStream, fileName);
            fileStream?.Dispose();

            if (!string.IsNullOrEmpty(errorMessage) || string.IsNullOrEmpty(publicUrl))
            {
                await DisplayAlert("Error de Supabase", $"Detalle técnico: {errorMessage}", "OK");
                BtnSubmit.IsEnabled = true;
                BtnSubmit.Text = "Entregar tarea";
                return;
            }

            var sub = new TaskSubmission
            {
                ContentItemId = _item.Id,
                UserId = _mainViewModel.CurrentUser.Id,
                FilePath = publicUrl
            };

            var (ok, message) = await _databaseService.SaveTaskSubmissionAsync(sub);
            BtnSubmit.IsEnabled = true;

            if (ok)
            {
                await DisplayAlert("Éxito", message, "OK");
                await LoadStudentSubmissionAsync();
            }
            else
            {
                await DisplayAlert("No permitido", message, "OK");
                await LoadStudentSubmissionAsync();
            }
        }
        catch (Exception ex)
        {
            BtnSubmit.IsEnabled = true;
            BtnSubmit.Text = "Entregar tarea";
            await DisplayAlert("Error inesperado", ex.Message, "OK");
        }
    }
}