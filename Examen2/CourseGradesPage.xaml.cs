using Examen2.Data;
using Examen2.ViewModels;

namespace Examen2;

public class UnitGradeDisplayModel
{
    public string UnitTitle { get; set; }
    public string StatusText { get; set; }
    public Color StatusColor { get; set; }
    public string FinalGradeText { get; set; }
    public string CriteriaBreakdown { get; set; }
}

public partial class CourseGradesPage : ContentPage
{
    private readonly Course _course;
    private readonly DatabaseService _databaseService;
    private readonly MainViewModel _mainViewModel;
    private readonly bool _isTeacher;

    public CourseGradesPage(Course course, DatabaseService databaseService, MainViewModel mainViewModel, bool isTeacher = false)
    {
        InitializeComponent();
        _course = course;
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;
        _isTeacher = isTeacher;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadUnitGradesAsync();
    }

    private async Task LoadUnitGradesAsync()
    {
        if (_mainViewModel?.CurrentUser == null) return;

        var units = await _databaseService.GetCourseUnitsAsync(_course.Id);
        var allItems = await _databaseService.GetCourseContentItemsAsync(_course.Id);
        var displayList = new List<UnitGradeDisplayModel>();

        if (_isTeacher)
        {
            // VISTA PROFESOR: Resumen de todos los alumnos inscritos por cada unidad
            var students = await _databaseService.GetCourseStudentsAsync(_course.Id);

            foreach (var unit in units)
            {
                bool isFinished = DateTime.Now > unit.EndDate;
                var criteria = await _databaseService.GetUnitCriteriaAsync(unit.Id);
                var unitTasks = allItems.Where(i => i.ItemType == "Tarea" &&
                    (i.UnitId == unit.Id || (i.CreatedAt.Date >= unit.StartDate.Date && i.CreatedAt.Date <= unit.EndDate.Date))).ToList();

                var studentLines = new List<string>();
                double classSum = 0;

                foreach (var student in students)
                {
                    double studentTotal = 0;

                    foreach (var crit in criteria)
                    {
                        var matchingTasks = unitTasks.Where(t =>
                            t.CriterionId == crit.Id ||
                            (!t.CriterionId.HasValue && t.Title.Contains(crit.Name, StringComparison.OrdinalIgnoreCase))
                        ).ToList();

                        double avgScore = 0;
                        if (matchingTasks.Count > 0)
                        {
                            var scores = new List<double>();
                            foreach (var task in matchingTasks)
                            {
                                var sub = await _databaseService.GetTaskSubmissionAsync(task.Id, student.Id);
                                scores.Add(sub?.Grade ?? 0);
                            }
                            avgScore = scores.Average();
                        }

                        studentTotal += (avgScore * crit.Percentage) / 100.0;
                    }

                    classSum += studentTotal;
                    string studentName = !string.IsNullOrWhiteSpace(student.Username) ? student.Username : student.Email;
                    studentLines.Add($"👤 {studentName}: {studentTotal:F1} / 100");
                }

                double classAvg = students.Count > 0 ? classSum / students.Count : 0;

                displayList.Add(new UnitGradeDisplayModel
                {
                    UnitTitle = unit.Title,
                    StatusText = isFinished
                        ? $"Unidad cerrada ({unit.EndDate:dd/MM/yyyy})"
                        : $"En curso • Cierra el {unit.EndDate:dd/MM/yyyy}",
                    StatusColor = isFinished ? Color.FromArgb("#5EEAD4") : Color.FromArgb("#F59E0B"),
                    FinalGradeText = $"Prom: {classAvg:F1}",
                    CriteriaBreakdown = studentLines.Count > 0
                        ? string.Join("\n", studentLines)
                        : "Aún no hay alumnos inscritos en este curso."
                });
            }
        }
        else
        {
            // VISTA ALUMNO: Desglose individual por criterio de evaluación
            foreach (var unit in units)
            {
                bool isFinished = DateTime.Now > unit.EndDate;
                var criteria = await _databaseService.GetUnitCriteriaAsync(unit.Id);
                var unitTasks = allItems.Where(i => i.ItemType == "Tarea" &&
                    (i.UnitId == unit.Id || (i.CreatedAt.Date >= unit.StartDate.Date && i.CreatedAt.Date <= unit.EndDate.Date))).ToList();

                double totalWeightedScore = 0;
                var breakdownLines = new List<string>();

                foreach (var crit in criteria)
                {
                    var matchingTasks = unitTasks.Where(t =>
                        t.CriterionId == crit.Id ||
                        (!t.CriterionId.HasValue && t.Title.Contains(crit.Name, StringComparison.OrdinalIgnoreCase))
                    ).ToList();

                    double avgScore = 0;
                    if (matchingTasks.Count > 0)
                    {
                        var scores = new List<double>();
                        foreach (var task in matchingTasks)
                        {
                            var sub = await _databaseService.GetTaskSubmissionAsync(task.Id, _mainViewModel.CurrentUser.Id);
                            scores.Add(sub?.Grade ?? 0);
                        }
                        avgScore = scores.Average();
                    }

                    double weightedPoints = (avgScore * crit.Percentage) / 100.0;
                    totalWeightedScore += weightedPoints;
                    breakdownLines.Add($"• {crit.Name} ({crit.Percentage}%): {avgScore:F1}/100 → +{weightedPoints:F1} pts");
                }

                displayList.Add(new UnitGradeDisplayModel
                {
                    UnitTitle = unit.Title,
                    StatusText = isFinished
                        ? $"Unidad finalizada ({unit.EndDate:dd/MM/yyyy})"
                        : $"En curso • Cierra el {unit.EndDate:dd/MM/yyyy}",
                    StatusColor = isFinished ? Color.FromArgb("#5EEAD4") : Color.FromArgb("#F59E0B"),
                    FinalGradeText = isFinished ? $"{totalWeightedScore:F1} / 100" : $"{totalWeightedScore:F1} (Parcial)",
                    CriteriaBreakdown = breakdownLines.Count > 0
                        ? string.Join("\n", breakdownLines)
                        : "Sin criterios de evaluación configurados en esta unidad."
                });
            }
        }

        CvUnitGrades.ItemsSource = displayList;
    }
}