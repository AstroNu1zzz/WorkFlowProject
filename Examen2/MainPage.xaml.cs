using Examen2.Data;
using Examen2.Services;
using Examen2.ViewModels;

#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using AndroidX.Core.App;
#endif

namespace Examen2;

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

#if ANDROID
[BroadcastReceiver(Enabled = true, Exported = true)]
public class FocusExtensionReceiver : BroadcastReceiver
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
                MainPage.ScheduleSystemAlarmsStatic(context, newEndTime);
                MainPage.ShowCountdownNotificationStatic(context, newRemaining);
            }
            MainPage.DismissExtensionNotificationStatic(context);
            MainPage.Instance?.SyncTimerFromStorage();
        }
        else if (action == ActionDismissPrompt)
        {
            MainPage.DismissExtensionNotificationStatic(context);
        }
        else if (action == ActionThreeMinWarning)
        {
            bool alreadyShown = Preferences.Default.Get("FocusThreeMinPromptShown", false);
            bool isRunning = Preferences.Default.Get("FocusIsRunning", false);

            if (isRunning && !alreadyShown)
            {
                Preferences.Default.Set("FocusThreeMinPromptShown", true);
                MainPage.ShowThreeMinuteExtensionNotificationStatic(context);
            }
        }
        else if (action == ActionTimerFinished)
        {
            Preferences.Default.Set("FocusIsRunning", false);
            Preferences.Default.Set("FocusEndTimeUtcTicks", 0L);
            Preferences.Default.Set("FocusThreeMinPromptShown", false);
            MainPage.SetDoNotDisturbStatic(context, false);
            MainPage.ShowFinishedNotificationStatic(context);
            MainPage.Instance?.SyncTimerFromStorage();
        }
    }
}
#endif

public partial class MainPage : ContentPage
{
    public static MainPage Instance { get; private set; }

    private readonly DatabaseService _databaseService;
    private readonly EmailService _emailService;
    private readonly MainViewModel _mainViewModel;

    // Variables del temporizador
    private int _selectedDurationMinutes = 25;
    private Button _selectedDurationButton;
    private int _remainingSeconds = 1500;
    private bool _isTimerRunning = false;
    private DateTime _endTimeUtc;
    private IDispatcherTimer _timer;

    // Constantes de notificaciones (v2 para aplicar BypassDnd en Android)
    public const string FocusChannelId = "workflow_focus_timer_channel_v2";
    public const string PromptChannelId = "workflow_focus_prompt_channel_v2";
    public const int FocusNotificationId = 1001;
    public const int ExtensionPromptNotificationId = 1002;

    // Variables del Calendario
    private int _currentMonthIndex = 11;
    private int _currentYear = 2026;

    private readonly string[] _monthNames = {
        "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
    };

    public MainPage(DatabaseService databaseService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        Instance = this;
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;
        BindingContext = _mainViewModel;
        _selectedDurationButton = Btn25Min;
        CreateNotificationChannels();
        LoadCalendarDays();
    }

    public MainPage(DatabaseService databaseService, EmailService emailService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        Instance = this;
        _databaseService = databaseService;
        _emailService = emailService;
        _mainViewModel = mainViewModel;
        BindingContext = _mainViewModel;
        _selectedDurationButton = Btn25Min;
        CreateNotificationChannels();
        LoadCalendarDays();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Instance = this;

        if (BindingContext is MainViewModel viewModel)
        {
            await viewModel.RefreshCoursesAsync();
        }

        SyncTimerFromStorage();
        LoadCalendarDays();
    }

    // ==========================================
    // SINCRONIZACIÓN DE ESTADO AL SALIR Y VOLVER
    // ==========================================
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
                    LblTimerStatus.Text = "En curso • No molestar 🔕";
                    UpdateTimerDisplay();
                    SetupTimerIfNeeded();
                    if (!_timer.IsRunning) _timer.Start();

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
        if (_timer != null) _timer.Stop();
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

        LblTimerStatus.Text = "¡Tiempo cumplido! 🎉";
        UpdateTimerDisplay();

        if (showInAppAlert)
        {
#if ANDROID
            ShowFinishedNotificationStatic(Android.App.Application.Context);
#endif
            DisplayAlert("Modo Enfoque", "¡Excelente trabajo! Has completado tu sesión de concentración.", "OK");
        }
    }

    // ==========================================
    // MÉTODOS NATIVOS ESTÁTICOS (ANDROID)
    // ==========================================
    private void CreateNotificationChannels()
    {
#if ANDROID
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var notificationManager = (NotificationManager)Android.App.Application.Context.GetSystemService(Context.NotificationService);

            var timerChannel = new NotificationChannel(FocusChannelId, "Temporizador Modo Enfoque", NotificationImportance.Low)
            {
                Description = "Muestra el contador regresivo en tiempo real."
            };
            timerChannel.SetBypassDnd(true);

            var promptChannel = new NotificationChannel(PromptChannelId, "Avisos de Extensión de Tiempo", NotificationImportance.High)
            {
                Description = "Pregunta si deseas añadir 5 minutos cuando quedan 3 minutos."
            };
            promptChannel.SetBypassDnd(true); // Permite mostrar la notificación encima de No Molestar
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
        var notificationManager = (NotificationManager)context.GetSystemService(Context.NotificationService);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.M && notificationManager != null && !notificationManager.IsNotificationPolicyAccessGranted)
        {
            bool goToSettings = await DisplayAlert(
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

        // Verifica permiso de alarmas exactas en Android 12+
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            var alarmManager = (AlarmManager)context.GetSystemService(Context.AlarmService);
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
            var notificationManager = (NotificationManager)context.GetSystemService(Context.NotificationService);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M && notificationManager != null && notificationManager.IsNotificationPolicyAccessGranted)
            {
                var filter = enable ? InterruptionFilter.Priority : InterruptionFilter.All;
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
        var alarmManager = (AlarmManager)context.GetSystemService(Context.AlarmService);
        if (alarmManager == null) return;

        long endMillis = new DateTimeOffset(endTimeUtc).ToUnixTimeMilliseconds();
        long warningMillis = new DateTimeOffset(endTimeUtc.AddMinutes(-3)).ToUnixTimeMilliseconds();
        long nowMillis = Java.Lang.JavaSystem.CurrentTimeMillis();

        var openAppIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName);
        var showAppPending = openAppIntent != null
            ? PendingIntent.GetActivity(context, 0, openAppIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)
            : null;

        // 1. Alarma exacta de fin de sesión
        var finishIntent = new Intent(context, typeof(FocusExtensionReceiver));
        finishIntent.SetAction(FocusExtensionReceiver.ActionTimerFinished);
        var finishPending = PendingIntent.GetBroadcast(context, 301, finishIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        ScheduleExactTrigger(alarmManager, endMillis, finishPending, showAppPending);

        // 2. Alarma exacta cuando falten 3 minutos
        if (warningMillis > nowMillis)
        {
            var warnIntent = new Intent(context, typeof(FocusExtensionReceiver));
            warnIntent.SetAction(FocusExtensionReceiver.ActionThreeMinWarning);
            var warnPending = PendingIntent.GetBroadcast(context, 302, warnIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            ScheduleExactTrigger(alarmManager, warningMillis, warnPending, showAppPending);
        }
    }

    private static void ScheduleExactTrigger(AlarmManager alarmManager, long triggerAtMillis, PendingIntent operation, PendingIntent showIntent)
    {
        try
        {
            // SetAlarmClock tiene máxima prioridad en Android 15 y no lo retrasa el ahorro de batería
            var alarmClockInfo = new AlarmManager.AlarmClockInfo(triggerAtMillis, showIntent);
            alarmManager.SetAlarmClock(alarmClockInfo, operation);
        }
        catch
        {
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                    alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, operation);
                else
                    alarmManager.SetExact(AlarmType.RtcWakeup, triggerAtMillis, operation);
            }
            catch
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                    alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, operation);
            }
        }
    }

    public static void CancelSystemAlarmsStatic(Context context)
    {
        var alarmManager = (AlarmManager)context.GetSystemService(Context.AlarmService);
        if (alarmManager == null) return;

        var finishIntent = new Intent(context, typeof(FocusExtensionReceiver));
        finishIntent.SetAction(FocusExtensionReceiver.ActionTimerFinished);
        var finishPending = PendingIntent.GetBroadcast(context, 301, finishIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        alarmManager.Cancel(finishPending);

        var warnIntent = new Intent(context, typeof(FocusExtensionReceiver));
        warnIntent.SetAction(FocusExtensionReceiver.ActionThreeMinWarning);
        var warnPending = PendingIntent.GetBroadcast(context, 302, warnIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        alarmManager.Cancel(warnPending);
    }

    public static void ShowCountdownNotificationStatic(Context context, int remainingSeconds)
    {
        long endTimeMillis = Java.Lang.JavaSystem.CurrentTimeMillis() + (remainingSeconds * 1000L);

        var openAppIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName);
        var contentPendingIntent = openAppIntent != null
            ? PendingIntent.GetActivity(context, 0, openAppIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)
            : null;

        var builder = new NotificationCompat.Builder(context, FocusChannelId)
            .SetSmallIcon(Resource.Mipmap.appicon)
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
        var yesPendingIntent = PendingIntent.GetBroadcast(context, 201, yesIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var noIntent = new Intent(context, typeof(FocusExtensionReceiver));
        noIntent.SetAction(FocusExtensionReceiver.ActionDismissPrompt);
        var noPendingIntent = PendingIntent.GetBroadcast(context, 202, noIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(context, PromptChannelId)
            .SetSmallIcon(Resource.Mipmap.appicon)
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
            .SetSmallIcon(Resource.Mipmap.appicon)
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

    // ==========================================
    // EVENTOS DE PERFIL Y NAVEGACIÓN INFERIOR
    // ==========================================
    private async void OnMenuEditProfileClicked(object sender, EventArgs e)
    {
        if (_mainViewModel != null)
        {
            _mainViewModel.IsProfileMenuVisible = false;
        }
        await Navigation.PushAsync(new EditProfilePage(_databaseService, _emailService, _mainViewModel));
    }

    private void OnTabClasesTapped(object sender, EventArgs e) => SetTab(ViewClases, TabClasesLabel, TabClasesIndicator);
    private void OnTabTareasTapped(object sender, EventArgs e) => SetTab(ViewTareas, TabTareasLabel, TabTareasIndicator);
    private void OnTabEnfoqueTapped(object sender, EventArgs e) => SetTab(ViewEnfoque, TabEnfoqueLabel, TabEnfoqueIndicator);

    private void OnTabCalendarioTapped(object sender, EventArgs e)
    {
        SetTab(ViewCalendario, TabCalendarioLabel, TabCalendarioIndicator);
        LoadCalendarDays();
    }

    // ==========================================
    // MODO ENFOQUE - CONTROLES DEL TEMPORIZADOR
    // ==========================================
    private void SetupTimerIfNeeded()
    {
        if (_timer == null)
        {
            _timer = Dispatcher.CreateTimer();
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

                    // Respaldo inmediato si el usuario está viendo la app al llegar a 3 minutos
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

    private void UpdateTimerDisplay()
    {
        int minutes = _remainingSeconds / 60;
        int seconds = _remainingSeconds % 60;
        LblTimer.Text = $"{minutes:D2}:{seconds:D2}";
    }

    private void UpdateDurationButtonsVisuals(Button selectedButton)
    {
        bool isDark = Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark;
        Color inactiveBg = isDark ? Color.FromArgb("#2D3748") : Color.FromArgb("#E2E8F0");
        Color inactiveText = isDark ? Color.FromArgb("#F8FAFC") : Color.FromArgb("#0F172A");

        Color activeBg = Color.FromArgb("#5EEAD4");
        Color activeText = Color.FromArgb("#0F172A");

        Button[] buttons = { Btn15Min, Btn25Min, Btn45Min, Btn60Min };

        foreach (var btn in buttons)
        {
            if (btn == selectedButton)
            {
                btn.BackgroundColor = activeBg;
                btn.TextColor = activeText;
                btn.FontAttributes = FontAttributes.Bold;
            }
            else
            {
                btn.BackgroundColor = inactiveBg;
                btn.TextColor = inactiveText;
                btn.FontAttributes = FontAttributes.None;
            }
        }
    }

    private void SetTimerMinutes(int minutes, Button selectedButton)
    {
        _selectedDurationMinutes = minutes;
        _selectedDurationButton = selectedButton;
        _remainingSeconds = minutes * 60;

        if (_timer != null) _timer.Stop();
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

        LblTimerStatus.Text = "Listo para iniciar";
        UpdateTimerDisplay();
        UpdateDurationButtonsVisuals(selectedButton);
    }

    private void OnSet15MinutesClicked(object sender, EventArgs e) => SetTimerMinutes(15, Btn15Min);
    private void OnSet25MinutesClicked(object sender, EventArgs e) => SetTimerMinutes(25, Btn25Min);
    private void OnSet45MinutesClicked(object sender, EventArgs e) => SetTimerMinutes(45, Btn45Min);
    private void OnSet60MinutesClicked(object sender, EventArgs e) => SetTimerMinutes(60, Btn60Min);

    private async void OnStartTimerClicked(object sender, EventArgs e)
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

            LblTimerStatus.Text = "En curso • No molestar 🔕";
            _timer.Start();

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

    private void OnPauseTimerClicked(object sender, EventArgs e)
    {
        if (_timer != null && _isTimerRunning)
        {
            _timer.Stop();
            _isTimerRunning = false;

            _remainingSeconds = Math.Max(0, (int)Math.Ceiling((_endTimeUtc - DateTime.UtcNow).TotalSeconds));

            Preferences.Default.Set("FocusIsRunning", false);
            Preferences.Default.Set("FocusEndTimeUtcTicks", 0L);

            LblTimerStatus.Text = "Pausado ⏸️";
            UpdateTimerDisplay();

#if ANDROID
            var ctx = Android.App.Application.Context;
            SetDoNotDisturbStatic(ctx, false);
            CancelSystemAlarmsStatic(ctx);
            CancelAllFocusNotificationsStatic(ctx);
#endif
        }
    }

    private void OnResetTimerClicked(object sender, EventArgs e)
    {
        Button targetButton = _selectedDurationButton ?? Btn25Min;
        SetTimerMinutes(_selectedDurationMinutes, targetButton);
    }

    // ==========================================
    // LÓGICA DE NAVEGACIÓN DEL CALENDARIO
    // ==========================================
    private void OnPreviousMonthClicked(object sender, EventArgs e)
    {
        _currentMonthIndex--;
        if (_currentMonthIndex < 1)
        {
            _currentMonthIndex = 12;
            _currentYear--;
        }
        UpdateCalendarHeader();
        LoadCalendarDays();
    }

    private void OnNextMonthClicked(object sender, EventArgs e)
    {
        _currentMonthIndex++;
        if (_currentMonthIndex > 12)
        {
            _currentMonthIndex = 1;
            _currentYear++;
        }
        UpdateCalendarHeader();
        LoadCalendarDays();
    }

    private void UpdateCalendarHeader()
    {
        if (LblMonthYear != null)
        {
            LblMonthYear.Text = $"{_monthNames[_currentMonthIndex - 1]} de {_currentYear}";
        }
    }

    private void LoadCalendarDays()
    {
        UpdateCalendarHeader();

        int daysInMonth = DateTime.DaysInMonth(_currentYear, _currentMonthIndex);
        var firstDayOfWeek = (int)new DateTime(_currentYear, _currentMonthIndex, 1).DayOfWeek;

        List<CalendarDayModel> calendarDays = new List<CalendarDayModel>();

        for (int i = 0; i < firstDayOfWeek; i++)
        {
            calendarDays.Add(new CalendarDayModel { DayNumber = 0, HasTask = false });
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

            calendarDays.Add(new CalendarDayModel
            {
                DayNumber = day,
                HasTask = hasTask,
                TaskIndicatorColor = taskColor
            });
        }

        if (CalendarCollectionView != null)
        {
            CalendarCollectionView.ItemsSource = calendarDays;
        }
    }

    private void SetTab(ScrollView activeView, Label activeLabel, BoxView activeIndicator)
    {
        ViewClases.IsVisible = (activeView == ViewClases);
        ViewTareas.IsVisible = (activeView == ViewTareas);
        ViewEnfoque.IsVisible = (activeView == ViewEnfoque);
        ViewCalendario.IsVisible = (activeView == ViewCalendario);

        Color inactiveColor = Color.FromArgb("#64748B");
        Color activeColor = Color.FromArgb("#5EEAD4");

        TabClasesLabel.TextColor = inactiveColor;
        TabClasesIndicator.Color = Colors.Transparent;

        TabTareasLabel.TextColor = inactiveColor;
        TabTareasIndicator.Color = Colors.Transparent;

        TabEnfoqueLabel.TextColor = inactiveColor;
        TabEnfoqueIndicator.Color = Colors.Transparent;

        TabCalendarioLabel.TextColor = inactiveColor;
        TabCalendarioIndicator.Color = Colors.Transparent;

        activeLabel.TextColor = activeColor;
        activeIndicator.Color = activeColor;
    }
}