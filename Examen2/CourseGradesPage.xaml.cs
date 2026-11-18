using System.ComponentModel;
using System.Runtime.CompilerServices;
using Examen2.Data;
using Examen2.ViewModels;

namespace Examen2;

public class CriterionBreakdownModel
{
    public string CriterionSummary { get; set; }
    public string EarnedPointsText { get; set; }
}

public class StudentTaskDetailModel
{
    public string TaskTitle { get; set; }
    public string CriterionTag { get; set; }
    public string StatusText { get; set; }
    public Color StatusTextColor { get; set; }
    public Color StatusBadgeBg { get; set; }
}

public class StudentUnitGradeModel : INotifyPropertyChanged
{
    public string StudentName { get; set; }
    public string FinalScoreText { get; set; }
    public List<CriterionBreakdownModel> CriteriaBreakdown { get; set; } = new();
    public List<StudentTaskDetailModel> Tasks { get; set; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToggleButtonText));
        }
    }

    public string ToggleButtonText => IsExpanded ? "Ocultar ▲" : "Ver más ▼";

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class UnitGradeDisplayModel
{
    public string UnitTitle { get; set; }
    public string StatusText { get; set; }
    public Color StatusColor { get; set; }
    public string HeaderBadgeText { get; set; }
    public List<StudentUnitGradeModel> Students { get; set; } = new();
}

public partial class CourseGradesPage : ContentPage
{
    private readonly Course _course;
    private readonly DatabaseService _databaseService;
    private readonly MainViewModel _mainViewModel;
    private bool _isTeacher;

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

    private void OnToggleStudentDetailsClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.BindingContext is StudentUnitGradeModel studentModel)
        {
            studentModel.IsExpanded = !studentModel.IsExpanded;
        }
    }

    private async Task LoadUnitGradesAsync()
    {
        if (_mainViewModel?.CurrentUser == null) return;

        string role = await _databaseService.GetUserRoleInCourseAsync(_course.Id, _mainViewModel.CurrentUser.Id);
        _isTeacher = string.Equals(role, "Teacher", StringComparison.OrdinalIgnoreCase);

        var units = await _databaseService.GetCourseUnitsAsync(_course.Id);
        var allItems = await _databaseService.GetCourseContentItemsAsync(_course.Id);

        List<User> targetStudents;
        if (_isTeacher)
        {
            // Profesor: ve a todos los alumnos inscritos (excluyéndose a sí mismo si apareciera)
            var allStudents = await _databaseService.GetCourseStudentsAsync(_course.Id);
            targetStudents = allStudents.Where(s => s.Id != _mainViewModel.CurrentUser.Id).ToList();
        }
        else
        {
            // Alumno: única y exclusivamente su propio registro
            targetStudents = new List<User> { _mainViewModel.CurrentUser };
        }

        var displayList = new List<UnitGradeDisplayModel>();

        foreach (var unit in units)
        {
            bool isFinished = DateTime.Now > unit.EndDate;
            var criteria = await _databaseService.GetUnitCriteriaAsync(unit.Id);
            var unitTasks = allItems.Where(i => i.ItemType == "Tarea" &&
                (i.UnitId == unit.Id || (i.CreatedAt.Date >= unit.StartDate.Date && i.CreatedAt.Date <= unit.EndDate.Date))).ToList();

            var studentModels = new List<StudentUnitGradeModel>();
            double classSum = 0;

            foreach (var student in targetStudents)
            {
                double studentTotal = 0;
                var breakdownList = new List<CriterionBreakdownModel>();
                var taskDetailList = new List<StudentTaskDetailModel>();

                // Precargar entregas del alumno para todas las tareas de esta unidad
                var studentSubmissions = new Dictionary<int, TaskSubmission>();
                foreach (var task in unitTasks)
                {
                    var sub = await _databaseService.GetTaskSubmissionAsync(task.Id, student.Id);
                    studentSubmissions[task.Id] = sub;
                }

                // 1. Calcular el desglose por cada criterio de la unidad
                foreach (var crit in criteria)
                {
                    var matchingTasks = unitTasks.Where(t =>
                        t.CriterionId == crit.Id ||
                        (!t.CriterionId.HasValue && t.Title.Contains(crit.Name, StringComparison.OrdinalIgnoreCase))
                    ).ToList();

                    double avgScore = 0;
                    if (matchingTasks.Count > 0)
                    {
                        avgScore = matchingTasks.Select(t => studentSubmissions[t.Id]?.Grade ?? 0).Average();
                    }

                    double weightedPoints = (avgScore * crit.Percentage) / 100.0;
                    studentTotal += weightedPoints;

                    breakdownList.Add(new CriterionBreakdownModel
                    {
                        CriterionSummary = $"• {crit.Name} ({crit.Percentage}%): Prom. {avgScore:F1}/100 ({matchingTasks.Count} tarea(s))",
                        EarnedPointsText = $"+{weightedPoints:F1} pts"
                    });
                }

                if (criteria.Count == 0)
                {
                    breakdownList.Add(new CriterionBreakdownModel
                    {
                        CriterionSummary = "No hay criterios de evaluación configurados para esta unidad.",
                        EarnedPointsText = "0.0 pts"
                    });
                }

                // 2. Construir la lista individual de tareas de la unidad y su criterio asociado
                foreach (var task in unitTasks)
                {
                    var matchedCrit = criteria.FirstOrDefault(c =>
                        task.CriterionId == c.Id ||
                        (!task.CriterionId.HasValue && task.Title.Contains(c.Name, StringComparison.OrdinalIgnoreCase)));

                    string critLabel = matchedCrit != null
                        ? $"Criterio: {matchedCrit.Name} ({matchedCrit.Percentage}%)"
                        : "Sin criterio asignado (no suma al promedio)";

                    var sub = studentSubmissions[task.Id];
                    bool hasSubmitted = sub != null && !string.IsNullOrEmpty(sub.FilePath);
                    bool isOverdue = task.DueDate.HasValue && DateTime.Now > task.DueDate.Value;

                    string statusText;
                    Color textColor;
                    Color badgeBg;

                    if (hasSubmitted && sub.Grade.HasValue)
                    {
                        statusText = $"Calificada: {sub.Grade.Value:F1} / 100";
                        textColor = Color.FromArgb("#5EEAD4");
                        badgeBg = Color.FromArgb("#132A2B");
                    }
                    else if (hasSubmitted)
                    {
                        statusText = "Entregada (Sin calificar)";
                        textColor = Color.FromArgb("#60A5FA");
                        badgeBg = Color.FromArgb("#1E3A8A");
                    }
                    else if (isOverdue)
                    {
                        statusText = "Vencida • Sin entregar (0)";
                        textColor = Color.FromArgb("#F87171");
                        badgeBg = Color.FromArgb("#450A0A");
                    }
                    else
                    {
                        statusText = "Pendiente de entrega";
                        textColor = Color.FromArgb("#FBBF24");
                        badgeBg = Color.FromArgb("#451A03");
                    }

                    taskDetailList.Add(new StudentTaskDetailModel
                    {
                        TaskTitle = task.Title,
                        CriterionTag = critLabel,
                        StatusText = statusText,
                        StatusTextColor = textColor,
                        StatusBadgeBg = badgeBg
                    });
                }

                if (unitTasks.Count == 0)
                {
                    taskDetailList.Add(new StudentTaskDetailModel
                    {
                        TaskTitle = "Sin tareas registradas",
                        CriterionTag = "No se asignaron actividades en esta unidad",
                        StatusText = "—",
                        StatusTextColor = Color.FromArgb("#94A3B8"),
                        StatusBadgeBg = Color.FromArgb("#1E293B")
                    });
                }

                classSum += studentTotal;
                string studentName = !string.IsNullOrWhiteSpace(student.Username) ? student.Username : student.Email;

                studentModels.Add(new StudentUnitGradeModel
                {
                    StudentName = studentName,
                    FinalScoreText = $"{studentTotal:F1} / 100",
                    CriteriaBreakdown = breakdownList,
                    Tasks = taskDetailList,
                    IsExpanded = !_isTeacher // Si es alumno, aparece desplegado por defecto
                });
            }

            double classAvg = targetStudents.Count > 0 ? classSum / targetStudents.Count : 0;

            displayList.Add(new UnitGradeDisplayModel
            {
                UnitTitle = unit.Title,
                StatusText = isFinished
                    ? $"Unidad cerrada ({unit.EndDate:dd/MM/yyyy})"
                    : $"En curso • Cierra el {unit.EndDate:dd/MM/yyyy}",
                StatusColor = isFinished ? Color.FromArgb("#5EEAD4") : Color.FromArgb("#F59E0B"),
                HeaderBadgeText = _isTeacher ? $"Prom. Grupo: {classAvg:F1}" : $"Total: {classAvg:F1} / 100",
                Students = studentModels
            });
        }

        CvUnitGrades.ItemsSource = displayList;
    }
}