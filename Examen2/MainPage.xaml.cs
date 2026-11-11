using Examen2.Data;
using Examen2.ViewModels;

namespace Examen2;

public partial class MainPage : ContentPage
{
    private readonly DatabaseService _databaseService;
    private readonly MainViewModel _mainViewModel;

    public MainPage(DatabaseService databaseService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;
        BindingContext = _mainViewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is MainViewModel viewModel)
        {
            await viewModel.RefreshCoursesAsync();
        }
    }

    // CONTROLADORES DE EVENTOS PARA LA NAVEGACIÓN DE PESTAÑAS
    private void OnTabClasesTapped(object sender, EventArgs e)
    {
        SetTab(ViewClases, TabClasesLabel, TabClasesIndicator);
    }

    private void OnTabTareasTapped(object sender, EventArgs e)
    {
        SetTab(ViewTareas, TabTareasLabel, TabTareasIndicator);
    }

    private void OnTabCalendarioTapped(object sender, EventArgs e)
    {
        SetTab(ViewCalendario, TabCalendarioLabel, TabCalendarioIndicator);
    }

    private void OnTabAjustesTapped(object sender, EventArgs e)
    {
        // Invoca el comando de navegación a configuración existente en MainViewModel
        if (_mainViewModel?.NavigateSettingsCommand != null && _mainViewModel.NavigateSettingsCommand.CanExecute(null))
        {
            _mainViewModel.NavigateSettingsCommand.Execute(null);
        }
    }

    // MÉTODO PARA ALTERNAR LA VISIBILIDAD DE VISTAS Y COLORES DE INDICADORES
    private void SetTab(ScrollView activeView, Label activeLabel, BoxView activeIndicator)
    {
        ViewClases.IsVisible = (activeView == ViewClases);
        ViewTareas.IsVisible = (activeView == ViewTareas);
        ViewCalendario.IsVisible = (activeView == ViewCalendario);

        Color inactiveColor = Color.FromArgb("#64748B");
        Color activeColor = Color.FromArgb("#5EEAD4");

        TabClasesLabel.TextColor = inactiveColor;
        TabClasesIndicator.Color = Colors.Transparent;

        TabTareasLabel.TextColor = inactiveColor;
        TabTareasIndicator.Color = Colors.Transparent;

        TabCalendarioLabel.TextColor = inactiveColor;
        TabCalendarioIndicator.Color = Colors.Transparent;

        TabAjustesLabel.TextColor = inactiveColor;
        TabAjustesIndicator.Color = Colors.Transparent;

        activeLabel.TextColor = activeColor;
        activeIndicator.Color = activeColor;
    }
}