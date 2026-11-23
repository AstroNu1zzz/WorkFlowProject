using Examen2.Data;
using Examen2.Services;
using Examen2.ViewModels;

namespace Examen2;

public partial class MainPage : ContentPage
{
    public static MainPage Instance { get; private set; }

    private readonly DatabaseService _databaseService;
    private readonly EmailService _emailService;
    private readonly MainViewModel _mainViewModel;

    public MainPage(DatabaseService databaseService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        Instance = this;
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;
        BindingContext = _mainViewModel;
    }

    public MainPage(DatabaseService databaseService, EmailService emailService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        Instance = this;
        _databaseService = databaseService;
        _emailService = emailService;
        _mainViewModel = mainViewModel;
        BindingContext = _mainViewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Instance = this;

        if (_mainViewModel != null)
        {
            await _mainViewModel.RefreshCoursesAsync();
            _mainViewModel.SyncTimerFromStorage();
        }
    }

    public void SyncTimerFromStorage()
    {
        _mainViewModel?.SyncTimerFromStorage();
    }
}