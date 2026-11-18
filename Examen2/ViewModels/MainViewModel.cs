using Examen2.Data;
using Examen2.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Examen2.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _databaseService;
        private readonly EmailService _emailService;

        // --- GESTIÓN DEL PERFIL DE USUARIO ---
        private User _currentUser;
        public User CurrentUser
        {
            get => _currentUser;
            set
            {
                _currentUser = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UserInitial));
                UpdateProfileImageState();
            }
        }

        public string UserInitial => !string.IsNullOrEmpty(CurrentUser?.Username)
            ? CurrentUser.Username.Substring(0, 1).ToUpper()
            : (!string.IsNullOrEmpty(CurrentUser?.Email) ? CurrentUser.Email.Substring(0, 1).ToUpper() : "U");

        private bool _hasProfilePicture;
        public bool HasProfilePicture
        {
            get => _hasProfilePicture;
            set { _hasProfilePicture = value; OnPropertyChanged(); }
        }

        private ImageSource _profileImageSource;
        public ImageSource ProfileImageSource
        {
            get => _profileImageSource;
            set { _profileImageSource = value; OnPropertyChanged(); }
        }

        // --- CONTROL DE MENÚS (PERFIL Y MENÚ LATERAL) ---
        private bool _isProfileMenuVisible;
        public ObservableCollection<ActivityItemViewModel> GlobalRecentActivities { get; set; } = new();
        public bool IsProfileMenuVisible
        {
            get => _isProfileMenuVisible;
            set { _isProfileMenuVisible = value; OnPropertyChanged(); }
        }

        private bool _isDrawerVisible;
        public bool IsDrawerVisible
        {
            get => _isDrawerVisible;
            set { _isDrawerVisible = value; OnPropertyChanged(); }
        }

        // --- PROPIEDADES DE LA INTERFAZ ---
        public ObservableCollection<Course> EnrolledCourses { get; set; }
        public string NextDeadlineMessage { get; set; }

        // --- COMANDOS ---
        public ICommand NavigateHomeCommand { get; }
        public ICommand NavigateCalendarCommand { get; }
        public ICommand NavigatePendingTasksCommand { get; }
        public ICommand NavigateArchivedCommand { get; }
        public ICommand NavigateSettingsCommand { get; }
        public ICommand OpenCourseCommand { get; }
        public ICommand OpenCourseFilesCommand { get; }
        public ICommand ViewPendingTaskCommand { get; }
        public ICommand ChangeProfilePictureCommand { get; }
        public ICommand ToggleProfileMenuCommand { get; }
        public ICommand ToggleDrawerCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand AddCourseCommand { get; }
        public ICommand DeleteCourseCommand { get; }
        public ICommand OpenActivityCommand { get; }

        public MainViewModel(DatabaseService databaseService, EmailService emailService)
        {
            _databaseService = databaseService;
            _emailService = emailService;
            EnrolledCourses = new ObservableCollection<Course>();

            NextDeadlineMessage = "No tienes ningún trabajo que entregar en los próximos 7 días";

            NavigateHomeCommand = new Command(OnNavigateHome);
            NavigateCalendarCommand = new Command(OnNavigateCalendar);
            NavigatePendingTasksCommand = new Command(OnNavigatePendingTasks);
            NavigateArchivedCommand = new Command(OnNavigateArchived);
            NavigateSettingsCommand = new Command(OnNavigateSettings);
            OpenActivityCommand = new Command<ActivityItemViewModel>(async (activity) => await OpenActivityAsync(activity));

            OpenCourseCommand = new Command<Course>(OnOpenCourse);
            OpenCourseFilesCommand = new Command<Course>(OnOpenCourseFiles);
            ViewPendingTaskCommand = new Command(OnNavigatePendingTasks);

            ChangeProfilePictureCommand = new Command(OnChangeProfilePicture);
            ToggleProfileMenuCommand = new Command(() => IsProfileMenuVisible = !IsProfileMenuVisible);
            ToggleDrawerCommand = new Command(() => IsDrawerVisible = !IsDrawerVisible);
            LogoutCommand = new Command(OnLogout);

            AddCourseCommand = new Command(OnAddCourse);
            DeleteCourseCommand = new Command<Course>(OnDeleteCourse);

            _ = LoadCoursesAsync();
        }

        public void SetCurrentUser(User user)
        {
            CurrentUser = user;
            if (user != null)
            {
                Preferences.Default.Set("LoggedInUserId", user.Id);
                _ = LoadCoursesAsync();
            }
            UpdateProfileImageState();
        }

        private void LoadProfilePicture()
        {
            if (CurrentUser != null && !string.IsNullOrEmpty(CurrentUser.ProfilePicturePath) && File.Exists(CurrentUser.ProfilePicturePath))
            {
                ProfileImageSource = ImageSource.FromFile(CurrentUser.ProfilePicturePath);
                HasProfilePicture = true;
            }
            else
            {
                ProfileImageSource = null;
                HasProfilePicture = false;
            }
        }

        private async void OnChangeProfilePicture()
        {
            IsProfileMenuVisible = false;
            try
            {
                var result = await MediaPicker.Default.PickPhotoAsync();

                if (result != null)
                {
                    if (CurrentUser == null) return;

                    using (var stream = await result.OpenReadAsync())
                    {
                        var (publicUrl, errorMessage) = await _databaseService.UploadFileStreamAsync(stream, result.FileName);

                        if (!string.IsNullOrEmpty(errorMessage) || string.IsNullOrEmpty(publicUrl))
                        {
                            if (Application.Current?.MainPage != null)
                            {
                                await Application.Current.MainPage.DisplayAlert("Error", $"No se pudo subir la foto: {errorMessage}", "OK");
                            }
                            return;
                        }

                        CurrentUser.ProfilePicturePath = publicUrl;
                        await _databaseService.UpdateUserAsync(CurrentUser);

                        ProfileImageSource = ImageSource.FromUri(new Uri(publicUrl));
                        HasProfilePicture = true;

                        OnPropertyChanged(nameof(CurrentUser));
                        OnPropertyChanged(nameof(HasProfilePicture));
                        OnPropertyChanged(nameof(ProfileImageSource));
                    }
                }
            }
            catch (Exception ex)
            {
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.DisplayAlert("Error", $"No se pudo cargar la imagen: {ex.Message}", "OK");
                }
            }
        }

        private void UpdateProfileImageState()
        {
            if (CurrentUser != null && !string.IsNullOrWhiteSpace(CurrentUser.ProfilePicturePath))
            {
                HasProfilePicture = true;
                ProfileImageSource = ImageSource.FromUri(new Uri(CurrentUser.ProfilePicturePath));
            }
            else
            {
                HasProfilePicture = false;
                ProfileImageSource = null;
            }

            OnPropertyChanged(nameof(HasProfilePicture));
            OnPropertyChanged(nameof(ProfileImageSource));
        }

        private void OnLogout()
        {
            Preferences.Default.Remove("IsLoggedIn");
            Preferences.Default.Remove("CurrentUserId");
            Preferences.Default.Remove("Last2FAVerifiedAt");

            if (Application.Current != null)
            {
                Application.Current.MainPage = new NavigationPage(new LoginPage(_databaseService, _emailService, this));
            }
        }

        public async Task RefreshCoursesAsync()
        {
            if (CurrentUser == null) return;

            var courses = await _databaseService.GetCoursesAsync(CurrentUser.Id);
            EnrolledCourses.Clear();
            foreach (var c in courses)
            {
                EnrolledCourses.Add(c);
            }

            var activities = await _databaseService.GetGlobalActivitiesAsync(CurrentUser.Id);
            GlobalRecentActivities.Clear();

            // Usamos un contador para alternar los 3 colores vibrantes en las tareas
            int colorIndex = 0;
            string[] vibrantColors = { "#5EEAD4", "#5C93F9", "#86EFAC" };

            foreach (var a in activities)
            {
                // Asignación de colores vibrantes
                string gradient = "#5EEAD4"; // Color por defecto

                if (a.ItemType == "Tarea")
                {
                    gradient = vibrantColors[colorIndex % vibrantColors.Length];
                    colorIndex++;
                }
                else if (a.ItemType == "Material")
                {
                    gradient = "#5EEAD4"; // Turquesa
                }
                else // Anuncio u otros
                {
                    gradient = "#86EFAC"; // Verde vibrante
                }

                string icon = a.ItemType switch
                {
                    "Tarea" => "📋",
                    "Material" => "📖",
                    _ => "📢"
                };

                var parentCourse = courses.FirstOrDefault(c => c.Id == a.CourseId);
                string courseName = parentCourse != null ? parentCourse.CourseName : "Clase";

                GlobalRecentActivities.Add(new ActivityItemViewModel
                {
                    OriginalItem = a,
                    DisplayTitle = $"{a.ItemType}: {a.Title}",
                    Subtitle = $"{courseName} • Publicado el {a.CreatedAt:dd MMM}",
                    GradientStartColor = gradient,
                    IconString = icon,
                    HasDueDate = a.DueDate.HasValue,
                    DueDateString = a.DueDate.HasValue ? $"Entrega: {a.DueDate.Value:dd/MM/yyyy HH:mm}" : ""
                });
            }
        }

        private async void OnAddCourse()
        {
            IsDrawerVisible = false;
            if (CurrentUser == null || Application.Current?.MainPage == null) return;

            string action = await Application.Current.MainPage.DisplayActionSheet(
                "¿Qué deseas hacer?",
                "Cancelar",
                null,
                "Unirse a una clase",
                "Escanear código QR",
                "Crear una clase");

            if (action == "Unirse a una clase")
            {
                string code = await Application.Current.MainPage.DisplayPromptAsync(
                    "Unirse a clase",
                    "Ingresa el código de 6 caracteres que te dio el profesor:",
                    accept: "Unirse",
                    cancel: "Cancelar",
                    placeholder: "Ej. X8K9P2",
                    maxLength: 6,
                    keyboard: Microsoft.Maui.Keyboard.Text);

                if (string.IsNullOrWhiteSpace(code)) return;

                code = code.Trim().ToUpper();
                var (success, message) = await _databaseService.JoinCourseWithCodeAsync(code, CurrentUser.Id);

                await Application.Current.MainPage.DisplayAlert(success ? "Éxito" : "Atención", message, "OK");

                if (success)
                {
                    await RefreshCoursesAsync();
                }
            }
            else if (action == "Escanear código QR")
            {
                string scannedResult = await Application.Current.MainPage.DisplayPromptAsync(
                    "Escáner QR WorkFlow",
                    "Apunta la cámara al código QR del profesor o ingresa el token detectado:",
                    "Conectar",
                    "Cancelar",
                    "Ej. WORKFLOW-QR-INF4",
                    maxLength: 30,
                    keyboard: Microsoft.Maui.Keyboard.Default);

                if (!string.IsNullOrWhiteSpace(scannedResult))
                {
                    scannedResult = scannedResult.Trim().ToUpper();
                    var (success, message) = await _databaseService.JoinCourseWithCodeAsync(scannedResult, CurrentUser.Id);

                    await Application.Current.MainPage.DisplayAlert(success ? "¡Éxito!" : "Atención", message, "OK");

                    if (success)
                    {
                        await RefreshCoursesAsync();
                    }
                }
            }
            else if (action == "Crear una clase")
            {
                var addCoursePage = Application.Current?.Handler?.MauiContext?.Services.GetService<AddCoursePage>()
                                    ?? new AddCoursePage(_databaseService, this);

                await Application.Current.MainPage.Navigation.PushAsync(addCoursePage);
            }
        }

        private async void OnDeleteCourse(Course course)
        {
            if (course == null) return;

            bool confirm = await Application.Current.MainPage.DisplayAlert(
                "Eliminar clase",
                $"¿Estás seguro de que deseas eliminar '{course.CourseName}'?",
                "Eliminar", "Cancelar");

            if (confirm)
            {
                await _databaseService.DeleteCourseAsync(course.Id);
                await LoadCoursesAsync();
            }
        }

        private async Task LoadCoursesAsync()
        {
            if (CurrentUser == null)
            {
                EnrolledCourses.Clear();
                GlobalRecentActivities.Clear();
                return;
            }

            var courses = await _databaseService.GetCoursesAsync(CurrentUser.Id);
            EnrolledCourses.Clear();
            foreach (var course in courses)
            {
                if (string.IsNullOrWhiteSpace(course.TeacherName) && !string.IsNullOrWhiteSpace(course.PrimaryTeacherName))
                {
                    course.TeacherName = course.PrimaryTeacherName;
                }

                EnrolledCourses.Add(course);
            }

            var activities = await _databaseService.GetGlobalActivitiesAsync(CurrentUser.Id);
            GlobalRecentActivities.Clear();

            int colorIndex = 0;
            string[] vibrantColors = { "#5EEAD4", "#5C93F9", "#86EFAC" };

            foreach (var a in activities)
            {
                // Asignación de colores vibrantes
                string gradient = "#5EEAD4";

                if (a.ItemType == "Tarea")
                {
                    gradient = vibrantColors[colorIndex % vibrantColors.Length];
                    colorIndex++;
                }
                else if (a.ItemType == "Material")
                {
                    gradient = "#5EEAD4";
                }
                else
                {
                    gradient = "#86EFAC";
                }

                string icon = a.ItemType == "Tarea" ? "📋" : (a.ItemType == "Material" ? "📖" : "📢");

                var parentCourse = courses.FirstOrDefault(c => c.Id == a.CourseId);
                string courseName = parentCourse != null ? parentCourse.CourseName : "Clase";

                GlobalRecentActivities.Add(new ActivityItemViewModel
                {
                    OriginalItem = a,
                    DisplayTitle = $"{a.ItemType}: {a.Title}",
                    Subtitle = $"{courseName} • Publicado el {a.CreatedAt:dd MMM}",
                    GradientStartColor = gradient,
                    IconString = icon,
                    HasDueDate = a.DueDate.HasValue,
                    DueDateString = a.DueDate.HasValue ? $"Entrega: {a.DueDate.Value:dd/MM/yyyy HH:mm}" : ""
                });
            }
        }

        private async Task OpenActivityAsync(ActivityItemViewModel activity)
        {
            if (activity?.OriginalItem == null) return;

            if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.Navigation.PushAsync(
                    new ContentDetailPage(activity.OriginalItem, _databaseService, this)
                );
            }
        }

        private async void OnNavigateHome()
        {
            IsDrawerVisible = false;
            await LoadCoursesAsync();
        }

        private async void OnNavigateCalendar()
        {
            IsDrawerVisible = false;
            if (Application.Current?.MainPage != null)
                await Application.Current.MainPage.Navigation.PushAsync(new CalendarPage());
        }

        private async void OnNavigatePendingTasks()
        {
            IsDrawerVisible = false;
            if (Application.Current?.MainPage != null)
                await Application.Current.MainPage.Navigation.PushAsync(new PendingTaskPage());
        }

        private async void OnNavigateArchived()
        {
            IsDrawerVisible = false;
            if (Application.Current?.MainPage != null)
                await Application.Current.MainPage.DisplayAlert("Clases archivadas", "No tienes clases archivadas en este ciclo escolar.", "OK");
        }

        private async void OnNavigateSettings()
        {
            IsDrawerVisible = false;
            IsProfileMenuVisible = false;

            if (Application.Current?.MainPage != null)
            {
                var settingsPage = Application.Current?.Handler?.MauiContext?.Services.GetService<SettingsPage>()
                                  ?? new SettingsPage(_databaseService, mainViewModel: this);

                await Application.Current.MainPage.Navigation.PushAsync(settingsPage);
            }
        }

        private async void OnOpenCourse(Course selectedCourse)
        {
            IsDrawerVisible = false;
            if (selectedCourse == null) return;
            if (Application.Current?.MainPage != null)
                await Application.Current.MainPage.Navigation.PushAsync(new CourseDetailPage(selectedCourse, _databaseService, this));
        }

        private async void OnOpenCourseFiles(Course selectedCourse)
        {
            IsDrawerVisible = false;
            if (selectedCourse == null) return;
            if (Application.Current?.MainPage != null)
                await Application.Current.MainPage.DisplayAlert("Archivos de clase", $"Abriendo la carpeta de Drive/archivos locales para {selectedCourse.CourseName}", "OK");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ActivityItemViewModel
    {
        public ClassContentItem OriginalItem { get; set; }
        public string DisplayTitle { get; set; }
        public string Subtitle { get; set; }
        public string GradientStartColor { get; set; }
        public string IconString { get; set; }
        public bool HasDueDate { get; set; }
        public string DueDateString { get; set; }
    }

    public class UnitGroup : List<ClassContentItem>
    {
        public string Name { get; private set; }
        public UnitGroup(string name, List<ClassContentItem> items) : base(items)
        {
            Name = name;
        }
    }
}

/*
 * 
 * Y al final, después de tantas iteraciones y refactorizaciones,
 * hemos llegado a estas últimas líneas de código.
 * Exactamente igual como acabó mi historia con ella...
 *
 * Todo empezó por allá a mis 16 años, a finales de Julio, en aquella
 * época trabajaba durante las vacaciones como becario en una empresa.Ahí conocí a Linda Villanueva.
 * Al principio pensé que me llevaría mejor con su hermana, pero con el tiempo
 * descubrimos una conexión única: los mismos gustos, una risa hermosa y una gran vibra
 * y congeniábamos bastante bien, pero como saber que no todo seria perfecto...
 *
 * En los días de trabajo nos escapábamos un ratito: veíamos películas, comíamos
 * botana y jugábamos juegos de mesa, una relación de amistad increible.Llegaban las vacaciones y el clásico ritual
* era hacer videollamada por Meet. ¿Qué hacíamos? Me ponía mascarillas con ella,
 * veíamos películas al mismo tiempo y jugábamos juegos de terror y mas.Tenía una personalidad increíble y una risa preciosa.
 *
 * Pero el sistema tenía una dependencia tóxica: ella cargaba con una pareja que
 * la limitaba muchísimo y terminaban a cada rato; siento que eso influyó bastante, pero nunca quedo en mi.
 * Con los meses me di cuenta del patrón crítico: si yo no le escribía, ella
 * jamás me mandaba mensaje.Dejé pasar el tiempo a ver que pasaba pero...Diez meses de silencio absoluto.
 *
 * El tiempo corrió desde aquel inicio hasta después del 11 de octubre, día de la
 * Carrera Quálitas (otra proyectada para más adelante, like para parte 2)
 * Una semana después, tras volver a instalar Instagram pq se ponia personal e hizo lo que yo no puedo darme de baja, subí una nota "buen resultado pero la espinita siempre se queda" o algo asi ns
 *Ella respondió con un emoji de cara preocupante o triste y lo único que dije "Hola, tiene mucho que no sabía
 * de ti"... y desde entonces, fin de la ejecución. Jamás volvimos a hablar.
 *
 * Pero habia algo que yo no sabia, llegaria el Viernes 13 de Noviembre 2026, el tiempo paso y despues de 1 Mes...
 * dos mensajes aparecieron en mi pantalla "Morar forever" "emoji calma/emoji calma"
 * En ese momento me sentia faliz por la moratvibra pero algo en mi no queria dar mucha respuesta, pense...y si talvez quiere voler a hablar, pero algo en mi no sabia
 *Y respondi...
 *
 *
 *
 *
 *
 *
 *
 *
 * Fin del bloque.Así como murieron esas líneas de comunicación,
 * murió toda interacción posible.
 * =====================================================================
 */
