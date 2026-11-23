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

#if ANDROID
using Android.Content;
using Android.OS;
using Android.Provider;
using AndroidX.Core.App;
#endif

namespace Examen2.ViewModels
{
#if ANDROID
    [Android.Content.BroadcastReceiver(Enabled = true, Exported = true)]
    public class FocusExtensionReceiver : Android.Content.BroadcastReceiver
    {
        public const string ActionAddFiveMinutes = "com.examen2.workflow.ADD_FIVE_MINUTES";
        public const string ActionDismissPrompt = "com.examen2.workflow.DISMISS_PROMPT";
        public const string ActionThreeMinWarning = "com.examen2.workflow.THREE_MIN_WARNING";
        public const string ActionTimerFinished = "com.examen2.workflow.TIMER_FINISHED";

        public override void OnReceive(Context context, Intent intent)
        {
            string action = intent?.Action;

            if (action == ActionAddFiveMinutes)
            {
                long currentEndTicks = Preferences.Default.Get("FocusEndTimeUtcTicks", 0L);
                if (currentEndTicks > 0)
                {
                    DateTime newEndTime = new DateTime(currentEndTicks, DateTimeKind.Utc).AddMinutes(5);
                    Preferences.Default.Set("FocusEndTimeUtcTicks", newEndTime.Ticks);
                    Preferences.Default.Set("FocusThreeMinPromptShown", false);

                    int newRemaining = (int)Math.Max(0, Math.Ceiling((newEndTime - DateTime.UtcNow).TotalSeconds));
                    MainViewModel.ScheduleSystemAlarmsStatic(context, newEndTime);
                    MainViewModel.ShowCountdownNotificationStatic(context, newRemaining);
                }
                MainViewModel.DismissExtensionNotificationStatic(context);
                MainViewModel.Instance?.SyncTimerFromStorage();
            }
            else if (action == ActionDismissPrompt)
            {
                MainViewModel.DismissExtensionNotificationStatic(context);
            }
            else if (action == ActionThreeMinWarning)
            {
                bool alreadyShown = Preferences.Default.Get("FocusThreeMinPromptShown", false);
                bool isRunning = Preferences.Default.Get("FocusIsRunning", false);

                if (isRunning && !alreadyShown)
                {
                    Preferences.Default.Set("FocusThreeMinPromptShown", true);
                    MainViewModel.ShowThreeMinuteExtensionNotificationStatic(context);
                }
            }
            else if (action == ActionTimerFinished)
            {
                Preferences.Default.Set("FocusIsRunning", false);
                Preferences.Default.Set("FocusEndTimeUtcTicks", 0L);
                Preferences.Default.Set("FocusThreeMinPromptShown", false);
                MainViewModel.SetDoNotDisturbStatic(context, false);
                MainViewModel.ShowFinishedNotificationStatic(context);
                MainViewModel.Instance?.SyncTimerFromStorage();
            }
        }
    }
#endif

    public class MainViewModel : INotifyPropertyChanged
    {
        public static MainViewModel Instance { get; private set; }

        private readonly DatabaseService _databaseService;
        private readonly EmailService _emailService;

        // Constantes de notificaciones Android
        public const string FocusChannelId = "workflow_focus_timer_channel_v2";
        public const string PromptChannelId = "workflow_focus_prompt_channel_v2";
        public const int FocusNotificationId = 1001;
        public const int ExtensionPromptNotificationId = 1002;

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
        private int _selectedDurationMinutes = 25;
        private int _remainingSeconds = 1500;
        private bool _isTimerRunning;
        private DateTime _endTimeUtc;

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

        // Propiedades visuales para los 4 botones de tiempo
        public Color Btn15BgColor => GetDurationBtnBg(15);
        public Color Btn15TextColor => GetDurationBtnText(15);
        public FontAttributes Btn15FontAttributes => _selectedDurationMinutes == 15 ? FontAttributes.Bold : FontAttributes.None;

        public Color Btn25BgColor => GetDurationBtnBg(25);
        public Color Btn25TextColor => GetDurationBtnText(25);
        public FontAttributes Btn25FontAttributes => _selectedDurationMinutes == 25 ? FontAttributes.Bold : FontAttributes.None;

        public Color Btn45BgColor => GetDurationBtnBg(45);
        public Color Btn45TextColor => GetDurationBtnText(45);
        public FontAttributes Btn45FontAttributes => _selectedDurationMinutes == 45 ? FontAttributes.Bold : FontAttributes.None;

        public Color Btn60BgColor => GetDurationBtnBg(60);
        public Color Btn60TextColor => GetDurationBtnText(60);
        public FontAttributes Btn60FontAttributes => _selectedDurationMinutes == 60 ? FontAttributes.Bold : FontAttributes.None;

        private Color GetDurationBtnBg(int minutes)
        {
            if (_selectedDurationMinutes == minutes)
                return Color.FromArgb("#5EEAD4");

            bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return isDark ? Color.FromArgb("#2D3748") : Color.FromArgb("#E2E8F0");
        }

        private Color GetDurationBtnText(int minutes)
        {
            if (_selectedDurationMinutes == minutes)
                return Color.FromArgb("#0F172A");

            bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return isDark ? Color.FromArgb("#F8FAFC") : Color.FromArgb("#0F172A");
        }

        private void NotifyDurationButtonsChanged()
        {
            OnPropertyChanged(nameof(Btn15BgColor));
            OnPropertyChanged(nameof(Btn15TextColor));
            OnPropertyChanged(nameof(Btn15FontAttributes));
            OnPropertyChanged(nameof(Btn25BgColor));
            OnPropertyChanged(nameof(Btn25TextColor));
            OnPropertyChanged(nameof(Btn25FontAttributes));
            OnPropertyChanged(nameof(Btn45BgColor));
            OnPropertyChanged(nameof(Btn45TextColor));
            OnPropertyChanged(nameof(Btn45FontAttributes));
            OnPropertyChanged(nameof(Btn60BgColor));
            OnPropertyChanged(nameof(Btn60TextColor));
            OnPropertyChanged(nameof(Btn60FontAttributes));
        }

        // --- CALENDARIO DINÁMICO ---
        private int _currentMonthIndex = DateTime.Now.Month;
        private int _currentYear = DateTime.Now.Year;
        private readonly string[] _monthNames = {
            "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
            "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
        };

        private string _currentMonthYearText = "";
        public string CurrentMonthYearText
        {
            get => _currentMonthYearText;
            set { _currentMonthYearText = value; OnPropertyChanged(); }
        }

        private ObservableCollection<CalendarDayModel> _calendarDays = new();
        public ObservableCollection<CalendarDayModel> CalendarDays
        {
            get => _calendarDays;
            set { _calendarDays = value; OnPropertyChanged(); }
        }

        // --- PROPIEDADES DE LA INTERFAZ ---
        public ObservableCollection<Course> EnrolledCourses { get; set; }
        public string NextDeadlineMessage { get; set; }
        private Dictionary<DateTime, List<ActivityItemViewModel>> _tasksByDate = new();

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
        public ICommand OpenCalendarDayCommand { get; }

        // Comandos MVVM para Tabs, Temporizador y Calendario
        public ICommand SelectTabCommand { get; }
        public ICommand SetTimerMinutesCommand { get; }
        public ICommand StartTimerCommand { get; }
        public ICommand PauseTimerCommand { get; }
        public ICommand ResetTimerCommand { get; }
        public ICommand PreviousMonthCommand { get; }
        public ICommand NextMonthCommand { get; }

        public MainViewModel(DatabaseService databaseService, EmailService emailService)
        {
            Instance = this;
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
            OpenCalendarDayCommand = new Command<CalendarDayModel>(async (day) => await OpenCalendarDayAsync(day));

            OpenCourseCommand = new Command<Course>(OnOpenCourse);
            OpenCourseFilesCommand = new Command<Course>(OnOpenCourseFiles);
            ViewPendingTaskCommand = new Command(OnNavigatePendingTasks);

            ChangeProfilePictureCommand = new Command(OnChangeProfilePicture);
            ToggleProfileMenuCommand = new Command(() => IsProfileMenuVisible = !IsProfileMenuVisible);
            ToggleDrawerCommand = new Command(() => IsDrawerVisible = !IsDrawerVisible);
            LogoutCommand = new Command(OnLogout);

            AddCourseCommand = new Command(OnAddCourse);
            DeleteCourseCommand = new Command<Course>(OnDeleteCourse);

            SelectTabCommand = new Command<string>(OnSelectTab);
            SetTimerMinutesCommand = new Command<string>(OnSetTimerMinutes);
            StartTimerCommand = new Command(async () => await OnStartTimerAsync());
            PauseTimerCommand = new Command(OnPauseTimer);
            ResetTimerCommand = new Command(OnResetTimer);
            PreviousMonthCommand = new Command(OnPreviousMonth);
            NextMonthCommand = new Command(OnNextMonth);

            CreateNotificationChannels();
            LoadCalendarDays();
            _ = LoadCoursesAsync();
        }

        public void SetCurrentUser(User user)
        {
            CurrentUser = user;
            if (user != null)
            {
                Preferences.Default.Set("LoggedInUserId", user.Id);

                string cloudTheme = string.IsNullOrWhiteSpace(user.AppThemePreference) ? "Sistema" : user.AppThemePreference;
                Preferences.Default.Set("UserAppTheme", cloudTheme);

                if (Application.Current != null)
                {
                    Application.Current.UserAppTheme = cloudTheme switch
                    {
                        "Claro" => AppTheme.Light,
                        "Oscuro" => AppTheme.Dark,
                        _ => AppTheme.Unspecified
                    };
                }

                NotifyDurationButtonsChanged();
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
        }

        // ==========================================
        // LÓGICA DEL TEMPORIZADOR Y SEGUNDO PLANO
        // ==========================================
        private void SetupTimerIfNeeded()
        {
            if (_timer == null && Application.Current != null)
            {
                _timer = Application.Current.Dispatcher.CreateTimer();
                _timer.Interval = TimeSpan.FromMilliseconds(500);
                _timer.Tick += (s, e) =>
                {
                    if (!_isTimerRunning) return;

                    int secondsLeft = (int)Math.Ceiling((_endTimeUtc - DateTime.UtcNow).TotalSeconds);

                    if (secondsLeft > 0)
                    {
                        if (secondsLeft != _remainingSeconds)
                        {
                            _remainingSeconds = secondsLeft;
                            UpdateTimerDisplay();
                        }

                        if (secondsLeft <= 180 && !Preferences.Default.Get("FocusThreeMinPromptShown", false))
                        {
                            Preferences.Default.Set("FocusThreeMinPromptShown", true);
#if ANDROID
                            ShowThreeMinuteExtensionNotificationStatic(Android.App.Application.Context);
#endif
                        }
                    }
                    else
                    {
                        CompleteFocusSession(showInAppAlert: true);
                    }
                };
            }
        }

        public void SyncTimerFromStorage()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                bool wasRunning = Preferences.Default.Get("FocusIsRunning", false);
                long savedEndTicks = Preferences.Default.Get("FocusEndTimeUtcTicks", 0L);

                if (wasRunning && savedEndTicks > 0)
                {
                    _endTimeUtc = new DateTime(savedEndTicks, DateTimeKind.Utc);
                    int secondsLeft = (int)Math.Ceiling((_endTimeUtc - DateTime.UtcNow).TotalSeconds);

                    if (secondsLeft > 0)
                    {
                        _remainingSeconds = secondsLeft;
                        _isTimerRunning = true;
                        TimerStatusText = "En curso • No molestar 🔕";
                        UpdateTimerDisplay();
                        SetupTimerIfNeeded();
                        if (_timer != null && !_timer.IsRunning) _timer.Start();

#if ANDROID
                        ShowCountdownNotificationStatic(Android.App.Application.Context, _remainingSeconds);
#endif
                    }
                    else
                    {
                        CompleteFocusSession(showInAppAlert: false);
                    }
                }
                else if (!wasRunning && _isTimerRunning)
                {
                    CompleteFocusSession(showInAppAlert: false);
                }
            });
        }

        private void CompleteFocusSession(bool showInAppAlert)
        {
            _timer?.Stop();
            _isTimerRunning = false;
            _remainingSeconds = 0;

            Preferences.Default.Set("FocusIsRunning", false);
            Preferences.Default.Set("FocusEndTimeUtcTicks", 0L);
            Preferences.Default.Set("FocusThreeMinPromptShown", false);

#if ANDROID
            var ctx = Android.App.Application.Context;
            SetDoNotDisturbStatic(ctx, false);
            CancelSystemAlarmsStatic(ctx);
#endif

            TimerStatusText = "¡Tiempo cumplido! 🎉";
            UpdateTimerDisplay();

            if (showInAppAlert)
            {
#if ANDROID
                ShowFinishedNotificationStatic(Android.App.Application.Context);
#endif
                Application.Current?.MainPage?.DisplayAlert("Modo Enfoque", "¡Excelente trabajo! Has completado tu sesión de concentración.", "OK");
            }
        }

        private void OnSetTimerMinutes(string minutesParam)
        {
            if (int.TryParse(minutesParam, out int minutes))
            {
                _selectedDurationMinutes = minutes;
                _remainingSeconds = minutes * 60;
                _timer?.Stop();
                _isTimerRunning = false;

                Preferences.Default.Set("FocusIsRunning", false);
                Preferences.Default.Set("FocusEndTimeUtcTicks", 0L);
                Preferences.Default.Set("FocusThreeMinPromptShown", false);

#if ANDROID
                var ctx = Android.App.Application.Context;
                SetDoNotDisturbStatic(ctx, false);
                CancelSystemAlarmsStatic(ctx);
                CancelAllFocusNotificationsStatic(ctx);
#endif

                TimerStatusText = "Listo para iniciar";
                UpdateTimerDisplay();
                NotifyDurationButtonsChanged();
            }
        }

        private async Task OnStartTimerAsync()
        {
            SetupTimerIfNeeded();
            if (!_isTimerRunning && _remainingSeconds > 0)
            {
                bool hasNotifPermission = await RequestNotificationPermissionAsync();
                await EnsureSystemPermissionsAsync();

                _endTimeUtc = DateTime.UtcNow.AddSeconds(_remainingSeconds);
                _isTimerRunning = true;

                Preferences.Default.Set("FocusIsRunning", true);
                Preferences.Default.Set("FocusEndTimeUtcTicks", _endTimeUtc.Ticks);
                Preferences.Default.Set("FocusThreeMinPromptShown", _remainingSeconds <= 180);

                TimerStatusText = "En curso • No molestar 🔕";
                _timer?.Start();

#if ANDROID
                var ctx = Android.App.Application.Context;
                SetDoNotDisturbStatic(ctx, true);
                ScheduleSystemAlarmsStatic(ctx, _endTimeUtc);

                if (hasNotifPermission)
                {
                    ShowCountdownNotificationStatic(ctx, _remainingSeconds);
                }
#endif
            }
        }

        private void OnPauseTimer()
        {
            if (_timer != null && _isTimerRunning)
            {
                _timer.Stop();
                _isTimerRunning = false;

                _remainingSeconds = Math.Max(0, (int)Math.Ceiling((_endTimeUtc - DateTime.UtcNow).TotalSeconds));

                Preferences.Default.Set("FocusIsRunning", false);
                Preferences.Default.Set("FocusEndTimeUtcTicks", 0L);

                TimerStatusText = "Pausado ⏸️";
                UpdateTimerDisplay();

#if ANDROID
                var ctx = Android.App.Application.Context;
                SetDoNotDisturbStatic(ctx, false);
                CancelSystemAlarmsStatic(ctx);
                CancelAllFocusNotificationsStatic(ctx);
#endif
            }
        }

        private void OnResetTimer()
        {
            OnSetTimerMinutes(_selectedDurationMinutes.ToString());
        }

        private void UpdateTimerDisplay()
        {
            int minutes = _remainingSeconds / 60;
            int seconds = _remainingSeconds % 60;
            TimerText = $"{minutes:D2}:{seconds:D2}";
        }

        // ==========================================
        // MÉTODOS NATIVOS DE ANDROID (NOTIFICACIONES Y DND)
        // ==========================================
        private void CreateNotificationChannels()
        {
#if ANDROID
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var notificationManager = (Android.App.NotificationManager)Android.App.Application.Context.GetSystemService(Context.NotificationService);

                var timerChannel = new Android.App.NotificationChannel(FocusChannelId, "Temporizador Modo Enfoque", Android.App.NotificationImportance.Low)
                {
                    Description = "Muestra el contador regresivo en tiempo real."
                };
                timerChannel.SetBypassDnd(true);

                var promptChannel = new Android.App.NotificationChannel(PromptChannelId, "Avisos de Extensión de Tiempo", Android.App.NotificationImportance.High)
                {
                    Description = "Pregunta si deseas añadir 5 minutos cuando quedan 3 minutos."
                };
                promptChannel.SetBypassDnd(true);
                promptChannel.EnableVibration(true);

                notificationManager?.CreateNotificationChannel(timerChannel);
                notificationManager?.CreateNotificationChannel(promptChannel);
            }
#endif
        }

        private async Task EnsureSystemPermissionsAsync()
        {
#if ANDROID
            var context = Android.App.Application.Context;
            var notificationManager = (Android.App.NotificationManager)context.GetSystemService(Context.NotificationService);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.M && notificationManager != null && !notificationManager.IsNotificationPolicyAccessGranted)
            {
                if (Application.Current?.MainPage != null)
                {
                    bool goToSettings = await Application.Current.MainPage.DisplayAlert(
                        "Permiso requerido",
                        "Para silenciar distracciones automáticamente, activa el permiso 'No molestar' para WorkFlow en la siguiente pantalla.",
                        "Ir a Ajustes",
                        "Cancelar");

                    if (goToSettings)
                    {
                        var intent = new Intent(Settings.ActionNotificationPolicyAccessSettings);
                        intent.AddFlags(ActivityFlags.NewTask);
                        context.StartActivity(intent);
                    }
                }
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                var alarmManager = (Android.App.AlarmManager)context.GetSystemService(Context.AlarmService);
                if (alarmManager != null && !alarmManager.CanScheduleExactAlarms())
                {
                    var intent = new Intent(Settings.ActionRequestScheduleExactAlarm);
                    intent.SetData(Android.Net.Uri.Parse($"package:{context.PackageName}"));
                    intent.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(intent);
                }
            }
#else
            await Task.CompletedTask;
#endif
        }

        private async Task<bool> RequestNotificationPermissionAsync()
        {
            var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.PostNotifications>();
            }
            return status == PermissionStatus.Granted;
        }

#if ANDROID
        public static void SetDoNotDisturbStatic(Context context, bool enable)
        {
            try
            {
                var notificationManager = (Android.App.NotificationManager)context.GetSystemService(Context.NotificationService);
                if (Build.VERSION.SdkInt >= BuildVersionCodes.M && notificationManager != null && notificationManager.IsNotificationPolicyAccessGranted)
                {
                    var filter = enable ? Android.App.InterruptionFilter.Priority : Android.App.InterruptionFilter.All;
                    notificationManager.SetInterruptionFilter(filter);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error DND: {ex.Message}");
            }
        }

        public static void ScheduleSystemAlarmsStatic(Context context, DateTime endTimeUtc)
        {
            var alarmManager = (Android.App.AlarmManager)context.GetSystemService(Context.AlarmService);
            if (alarmManager == null) return;

            long endMillis = new DateTimeOffset(endTimeUtc).ToUnixTimeMilliseconds();
            long warningMillis = new DateTimeOffset(endTimeUtc.AddMinutes(-3)).ToUnixTimeMilliseconds();
            long nowMillis = Java.Lang.JavaSystem.CurrentTimeMillis();

            var openAppIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName);
            var showAppPending = openAppIntent != null
                ? Android.App.PendingIntent.GetActivity(context, 0, openAppIntent, Android.App.PendingIntentFlags.UpdateCurrent | Android.App.PendingIntentFlags.Immutable)
                : null;

            var finishIntent = new Intent(context, typeof(FocusExtensionReceiver));
            finishIntent.SetAction(FocusExtensionReceiver.ActionTimerFinished);
            var finishPending = Android.App.PendingIntent.GetBroadcast(context, 301, finishIntent, Android.App.PendingIntentFlags.UpdateCurrent | Android.App.PendingIntentFlags.Immutable);

            ScheduleExactTrigger(alarmManager, endMillis, finishPending, showAppPending);

            if (warningMillis > nowMillis)
            {
                var warnIntent = new Intent(context, typeof(FocusExtensionReceiver));
                warnIntent.SetAction(FocusExtensionReceiver.ActionThreeMinWarning);
                var warnPending = Android.App.PendingIntent.GetBroadcast(context, 302, warnIntent, Android.App.PendingIntentFlags.UpdateCurrent | Android.App.PendingIntentFlags.Immutable);

                ScheduleExactTrigger(alarmManager, warningMillis, warnPending, showAppPending);
            }
        }

        private static void ScheduleExactTrigger(Android.App.AlarmManager alarmManager, long triggerAtMillis, Android.App.PendingIntent operation, Android.App.PendingIntent showIntent)
        {
            try
            {
                var alarmClockInfo = new Android.App.AlarmManager.AlarmClockInfo(triggerAtMillis, showIntent);
                alarmManager.SetAlarmClock(alarmClockInfo, operation);
            }
            catch
            {
                try
                {
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                        alarmManager.SetExactAndAllowWhileIdle(Android.App.AlarmType.RtcWakeup, triggerAtMillis, operation);
                    else
                        alarmManager.SetExact(Android.App.AlarmType.RtcWakeup, triggerAtMillis, operation);
                }
                catch
                {
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                        alarmManager.SetAndAllowWhileIdle(Android.App.AlarmType.RtcWakeup, triggerAtMillis, operation);
                }
            }
        }

        public static void CancelSystemAlarmsStatic(Context context)
        {
            var alarmManager = (Android.App.AlarmManager)context.GetSystemService(Context.AlarmService);
            if (alarmManager == null) return;

            var finishIntent = new Intent(context, typeof(FocusExtensionReceiver));
            finishIntent.SetAction(FocusExtensionReceiver.ActionTimerFinished);
            var finishPending = Android.App.PendingIntent.GetBroadcast(context, 301, finishIntent, Android.App.PendingIntentFlags.UpdateCurrent | Android.App.PendingIntentFlags.Immutable);
            alarmManager.Cancel(finishPending);

            var warnIntent = new Intent(context, typeof(FocusExtensionReceiver));
            warnIntent.SetAction(FocusExtensionReceiver.ActionThreeMinWarning);
            var warnPending = Android.App.PendingIntent.GetBroadcast(context, 302, warnIntent, Android.App.PendingIntentFlags.UpdateCurrent | Android.App.PendingIntentFlags.Immutable);
            alarmManager.Cancel(warnPending);
        }

        public static void ShowCountdownNotificationStatic(Context context, int remainingSeconds)
        {
            long endTimeMillis = Java.Lang.JavaSystem.CurrentTimeMillis() + (remainingSeconds * 1000L);

            var openAppIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName);
            var contentPendingIntent = openAppIntent != null
                ? Android.App.PendingIntent.GetActivity(context, 0, openAppIntent, Android.App.PendingIntentFlags.UpdateCurrent | Android.App.PendingIntentFlags.Immutable)
                : null;

            var builder = new NotificationCompat.Builder(context, FocusChannelId)
                .SetSmallIcon(Examen2.Resource.Mipmap.appicon)
                .SetContentTitle("WorkFlow • Modo Enfoque 🚀")
                .SetContentText("No molestar activado • Concentración en curso")
                .SetWhen(endTimeMillis)
                .SetShowWhen(true)
                .SetUsesChronometer(true)
                .SetChronometerCountDown(true)
                .SetOngoing(true)
                .SetOnlyAlertOnce(true)
                .SetCategory(NotificationCompat.CategoryAlarm)
                .SetVisibility(NotificationCompat.VisibilityPublic)
                .SetPriority(NotificationCompat.PriorityLow);

            if (contentPendingIntent != null)
                builder.SetContentIntent(contentPendingIntent);

            NotificationManagerCompat.From(context).Notify(FocusNotificationId, builder.Build());
        }

        public static void ShowThreeMinuteExtensionNotificationStatic(Context context)
        {
            var yesIntent = new Intent(context, typeof(FocusExtensionReceiver));
            yesIntent.SetAction(FocusExtensionReceiver.ActionAddFiveMinutes);
            var yesPendingIntent = Android.App.PendingIntent.GetBroadcast(context, 201, yesIntent, Android.App.PendingIntentFlags.UpdateCurrent | Android.App.PendingIntentFlags.Immutable);

            var noIntent = new Intent(context, typeof(FocusExtensionReceiver));
            noIntent.SetAction(FocusExtensionReceiver.ActionDismissPrompt);
            var noPendingIntent = Android.App.PendingIntent.GetBroadcast(context, 202, noIntent, Android.App.PendingIntentFlags.UpdateCurrent | Android.App.PendingIntentFlags.Immutable);

            var builder = new NotificationCompat.Builder(context, PromptChannelId)
                .SetSmallIcon(Examen2.Resource.Mipmap.appicon)
                .SetContentTitle("⏳ Quedan 3 minutos")
                .SetContentText("¿Quieres añadir 5 minutos más a tu sesión?")
                .SetPriority(NotificationCompat.PriorityMax)
                .SetCategory(NotificationCompat.CategoryAlarm)
                .SetVisibility(NotificationCompat.VisibilityPublic)
                .SetDefaults(NotificationCompat.DefaultAll)
                .SetAutoCancel(true)
                .AddAction(0, "Sí (+5 min)", yesPendingIntent)
                .AddAction(0, "No", noPendingIntent);

            NotificationManagerCompat.From(context).Notify(ExtensionPromptNotificationId, builder.Build());
        }

        public static void DismissExtensionNotificationStatic(Context context)
        {
            NotificationManagerCompat.From(context).Cancel(ExtensionPromptNotificationId);
        }

        public static void ShowFinishedNotificationStatic(Context context)
        {
            DismissExtensionNotificationStatic(context);

            var builder = new NotificationCompat.Builder(context, PromptChannelId)
                .SetSmallIcon(Examen2.Resource.Mipmap.appicon)
                .SetContentTitle("WorkFlow • ¡Tiempo cumplido! 🎉")
                .SetContentText("Has completado tu sesión y se desactivó No molestar.")
                .SetAutoCancel(true)
                .SetOngoing(false)
                .SetVisibility(NotificationCompat.VisibilityPublic)
                .SetPriority(NotificationCompat.PriorityHigh);

            NotificationManagerCompat.From(context).Notify(FocusNotificationId, builder.Build());
        }

        public static void CancelAllFocusNotificationsStatic(Context context)
        {
            var nm = NotificationManagerCompat.From(context);
            nm.Cancel(FocusNotificationId);
            nm.Cancel(ExtensionPromptNotificationId);
        }
#endif

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

            var days = new List<CalendarDayModel>(firstDayOfWeek + daysInMonth);

            for (int i = 0; i < firstDayOfWeek; i++)
            {
                days.Add(new CalendarDayModel { DayNumber = 0, HasTask = false });
            }

            for (int day = 1; day <= daysInMonth; day++)
            {
                var currentDate = new DateTime(_currentYear, _currentMonthIndex, day);
                _tasksByDate.TryGetValue(currentDate, out var tasksForThisDay);

                bool hasTask = tasksForThisDay != null && tasksForThisDay.Count > 0;
                string borderColor = "Transparent";

                if (hasTask)
                {
                    if (tasksForThisDay.Any(t => t.StatusColor == "#F87171"))
                        borderColor = "#F87171"; // Rojo: al menos una vencida
                    else if (tasksForThisDay.Any(t => t.StatusColor == "#FACC15"))
                        borderColor = "#FACC15"; // Amarillo: al menos una pendiente
                    else
                        borderColor = "#4ADE80"; // Verde: todas entregadas/calificadas
                }

                days.Add(new CalendarDayModel
                {
                    Date = currentDate,
                    DayNumber = day,
                    HasTask = hasTask,
                    TaskIndicatorColor = borderColor,
                    DayTasks = tasksForThisDay ?? new List<ActivityItemViewModel>()
                });
            }

            CalendarDays = new ObservableCollection<CalendarDayModel>(days);
        }

        private async Task OpenCalendarDayAsync(CalendarDayModel dayModel)
        {
            if (dayModel == null || !dayModel.HasTask || dayModel.DayTasks == null || dayModel.DayTasks.Count == 0)
                return;

            if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.Navigation.PushAsync(
                    new DayTasksPage(dayModel.Date, dayModel.DayTasks, _databaseService, this)
                );
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
            await LoadCoursesAsync();
        }

        private async Task LoadCoursesAsync()
        {
            if (CurrentUser == null)
            {
                EnrolledCourses.Clear();
                GlobalRecentActivities.Clear();
                _tasksByDate.Clear();
                LoadCalendarDays();
                return;
            }

            var courses = await _databaseService.GetCoursesAsync(CurrentUser.Id);
            var teacherCourseIds = await _databaseService.GetTeacherCourseIdsAsync(CurrentUser.Id);

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
            var completedTaskIds = await _databaseService.GetCompletedTaskIdsAsync(CurrentUser.Id);

            GlobalRecentActivities.Clear();
            _tasksByDate.Clear();

            int colorIndex = 0;
            string[] vibrantColors = { "#5EEAD4", "#5C93F9", "#86EFAC" };

            foreach (var a in activities)
            {
                bool isTeacherInThisCourse = teacherCourseIds.Contains(a.CourseId);
                var parentCourse = courses.FirstOrDefault(c => c.Id == a.CourseId);
                string courseName = parentCourse != null ? parentCourse.CourseName : "Clase";

                // Evaluar estado semáforo para el calendario
                if (a.ItemType == "Tarea" && a.DueDate.HasValue)
                {
                    string statusColor;
                    string statusLabel;

                    if (isTeacherInThisCourse)
                    {
                        bool isClosed = a.DueDate.Value < DateTime.Now;
                        statusColor = isClosed ? "#4ADE80" : "#FACC15";
                        statusLabel = isClosed ? $"Cerrada • {a.DueDate.Value:HH:mm}" : $"Activa • {a.DueDate.Value:HH:mm}";
                    }
                    else if (completedTaskIds.Contains(a.Id))
                    {
                        statusColor = "#4ADE80"; // Verde: entregada o calificada
                        statusLabel = $"Entregada • {a.DueDate.Value:HH:mm}";
                    }
                    else if (a.DueDate.Value < DateTime.Now)
                    {
                        statusColor = "#F87171"; // Rojo: vencida sin entregar
                        statusLabel = $"Vencida • {a.DueDate.Value:HH:mm}";
                    }
                    else
                    {
                        statusColor = "#FACC15"; // Amarillo: pendiente de entrega
                        statusLabel = $"Pendiente • {a.DueDate.Value:HH:mm}";
                    }

                    var dateKey = a.DueDate.Value.Date;
                    if (!_tasksByDate.TryGetValue(dateKey, out var dayList))
                    {
                        dayList = new List<ActivityItemViewModel>();
                        _tasksByDate[dateKey] = dayList;
                    }

                    dayList.Add(new ActivityItemViewModel
                    {
                        OriginalItem = a,
                        DisplayTitle = a.Title,
                        Subtitle = $"{courseName} • {(isTeacherInThisCourse ? "Clase impartida" : "Clase inscrita")}",
                        GradientStartColor = statusColor,
                        StatusColor = statusColor,
                        IconString = "📋",
                        HasDueDate = true,
                        DueDateString = statusLabel
                    });
                }

                if (isTeacherInThisCourse && a.ItemType == "Tarea")
                {
                    continue;
                }

                if (a.ItemType == "Tarea" && a.DueDate.HasValue && a.DueDate.Value < DateTime.Now)
                {
                    continue;
                }

                string gradient = a.ItemType switch
                {
                    "Tarea" => vibrantColors[colorIndex++ % vibrantColors.Length],
                    "Material" => "#5EEAD4",
                    _ => "#86EFAC"
                };

                string icon = a.ItemType switch
                {
                    "Tarea" => "📋",
                    "Material" => "📖",
                    _ => "📢"
                };

                GlobalRecentActivities.Add(new ActivityItemViewModel
                {
                    OriginalItem = a,
                    DisplayTitle = $"{a.ItemType}: {a.Title}",
                    Subtitle = $"{courseName} • Publicado el {a.CreatedAt:dd MMM}",
                    GradientStartColor = gradient,
                    StatusColor = gradient,
                    IconString = icon,
                    HasDueDate = a.DueDate.HasValue,
                    DueDateString = a.DueDate.HasValue ? $"Entrega: {a.DueDate.Value:dd/MM/yyyy HH:mm}" : ""
                });
            }

            LoadCalendarDays();
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

        private async Task OpenActivityAsync(ActivityItemViewModel activity)
        {
            if (activity?.OriginalItem == null || Application.Current?.MainPage == null) return;

            string role = await _databaseService.GetUserRoleInCourseAsync(activity.OriginalItem.CourseId, CurrentUser.Id);
            bool isTeacher = string.Equals(role, "Teacher", StringComparison.OrdinalIgnoreCase);

            if (isTeacher && activity.OriginalItem.ItemType == "Tarea")
            {
                await Application.Current.MainPage.Navigation.PushAsync(
                    new TeacherTaskOverviewPage(activity.OriginalItem, activity.OriginalItem.CourseId, _databaseService, this)
                );
            }
            else
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
        public string StatusColor { get; set; } = "#FACC15";
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
        public DateTime Date { get; set; }
        public int DayNumber { get; set; }
        public string DayNumberText => DayNumber > 0 ? DayNumber.ToString() : string.Empty;
        public bool HasTask { get; set; }
        public string TaskIndicatorColor { get; set; } = "Transparent";
        public List<ActivityItemViewModel> DayTasks { get; set; } = new();
        public double BorderThickness => HasTask ? 1.5 : 1.0;
        public string BorderColor => HasTask ? TaskIndicatorColor : "#2D3748";
        public FontAttributes DayFontAttributes => HasTask ? FontAttributes.Bold : FontAttributes.None;
        public Color DayTextColor => HasTask ? Colors.White : Color.FromArgb("#64748B");
    }
}