using Examen2.Data;
using Examen2.ViewModels;

namespace Examen2;

public partial class UnitGradesPage : ContentPage
{
    private readonly int _courseId;
    private readonly DatabaseService _databaseService;
    private readonly MainViewModel _mainViewModel;
    private bool _isTeacher;

    public UnitGradesPage(int courseId, DatabaseService databaseService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _courseId = courseId;
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;

        _ = InitializeDataAsync();
    }

    private async Task InitializeDataAsync()
    {
        string role = await _databaseService.GetUserRoleInCourseAsync(_courseId, _mainViewModel.CurrentUser.Id);
        _isTeacher = (role == "Teacher");

        var units = await _databaseService.GetCourseUnitsAsync(_courseId);
        PickerUnits.ItemsSource = units;

        if (units.Any())
        {
            PickerUnits.SelectedIndex = 0;
        }
    }

    private async void OnUnitPickerChanged(object sender, EventArgs e)
    {
        var selectedUnit = PickerUnits.SelectedItem as CourseUnit;
        if (selectedUnit == null) return;

        if (_isTeacher)
        {
            BorderStudentSummary.IsVisible = false;

            // Vista Profesor: Promedio de todos los alumnos de la clase
            var students = await _databaseService.GetCourseStudentsAsync(_courseId);
            var gradeList = new List<StudentGradeSummary>();

            foreach (var student in students)
            {
                double grade = await _databaseService.CalculateStudentUnitGradeAsync(selectedUnit.Id, student.Id);
                gradeList.Add(new StudentGradeSummary
                {
                    StudentName = student.Username,
                    StatusText = "Promedio acumulado",
                    FinalGradeText = $"{grade} / 100"
                });
            }

            CvGradesSummary.ItemsSource = gradeList;
        }
        else
        {
            // Vista Alumno: Su propio promedio
            BorderStudentSummary.IsVisible = true;
            double myGrade = await _databaseService.CalculateStudentUnitGradeAsync(selectedUnit.Id, _mainViewModel.CurrentUser.Id);
            LblStudentFinalGrade.Text = $"{myGrade} / 100";

            CvGradesSummary.ItemsSource = new List<StudentGradeSummary>
            {
                new StudentGradeSummary
                {
                    StudentName = _mainViewModel.CurrentUser.Username,
                    StatusText = $"Evaluación de {selectedUnit.Title}",
                    FinalGradeText = $"{myGrade} / 100"
                }
            };
        }
    }
}

public class StudentGradeSummary
{
    public string StudentName { get; set; }
    public string StatusText { get; set; }
    public string FinalGradeText { get; set; }
}