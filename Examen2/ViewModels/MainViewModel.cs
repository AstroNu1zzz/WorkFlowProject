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

        // --- NAVEGACIÓN DE PESTAÑAS (TABS INFERIORES) ---
        private string _activeTab = "Clases";
        public bool IsClasesVisible => _activeTab == "Clases";
        public bool IsTareasVisible => _activeTab == "Tareas";
        public bool IsEnfoqueVisible => _activeTab == "Enfoque";
        public bool IsCalendarioVisible => _activeTab == "Calendario";

        public Color ClasesTabColor => IsClasesVisible ? Color.FromArgb("#5EEAD4") : Color.FromArgb("#64748B");
        public Color ClasesIndicatorColor => IsClasesVisible ? Color.FromArgb("#5EEAD4") : Colors.Transparent;
        public FontAttributes ClasesFontAttributes => IsClasesVisible ? FontAttributes.Bold : FontAttributes.None;

        public Color TareasTabColor => IsTareasVisible ? Color.FromArgb("#5EEAD4") : Color.FromArgb("#64748B");
        public Color TareasIndicatorColor => IsTareasVisible ? Color.FromArgb("#5EEAD4") : Colors.Transparent;
        public FontAttributes TareasFontAttributes => IsTareasVisible ? FontAttributes.Bold : FontAttributes.None;

        public Color EnfoqueTabColor => IsEnfoqueVisible ? Color.FromArgb("#5EEAD4") : Color.FromArgb("#64748B");
        public Color EnfoqueIndicatorColor => IsEnfoqueVisible ? Color.FromArgb("#5EEAD4") : Colors.Transparent;
        public FontAttributes EnfoqueFontAttributes => IsEnfoqueVisible ? FontAttributes.Bold : FontAttributes.None;

        public Color CalendarioTabColor => IsCalendarioVisible ? Color.FromArgb("#5EEAD4") : Color.FromArgb("#64748B");
        public Color CalendarioIndicatorColor => IsCalendarioVisible ? Color.FromArgb("#5EEAD4") : Colors.Transparent;
        public FontAttributes CalendarioFontAttributes => IsCalendarioVisible ? FontAttributes.Bold : FontAttributes.None;

        // --- TEMPORIZADOR POMODORO (MODO ENFOQUE) ---
        private IDispatcherTimer _timer;
        private int _remainingSeconds = 1500;
        private bool _isTimerRunning;

        private string _timerText = "25:00";
        public string TimerText
        {
            get => _timerText;
            set { _timerText = value; OnPropertyChanged(); }
        }

        private string _timerStatusText = "Listo para iniciar";
        public string TimerStatusText
        {
            get => _timerStatusText;
            set { _timerStatusText = value; OnPropertyChanged(); }
        }

        // --- CALENDARIO DINÁMICO ---
        private int _currentMonthIndex = 11;
        private int _currentYear = 2026;
        private readonly string[] _monthNames = {
            "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
            "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
        };

        private string _currentMonthYearText = "Noviembre de 2026";
        public string CurrentMonthYearText
        {
            get => _currentMonthYearText;
            set { _currentMonthYearText = value; OnPropertyChanged(); }
        }

        public ObservableCollection<CalendarDayModel> CalendarDays { get; set; } = new();

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

        // Nuevos comandos MVVM para Tabs, Temporizador y Calendario
        public ICommand SelectTabCommand { get; }
        public ICommand SetTimerMinutesCommand { get; }
        public ICommand StartTimerCommand { get; }
        public ICommand PauseTimerCommand { get; }
        public ICommand ResetTimerCommand { get; }
        public ICommand PreviousMonthCommand { get; }
        public ICommand NextMonthCommand { get; }

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

            // Inicialización de comandos MVVM migrados
            SelectTabCommand = new Command<string>(OnSelectTab);
            SetTimerMinutesCommand = new Command<string>(OnSetTimerMinutes);
            StartTimerCommand = new Command(OnStartTimer);
            PauseTimerCommand = new Command(OnPauseTimer);
            ResetTimerCommand = new Command(OnResetTimer);
            PreviousMonthCommand = new Command(OnPreviousMonth);
            NextMonthCommand = new Command(OnNextMonth);

            LoadCalendarDays();
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

        // --- LÓGICA DE PESTAÑAS ---
        private void OnSelectTab(string tabName)
        {
            _activeTab = tabName;

            OnPropertyChanged(nameof(IsClasesVisible));
            OnPropertyChanged(nameof(IsTareasVisible));
            OnPropertyChanged(nameof(IsEnfoqueVisible));
            OnPropertyChanged(nameof(IsCalendarioVisible));

            OnPropertyChanged(nameof(ClasesTabColor));
            OnPropertyChanged(nameof(ClasesIndicatorColor));
            OnPropertyChanged(nameof(ClasesFontAttributes));

            OnPropertyChanged(nameof(TareasTabColor));
            OnPropertyChanged(nameof(TareasIndicatorColor));
            OnPropertyChanged(nameof(TareasFontAttributes));

            OnPropertyChanged(nameof(EnfoqueTabColor));
            OnPropertyChanged(nameof(EnfoqueIndicatorColor));
            OnPropertyChanged(nameof(EnfoqueFontAttributes));

            OnPropertyChanged(nameof(CalendarioTabColor));
            OnPropertyChanged(nameof(CalendarioIndicatorColor));
            OnPropertyChanged(nameof(CalendarioFontAttributes));

            if (IsCalendarioVisible) LoadCalendarDays();
        }

        // --- LÓGICA DEL TEMPORIZADOR POMODORO ---
        private void OnSetTimerMinutes(string minutesParam)
        {
            if (int.TryParse(minutesParam, out int minutes))
            {
                _remainingSeconds = minutes * 60;
                _timer?.Stop();
                _isTimerRunning = false;
                TimerStatusText = "Listo para iniciar";
                UpdateTimerDisplay();
            }
        }

        private void OnStartTimer()
        {
            if (_timer == null && Application.Current != null)
            {
                _timer = Application.Current.Dispatcher.CreateTimer();
                _timer.Interval = TimeSpan.FromSeconds(1);
                _timer.Tick += (s, e) =>
                {
                    if (_remainingSeconds > 0)
                    {
                        _remainingSeconds--;
                        UpdateTimerDisplay();
                    }
                    else
                    {
                        _timer.Stop();
                        _isTimerRunning = false;
                        TimerStatusText = "¡Tiempo cumplido! 🎉";
                        Application.Current?.MainPage?.DisplayAlert("Modo Enfoque", "¡Excelente trabajo! Has completado tu sesión de concentración.", "OK");
                    }
                };
            }

            if (!_isTimerRunning)
            {
                _isTimerRunning = true;
                TimerStatusText = "En curso 🚀";
                _timer?.Start();
            }
        }

        private void OnPauseTimer()
        {
            if (_timer != null && _isTimerRunning)
            {
                _timer.Stop();
                _isTimerRunning = false;
                TimerStatusText = "Pausado ⏸️";
            }
        }

        private void OnResetTimer()
        {
            _timer?.Stop();
            _isTimerRunning = false;
            _remainingSeconds = 1500;
            TimerStatusText = "Listo para iniciar";
            UpdateTimerDisplay();
        }

        private void UpdateTimerDisplay()
        {
            int minutes = _remainingSeconds / 60;
            int seconds = _remainingSeconds % 60;
            TimerText = $"{minutes:D2}:{seconds:D2}";
        }

        // --- LÓGICA DEL CALENDARIO ---
        private void OnPreviousMonth()
        {
            _currentMonthIndex--;
            if (_currentMonthIndex < 1)
            {
                _currentMonthIndex = 12;
                _currentYear--;
            }
            LoadCalendarDays();
        }

        private void OnNextMonth()
        {
            _currentMonthIndex++;
            if (_currentMonthIndex > 12)
            {
                _currentMonthIndex = 1;
                _currentYear++;
            }
            LoadCalendarDays();
        }

        public void LoadCalendarDays()
        {
            CurrentMonthYearText = $"{_monthNames[_currentMonthIndex - 1]} de {_currentYear}";

            int daysInMonth = DateTime.DaysInMonth(_currentYear, _currentMonthIndex);
            var firstDayOfWeek = (int)new DateTime(_currentYear, _currentMonthIndex, 1).DayOfWeek;

            CalendarDays.Clear();

            for (int i = 0; i < firstDayOfWeek; i++)
            {
                CalendarDays.Add(new CalendarDayModel { DayNumber = 0, HasTask = false });
            }

            for (int day = 1; day <= daysInMonth; day++)
            {
                bool hasTask = false;
                string taskColor = "Transparent";

                if (_currentYear == 2026 && _currentMonthIndex == 11)
                {
                    if (day == 7) { hasTask = true; taskColor = "#5EEAD4"; }
                    if (day == 8) { hasTask = true; taskColor = "#5C93F9"; }
                    if (day == 10) { hasTask = true; taskColor = "#86EFAC"; }
                }

                CalendarDays.Add(new CalendarDayModel
                {
                    DayNumber = day,
                    HasTask = hasTask,
                    TaskIndicatorColor = taskColor
                });
            }
        }

        // --- GESTIÓN DE FOTO DE PERFIL ---
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
            LoadCalendarDays();
            if (CurrentUser == null) return;

            var courses = await _databaseService.GetCoursesAsync(CurrentUser.Id);
            EnrolledCourses.Clear();
            foreach (var c in courses)
            {
                EnrolledCourses.Add(c);
            }

            var activities = await _databaseService.GetGlobalActivitiesAsync(CurrentUser.Id);
            GlobalRecentActivities.Clear();

            int colorIndex = 0;
            string[] vibrantColors = { "#5EEAD4", "#5C93F9", "#86EFAC" };

            foreach (var a in activities)
            {
                if (a.ItemType == "Tarea" && a.DueDate.HasValue && a.DueDate.Value < DateTime.Now)
                {
                    continue;
                }
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

    public class CalendarDayModel
    {
        public int DayNumber { get; set; }
        public string DayNumberText => DayNumber > 0 ? DayNumber.ToString() : string.Empty;
        public bool HasTask { get; set; }
        public string TaskIndicatorColor { get; set; } = "Transparent";
        public double BorderThickness => HasTask ? 1.5 : 1.0;
        public string BorderColor => HasTask ? TaskIndicatorColor : "#2D3748";
        public FontAttributes DayFontAttributes => HasTask ? FontAttributes.Bold : FontAttributes.None;
        public Color DayTextColor => HasTask ? Colors.White : Color.FromArgb("#64748B");
    }
}