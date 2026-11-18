using Examen2.Data;
using Examen2.Services;
using Examen2.ViewModels;

namespace Examen2;

public partial class MainPage : ContentPage
{
    public MainPage(DatabaseService databaseService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        BindingContext = mainViewModel;
    }

    public MainPage(DatabaseService databaseService, EmailService emailService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        BindingContext = mainViewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is MainViewModel viewModel)
        {
            await viewModel.RefreshCoursesAsync();
        }
    }
}