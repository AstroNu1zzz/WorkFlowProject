using Examen2.Data;
using Examen2.ViewModels;
using Examen2.Services;


#if ANDROID
// Espacios de nombres o llamadas exclusivas de Android (si tu proyecto los requiere)
using Android.Content;
using Android.Views;
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

public partial class MainPage : ContentPage
{
    private readonly DatabaseService _databaseService;
    private readonly EmailService _emailService;
    private readonly MainViewModel _mainViewModel;

    // Variables para el temporizador del Modo Enfoque
    private int _remainingSeconds = 1500; // 25 minutos por defecto
    private bool _isTimerRunning = false;
    private IDispatcherTimer _timer;

    // Variables para la navegación del Calendario (Meses)
    private int _currentMonthIndex = 11; // Noviembre (11)
    private int _currentYear = 2026;

    private readonly string[] _monthNames = {
        "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
    };

    public MainPage(DatabaseService databaseService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;
        BindingContext = _mainViewModel;
        LoadCalendarDays();
    }

    public MainPage(DatabaseService databaseService, EmailService emailService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _emailService = emailService;
        _mainViewModel = mainViewModel;
        BindingContext = _mainViewModel;
        LoadCalendarDays();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is MainViewModel viewModel)
        {
            await viewModel.RefreshCoursesAsync();
        }
        LoadCalendarDays();
    }

    // EVENTO PARA ABRIR LA PÁGINA DE EDITAR PERFIL DESDE EL MENÚ FLOTANTE
    private async void OnMenuEditProfileClicked(object sender, EventArgs e)
    {
        if (_mainViewModel != null)
        {
            _mainViewModel.IsProfileMenuVisible = false;
        }
        await Navigation.PushAsync(new EditProfilePage(_databaseService, _emailService, _mainViewModel));
    }

    // CONTROLADORES DE EVENTOS PARA LA NAVEGACIÓN DE PESTAÑAS INFERIORES
    private void OnTabClasesTapped(object sender, EventArgs e)
    {
        SetTab(ViewClases, TabClasesLabel, TabClasesIndicator);
    }

    private void OnTabTareasTapped(object sender, EventArgs e)
    {
        SetTab(ViewTareas, TabTareasLabel, TabTareasIndicator);
    }

    private void OnTabEnfoqueTapped(object sender, EventArgs e)
    {
        SetTab(ViewEnfoque, TabEnfoqueLabel, TabEnfoqueIndicator);
    }

    private void OnTabCalendarioTapped(object sender, EventArgs e)
    {
        SetTab(ViewCalendario, TabCalendarioLabel, TabCalendarioIndicator);
        LoadCalendarDays();
    }

    // MODO ENFOQUE - LÓGICA DEL TEMPORIZADOR
    private void SetupTimerIfNeeded()
    {
        if (_timer == null)
        {
            _timer = Dispatcher.CreateTimer();
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
                    LblTimerStatus.Text = "¡Tiempo cumplido! 🎉";
                    DisplayAlert("Modo Enfoque", "¡Excelente trabajo! Has completado tu sesión de concentración.", "OK");
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

    private void SetTimerMinutes(int minutes)
    {
        _remainingSeconds = minutes * 60;
        if (_timer != null) _timer.Stop();
        _isTimerRunning = false;
        LblTimerStatus.Text = "Listo para iniciar";
        UpdateTimerDisplay();
    }

    private void OnSet15MinutesClicked(object sender, EventArgs e) => SetTimerMinutes(15);
    private void OnSet25MinutesClicked(object sender, EventArgs e) => SetTimerMinutes(25);
    private void OnSet45MinutesClicked(object sender, EventArgs e) => SetTimerMinutes(45);
    private void OnSet60MinutesClicked(object sender, EventArgs e) => SetTimerMinutes(60);

    private void OnStartTimerClicked(object sender, EventArgs e)
    {
        SetupTimerIfNeeded();
        if (!_isTimerRunning)
        {
            _isTimerRunning = true;
            LblTimerStatus.Text = "En curso 🚀";
            _timer.Start();
        }
    }

    private void OnPauseTimerClicked(object sender, EventArgs e)
    {
        if (_timer != null && _isTimerRunning)
        {
            _timer.Stop();
            _isTimerRunning = false;
            LblTimerStatus.Text = "Pausado ⏸️";
        }
    }

    private void OnResetTimerClicked(object sender, EventArgs e)
    {
        if (_timer != null) _timer.Stop();
        _isTimerRunning = false;
        _remainingSeconds = 1500; // Vuelve a 25 minutos por defecto
        LblTimerStatus.Text = "Listo para iniciar";
        UpdateTimerDisplay();
    }

    // LÓGICA DE NAVEGACIÓN Y GENERACIÓN DINÁMICA DEL CALENDARIO
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

    // MÉTODO PARA ALTERNAR LA VISIBILIDAD DE VISTAS Y COLORES DE INDICADORES
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