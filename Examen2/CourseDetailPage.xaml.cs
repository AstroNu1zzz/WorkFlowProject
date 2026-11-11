using System.Collections.ObjectModel;
using Examen2.Data;
using Examen2.ViewModels;

namespace Examen2;

// 1. MODELO DE GRUPO PARA COLLECTIONVIEW AGRUPADO EN PESTAÑA TRABAJOS
public class UnitGroupViewModel : List<FeedPostViewModel>
{
    public string UnitTitle { get; set; }
    public string DateRangeText { get; set; }

    public UnitGroupViewModel(string unitTitle, string dateRangeText, List<FeedPostViewModel> items) : base(items)
    {
        UnitTitle = unitTitle;
        DateRangeText = dateRangeText;
    }
}

// 2. MODELO DE VISTA PARA FEED DE PUBLICACIONES Y TARJETAS
public class FeedPostViewModel
{
    public ClassContentItem SourceItem { get; }

    public string Title => $"{SourceItem.ItemType}: {SourceItem.Title}";
    public string Description => SourceItem.Description;

    public bool HasDescription => SourceItem.ItemType == "Anuncio" && !string.IsNullOrWhiteSpace(SourceItem.Description);

    public string DateFormatted => SourceItem.CreatedAt.ToString("dd 'de' MMM.");
    public bool IsAssignment => SourceItem.ItemType == "Tarea";
    public string DueDateText => SourceItem.DueDate.HasValue ? $"Fecha de entrega: {SourceItem.DueDate.Value:dd/MM HH:mm}" : "";
    public string CommentsCountText => "Agregar un comentario de clase";

    public string IconSymbol => SourceItem.ItemType switch
    {
        "Tarea" => "📋",
        "Material" => "📖",
        _ => "📢"
    };

    public Color IconBgColor => SourceItem.ItemType switch
    {
        "Tarea" => Color.FromArgb("#D93025"),
        "Material" => Color.FromArgb("#1A73E8"),
        _ => Color.FromArgb("#1E8E3E")
    };

    public Brush CardGradientBrush
    {
        get
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };

            if (SourceItem.ItemType == "Tarea") // ROJO
            {
                brush.GradientStops.Add(new GradientStop(Color.FromArgb("#FADBD8"), 0.0f));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb("#FFFFFF"), 0.8f));
            }
            else if (SourceItem.ItemType == "Material") // AZUL
            {
                brush.GradientStops.Add(new GradientStop(Color.FromArgb("#D6EAF8"), 0.0f));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb("#FFFFFF"), 0.8f));
            }
            else // ANUNCIO - VERDE
            {
                brush.GradientStops.Add(new GradientStop(Color.FromArgb("#D1F2EB"), 0.0f));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb("#FFFFFF"), 0.8f));
            }

            return brush;
        }
    }

    public FeedPostViewModel(ClassContentItem item)
    {
        SourceItem = item;
    }
}

// 3. PÁGINA PRINCIPAL DE DETALLE DEL CURSO
public partial class CourseDetailPage : ContentPage
{
    public Course Course { get; set; }
    public ObservableCollection<FeedPostViewModel> FeedPosts { get; set; } = new();
    public ObservableCollection<FeedPostViewModel> AssignmentPosts { get; set; } = new();

    private DatabaseService _databaseService;
    private MainViewModel _mainViewModel;
    private bool _isTeacher;

    public CourseDetailPage(Course course, DatabaseService databaseService = null, MainViewModel mainViewModel = null)
    {
        InitializeComponent();
        Course = course;
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        EnsureServices();
        await LoadCourseContentAsync();
    }

    private void EnsureServices()
    {
        _databaseService ??= Handler?.MauiContext?.Services.GetService<DatabaseService>()
                            ?? Application.Current?.Handler?.MauiContext?.Services.GetService<DatabaseService>();

        _mainViewModel ??= Handler?.MauiContext?.Services.GetService<MainViewModel>()
                          ?? Application.Current?.Handler?.MauiContext?.Services.GetService<MainViewModel>();
    }

    private async Task LoadCourseContentAsync()
    {
        EnsureServices();

        if (_databaseService == null || Course == null) return;

        // 1. Determinar el rol del usuario en la clase
        if (_mainViewModel?.CurrentUser != null)
        {
            string role = await _databaseService.GetUserRoleInCourseAsync(Course.Id, _mainViewModel.CurrentUser.Id);
            _isTeacher = (role == "Teacher");
        }
        else
        {
            _isTeacher = false;
        }

        // 2. Ajustar visibilidad de botones y herramientas según el rol
        if (FindByName("FabAdd") is VisualElement fabAdd) fabAdd.IsVisible = _isTeacher;
        if (FindByName("BtnAddTeacher") is VisualElement btnAddTeacher) btnAddTeacher.IsVisible = _isTeacher;
        if (FindByName("BorderCreatePost") is VisualElement borderCreatePost) borderCreatePost.IsVisible = _isTeacher;
        if (FindByName("BtnManageUnits") is VisualElement btnManageUnits) btnManageUnits.IsVisible = _isTeacher;

        // 3. Cargar unidades y publicaciones de Supabase
        var units = await _databaseService.GetCourseUnitsAsync(Course.Id);
        var items = await _databaseService.GetCourseContentItemsAsync(Course.Id);

        FeedPosts.Clear();
        AssignmentPosts.Clear();

        foreach (var item in items)
        {
            var vm = new FeedPostViewModel(item);
            FeedPosts.Add(vm);

            if (item.ItemType == "Tarea")
            {
                AssignmentPosts.Add(vm);
            }
        }

        // 4. Agrupar la lista de actividades por Unidades/Parciales para la pestaña Trabajos
        var groupedAssignments = new List<UnitGroupViewModel>();

        foreach (var unit in units)
        {
            var unitItems = items.Where(i => i.UnitId == unit.Id || (i.CreatedAt.Date >= unit.StartDate.Date && i.CreatedAt.Date <= unit.EndDate.Date))
                                  .Select(i => new FeedPostViewModel(i))
                                  .ToList();

            if (unitItems.Any())
            {
                groupedAssignments.Add(new UnitGroupViewModel(
                    unit.Title,
                    $"{unit.StartDate:dd/MM} - {unit.EndDate:dd/MM}",
                    unitItems));
            }
        }

        // Elementos sin unidad asignada o publicados fuera del rango de fechas
        var unassignedItems = items.Where(i => !i.UnitId.HasValue && !units.Any(u => i.CreatedAt.Date >= u.StartDate.Date && i.CreatedAt.Date <= u.EndDate.Date))
                                   .Select(i => new FeedPostViewModel(i))
                                   .ToList();

        if (unassignedItems.Any())
        {
            groupedAssignments.Add(new UnitGroupViewModel("General / Sin Unidad", "", unassignedItems));
        }

        if (FindByName("CvAssignmentsGrouped") is CollectionView cvGrouped)
        {
            cvGrouped.ItemsSource = groupedAssignments;
        }

        // 5. Cargar profesores y alumnos para la pestaña "Personas"
        var teachers = await _databaseService.GetCourseTeachersAsync(Course.Id);
        if (FindByName("CvTeachers") is CollectionView cvTeachers)
        {
            cvTeachers.ItemsSource = teachers;
        }

        var students = await _databaseService.GetCourseStudentsAsync(Course.Id);
        if (FindByName("CvStudents") is CollectionView cvStudents)
        {
            cvStudents.ItemsSource = students;
        }
    }

    // NAVEGACIÓN DE PESTAÑAS
    private void OnTabNovedadesTapped(object sender, EventArgs e)
    {
        SetTab(ViewNovedades, TabNovedadesLabel, TabNovedadesIndicator);
    }

    private void OnTabTrabajosTapped(object sender, EventArgs e)
    {
        SetTab(ViewTrabajos, TabTrabajosLabel, TabTrabajosIndicator);
    }

    private void OnTabPersonasTapped(object sender, EventArgs e)
    {
        SetTab(ViewPersonas, TabPersonasLabel, TabPersonasIndicator);
    }

    private void SetTab(ScrollView activeView, Label activeLabel, BoxView activeIndicator)
    {
        ViewNovedades.IsVisible = (activeView == ViewNovedades);
        ViewTrabajos.IsVisible = (activeView == ViewTrabajos);
        ViewPersonas.IsVisible = (activeView == ViewPersonas);

        Color inactiveColor = Color.FromArgb("#64748B");
        Color activeColor = Color.FromArgb("#5EEAD4");

        TabNovedadesLabel.TextColor = inactiveColor;
        TabNovedadesIndicator.Color = Colors.Transparent;
        TabTrabajosLabel.TextColor = inactiveColor;
        TabTrabajosIndicator.Color = Colors.Transparent;
        TabPersonasLabel.TextColor = inactiveColor;
        TabPersonasIndicator.Color = Colors.Transparent;

        activeLabel.TextColor = activeColor;
        activeIndicator.Color = activeColor;
    }

    // ACCIONES Y BOTONES
    private async void OnBackButtonClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnManageUnitsClicked(object sender, EventArgs e)
    {
        EnsureServices();

        if (!_isTeacher)
        {
            await DisplayAlert("Acceso Restringido", "Solo los profesores pueden gestionar las unidades del curso.", "OK");
            return;
        }

        await Navigation.PushAsync(new ManageUnitsPage(Course.Id, _databaseService));
    }

    private async void OnAddTeacherClicked(object sender, EventArgs e)
    {
        EnsureServices();

        if (!_isTeacher)
        {
            await DisplayAlert("Acceso Restringido", "Solo los profesores pueden añadir a otros profesores.", "OK");
            return;
        }

        string input = await DisplayPromptAsync(
            "Añadir Profesor",
            "Ingresa el nombre de usuario o correo del profesor:",
            accept: "Añadir",
            cancel: "Cancelar",
            placeholder: "ejemplo@correo.com");

        if (string.IsNullOrWhiteSpace(input)) return;

        var (success, message) = await _databaseService.AddTeacherToCourseAsync(Course.Id, input.Trim());

        await DisplayAlert(success ? "Éxito" : "Atención", message, "OK");

        if (success)
        {
            var teachers = await _databaseService.GetCourseTeachersAsync(Course.Id);
            if (FindByName("CvTeachers") is CollectionView cvTeachers)
            {
                cvTeachers.ItemsSource = teachers;
            }
        }
    }

    private async void OnVideoCallClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Videollamada", "Iniciando sesión de videollamada para la clase...", "OK");
    }

    private async void OnCourseInfoClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Información del Curso", $"{Course.CourseName}\nCódigo de clase: {Course.CourseCode}\nProfesor: {Course.DisplayTeacherName}", "OK");
    }

    private async void OnCopyCourseCodeTapped(object sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Course?.CourseCode))
        {
            await Clipboard.Default.SetTextAsync(Course.CourseCode);
            await DisplayAlert("Código Copiado", $"El código '{Course.CourseCode}' se ha copiado al portapapeles.", "OK");
        }
    }

    private async void OnCreatePostTapped(object sender, EventArgs e)
    {
        await ShowCreateOptionsAsync();
    }

    private async void OnCreatePostOptionsClicked(object sender, EventArgs e)
    {
        await ShowCreateOptionsAsync();
    }

    private async Task ShowCreateOptionsAsync()
    {
        EnsureServices();

        if (!_isTeacher)
        {
            await DisplayAlert("Acceso Restringido", "Solo el profesor de la clase puede publicar tareas o anuncios.", "OK");
            return;
        }

        if (_databaseService == null || _mainViewModel == null || Course == null)
        {
            await DisplayAlert("Error", "No se pudo acceder al servicio de datos.", "OK");
            return;
        }

        string action = await DisplayActionSheet(
            "¿Qué deseas publicar?",
            "Cancelar",
            null,
            "📋  Crear Tarea",
            "📖  Crear Material",
            "📢  Crear Anuncio");

        string selectedType = action switch
        {
            "📋  Crear Tarea" => "Tarea",
            "📖  Crear Material" => "Material",
            "📢  Crear Anuncio" => "Anuncio",
            _ => null
        };

        if (selectedType != null)
        {
            await Navigation.PushAsync(new CreateContentPage(Course.Id, selectedType, _databaseService, _mainViewModel));
        }
    }

    private async void OnFeedPostTapped(object sender, EventArgs e)
    {
        EnsureServices();
        var border = sender as Border;
        var vm = border?.BindingContext as FeedPostViewModel;

        if (vm == null || vm.SourceItem.ItemType == "Anuncio") return;

        if (_isTeacher && vm.SourceItem.ItemType == "Tarea")
        {
            await Navigation.PushAsync(new TeacherTaskOverviewPage(vm.SourceItem, Course.Id, _databaseService, _mainViewModel));
        }
        else
        {
            await Navigation.PushAsync(new ContentDetailPage(vm.SourceItem, _databaseService, _mainViewModel));
        }
    }

    private async void OnPostOptionsClicked(object sender, EventArgs e)
    {
        EnsureServices();

        var button = sender as ImageButton;
        var vm = button?.BindingContext as FeedPostViewModel;

        if (vm == null) return;

        if (!_isTeacher)
        {
            await DisplayAlert("Opciones", $"Publicado el {vm.DateFormatted}", "OK");
            return;
        }

        string action = await DisplayActionSheet(
            $"Opciones de: {vm.SourceItem.Title}",
            "Cancelar",
            "Eliminar publicación",
            "Copiar enlace");

        if (action == "Eliminar publicación")
        {
            bool confirm = await DisplayAlert(
                "Confirmar eliminación",
                $"¿Estás seguro de eliminar '{vm.SourceItem.Title}'? Esta acción no se puede deshacer.",
                "Sí, eliminar",
                "Cancelar");

            if (confirm)
            {
                await _databaseService.DeleteClassContentItemAsync(vm.SourceItem.Id);
                await LoadCourseContentAsync();
            }
        }
        else if (action == "Copiar enlace")
        {
            await Clipboard.Default.SetTextAsync($"workflow://course/{Course.Id}/item/{vm.SourceItem.Id}");
            await DisplayAlert("Éxito", "Enlace copiado al portapapeles.", "OK");
        }
    }
}