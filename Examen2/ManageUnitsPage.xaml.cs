using Examen2.Data;

namespace Examen2;

public partial class ManageUnitsPage : ContentPage
{
    private readonly int _courseId;
    private readonly DatabaseService _databaseService;
    private CourseUnit _selectedUnit;
    private List<CourseUnit> _units = new();
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
        _units = await _databaseService.GetCourseUnitsAsync(_courseId) ?? new List<CourseUnit>();
        CvUnits.ItemsSource = null;
        CvUnits.ItemsSource = _units;
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

        var existingUnits = await _databaseService.GetCourseUnitsAsync(_courseId) ?? new List<CourseUnit>();
        var overlappingUnit = existingUnits.FirstOrDefault(u => startDate.Date <= u.EndDate.Date && endDate.Date >= u.StartDate.Date);

        if (overlappingUnit != null)
        {
            await DisplayAlert("Rango Inválido",
                $"Las fechas ingresadas se enciman con la unidad '{overlappingUnit.Title}' ({overlappingUnit.StartDate:dd/MM/yyyy} al {overlappingUnit.EndDate:dd/MM/yyyy}). Ajusta las fechas.",
                "OK");
            return;
        }

        if (_selectedUnit != null)
        {
            var prevCriteria = await _databaseService.GetUnitCriteriaAsync(_selectedUnit.Id) ?? new List<UnitEvaluationCriterion>();
            double prevTotal = prevCriteria.Sum(c => c.Percentage);
            if (Math.Abs(prevTotal - 100.0) > 0.01)
            {
                await DisplayAlert("Criterios incompletos",
                    $"Antes de crear otra unidad, los criterios de '{_selectedUnit.Title}' deben sumar 100% (actualmente suman {prevTotal:0.##}%).",
                    "OK");
                return;
            }
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

            var createdUnit = _units.LastOrDefault(u => u.Title == title && u.StartDate.Date == startDate.Date)
                              ?? _units.LastOrDefault();

            if (createdUnit != null)
            {
                _selectedUnit = createdUnit;
                LblSelectedUnitTitle.Text = $"Criterios de: {_selectedUnit.Title} (Obligatorio 100%)";
                BorderCriteriaPanel.IsVisible = true;
                await RefreshCriteriaAsync();
                await DisplayAlert("Configura criterios",
                    $"Unidad '{_selectedUnit.Title}' creada. Agrega sus criterios de evaluación hasta completar el 100%.",
                    "OK");
            }
        }
        else
        {
            await DisplayAlert("Error", "No se pudo guardar la unidad.", "OK");
        }
    }

    private async void OnUnitSelectedTapped(object sender, TappedEventArgs e)
    {
        var border = sender as Border;
        var tappedUnit = border?.BindingContext as CourseUnit;

        if (tappedUnit == null) return;

        if (_selectedUnit != null && _selectedUnit.Id != tappedUnit.Id)
        {
            var currentCriteria = await _databaseService.GetUnitCriteriaAsync(_selectedUnit.Id) ?? new List<UnitEvaluationCriterion>();
            double currentTotal = currentCriteria.Sum(c => c.Percentage);
            if (Math.Abs(currentTotal - 100.0) > 0.01)
            {
                await DisplayAlert("Criterios incompletos",
                    $"Completa el 100% de criterios en '{_selectedUnit.Title}' ({currentTotal:0.##}% actual) antes de cambiar de unidad.",
                    "OK");
                return;
            }
        }

        _selectedUnit = tappedUnit;
        LblSelectedUnitTitle.Text = $"Criterios de: {_selectedUnit.Title}";
        BorderCriteriaPanel.IsVisible = true;
        await RefreshCriteriaAsync();
    }

    private async Task RefreshCriteriaAsync()
    {
        if (_selectedUnit == null) return;

        _currentCriteria = await _databaseService.GetUnitCriteriaAsync(_selectedUnit.Id) ?? new List<UnitEvaluationCriterion>();
        CvCriteria.ItemsSource = null;
        CvCriteria.ItemsSource = _currentCriteria;

        double currentTotal = _currentCriteria.Sum(c => c.Percentage);
        LblTotalPercentage.Text = $"Total Criterios: {currentTotal:0.##}% / 100%";
        LblTotalPercentage.TextColor = Math.Abs(currentTotal - 100.0) < 0.01 ? Color.FromArgb("#5EEAD4") : Color.FromArgb("#F87171");
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
        if (currentTotal + perc > 100.0001)
        {
            await DisplayAlert("Límite excedido", $"No se puede agregar {perc}%. La suma de criterios sobrepasaría el 100% (Actual: {currentTotal:0.##}%).", "OK");
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

    private async void OnFinishSetupClicked(object sender, EventArgs e)
    {
        var units = await _databaseService.GetCourseUnitsAsync(_courseId) ?? new List<CourseUnit>();

        if (units.Count < 1)
        {
            await DisplayAlert("Unidad obligatoria",
                "Debes crear al menos una unidad y completar sus criterios al 100% para terminar la configuración.",
                "OK");
            return;
        }

        foreach (var unit in units)
        {
            var criteria = await _databaseService.GetUnitCriteriaAsync(unit.Id) ?? new List<UnitEvaluationCriterion>();
            double totalPercentage = criteria.Sum(c => c.Percentage);

            if (criteria.Count == 0 || Math.Abs(totalPercentage - 100.0) > 0.01)
            {
                _selectedUnit = unit;
                LblSelectedUnitTitle.Text = $"Criterios de: {_selectedUnit.Title} (Faltan criterios hasta 100%)";
                BorderCriteriaPanel.IsVisible = true;
                await RefreshCriteriaAsync();

                await DisplayAlert("Criterios al 100% obligatorios",
                    $"La unidad '{unit.Title}' tiene {totalPercentage:0.##}% de criterios. Debes completar el 100% antes de terminar la configuración.",
                    "OK");
                return;
            }
        }

        int userId = Preferences.Default.Get("CurrentUserId", 0);
        var courses = await _databaseService.GetCoursesAsync(userId);
        var course = courses?.FirstOrDefault(c => c.Id == _courseId);

        if (course != null)
        {
            await Navigation.PushAsync(new CourseDetailPage(course, _databaseService));
        }
        else
        {
            await DisplayAlert("Error", "No se pudo encontrar la clase creada.", "OK");
        }
    }
}