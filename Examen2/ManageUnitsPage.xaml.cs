
using Examen2.Data;

namespace Examen2;

public partial class ManageUnitsPage : ContentPage
{
    private readonly int _courseId;
    private readonly DatabaseService _databaseService;
    private CourseUnit _selectedUnit;
    private List<UnitEvaluationCriterion> _currentCriteria = new();

    public ManageUnitsPage(int courseId, DatabaseService databaseService)
    {
        InitializeComponent();
        _courseId = courseId;
        _databaseService = databaseService;

        DpStart.Date = DateTime.Now;
        DpEnd.Date = DateTime.Now.AddDays(30);

        _ = LoadUnitsAsync();
    }

    private async Task LoadUnitsAsync()
    {
        var units = await _databaseService.GetCourseUnitsAsync(_courseId);
        CvUnits.ItemsSource = units;
    }

    private async void OnAddUnitClicked(object sender, EventArgs e)
    {
        string title = TxtUnitTitle.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            await DisplayAlert("Atención", "Ingresa un nombre para la unidad.", "OK");
            return;
        }

        DateTime rawStart = DpStart.Date ?? DateTime.Now;
        DateTime rawEnd = DpEnd.Date ?? DateTime.Now.AddDays(30);

        DateTime startDate = rawStart.Date.AddHours(12);
        DateTime endDate = rawEnd.Date.AddHours(12);

        if (endDate < startDate)
        {
            await DisplayAlert("Error", "La fecha fin debe ser posterior a la fecha de inicio.", "OK");
            return;
        }

        // VALIDACIÓN DE TRASLAPE: Verificar si el rango [startDate, endDate] se encima con alguna unidad existente
        var existingUnits = await _databaseService.GetCourseUnitsAsync(_courseId);
        var overlappingUnit = existingUnits.FirstOrDefault(u => startDate.Date <= u.EndDate.Date && endDate.Date >= u.StartDate.Date);

        if (overlappingUnit != null)
        {
            await DisplayAlert("Rango Inválido",
                $"Las fechas ingresadas se enciman con la unidad '{overlappingUnit.Title}' ({overlappingUnit.StartDate:dd/MM/yyyy} al {overlappingUnit.EndDate:dd/MM/yyyy}). Ajusta las fechas.",
                "OK");
            return;
        }

        var newUnit = new CourseUnit
        {
            CourseId = _courseId,
            Title = title,
            StartDate = startDate,
            EndDate = endDate
        };

        bool ok = await _databaseService.AddCourseUnitAsync(newUnit);
        if (ok)
        {
            TxtUnitTitle.Text = string.Empty;
            await LoadUnitsAsync();
        }
        else
        {
            await DisplayAlert("Error", "No se pudo guardar la unidad.", "OK");
        }
    }

    private async void OnUnitSelectedTapped(object sender, TappedEventArgs e)
    {
        var border = sender as Border;
        _selectedUnit = border?.BindingContext as CourseUnit;

        if (_selectedUnit == null) return;

        LblSelectedUnitTitle.Text = $"Criterios de: {_selectedUnit.Title}";
        BorderCriteriaPanel.IsVisible = true;
        await RefreshCriteriaAsync();
    }

    private async Task RefreshCriteriaAsync()
    {
        if (_selectedUnit == null) return;

        _currentCriteria = await _databaseService.GetUnitCriteriaAsync(_selectedUnit.Id);
        CvCriteria.ItemsSource = _currentCriteria;

        double currentTotal = _currentCriteria.Sum(c => c.Percentage);
        LblTotalPercentage.Text = $"Total Criterios: {currentTotal}% / 100%";
        LblTotalPercentage.TextColor = currentTotal == 100 ? Color.FromArgb("#5EEAD4") : Color.FromArgb("#F87171");
    }

    private async void OnAddCriterionClicked(object sender, EventArgs e)
    {
        if (_selectedUnit == null) return;

        string name = TxtCriterionName.Text?.Trim();
        string percText = TxtCriterionPercentage.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name) || !double.TryParse(percText, out double perc) || perc <= 0)
        {
            await DisplayAlert("Error", "Ingresa un nombre y un porcentaje válido mayor a 0.", "OK");
            return;
        }

        double currentTotal = _currentCriteria.Sum(c => c.Percentage);
        if (currentTotal + perc > 100)
        {
            await DisplayAlert("Límite excedido", $"No se puede agregar {perc}%. La suma de criterios sobrepasaría el 100% (Actual: {currentTotal}%).", "OK");
            return;
        }

        var criterion = new UnitEvaluationCriterion
        {
            UnitId = _selectedUnit.Id,
            Name = name,
            Percentage = perc
        };

        bool ok = await _databaseService.AddUnitCriterionAsync(criterion);
        if (ok)
        {
            TxtCriterionName.Text = string.Empty;
            TxtCriterionPercentage.Text = string.Empty;
            await RefreshCriteriaAsync();
        }
    }

    private async void OnDeleteCriterionClicked(object sender, EventArgs e)
    {
        var btn = sender as Button;
        var criterion = btn?.CommandParameter as UnitEvaluationCriterion;

        if (criterion == null) return;

        bool ok = await _databaseService.DeleteUnitCriterionAsync(criterion.Id);
        if (ok)
        {
            await RefreshCriteriaAsync();
        }
    }
}