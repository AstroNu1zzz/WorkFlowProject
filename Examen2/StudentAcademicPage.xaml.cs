using Examen2.Data;
using Examen2.ViewModels;
using Microsoft.Maui.Graphics;

namespace Examen2;

public partial class StudentAcademicPage : ContentPage
{
    private readonly DatabaseService _databaseService;
    private readonly MainViewModel _mainViewModel;
    private readonly int _studentId;

    private List<SemesterRecord> _semesters = new();
    private List<SubjectGradeBreakdown> _subjectGrades = new();
    private TrendChartDrawable _chartDrawable = new();

    public StudentAcademicPage(DatabaseService databaseService, MainViewModel mainViewModel, int studentId = 0)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;
        _studentId = studentId > 0 ? studentId : (_mainViewModel?.CurrentUser?.Id ?? Preferences.Default.Get("CurrentUserId", 0));

        TrendChartView.Drawable = _chartDrawable;
        _ = LoadAcademicDataAsync();
    }

    private async Task LoadAcademicDataAsync()
    {
        var courses = _studentId > 0 && _databaseService != null
            ? await _databaseService.GetCoursesAsync(_studentId)
            : new List<Course>();

        _semesters = new List<SemesterRecord>();
        _subjectGrades = new List<SubjectGradeBreakdown>();

        if (courses != null && courses.Any())
        {
            var currentSemesterSubjects = new List<KardexSubjectItem>();

            foreach (var course in courses)
            {
                var units = await _databaseService.GetCourseUnitsAsync(course.Id);
                double unit1Avg = 0;
                double unit2Avg = 0;
                double unit3Avg = 0;
                int gradedUnitsCount = 0;
                double sumUnits = 0;

                if (units.Count > 0)
                {
                    unit1Avg = await _databaseService.CalculateStudentUnitGradeAsync(units[0].Id, _studentId);
                    if (unit1Avg > 0) { sumUnits += unit1Avg; gradedUnitsCount++; }
                }
                if (units.Count > 1)
                {
                    unit2Avg = await _databaseService.CalculateStudentUnitGradeAsync(units[1].Id, _studentId);
                    if (unit2Avg > 0) { sumUnits += unit2Avg; gradedUnitsCount++; }
                }
                if (units.Count > 2)
                {
                    unit3Avg = await _databaseService.CalculateStudentUnitGradeAsync(units[2].Id, _studentId);
                    if (unit3Avg > 0) { sumUnits += unit3Avg; gradedUnitsCount++; }
                }

                double finalGrade = gradedUnitsCount > 0 ? Math.Round(sumUnits / gradedUnitsCount, 1) : 0;

                currentSemesterSubjects.Add(new KardexSubjectItem
                {
                    SubjectName = course.CourseName,
                    FinalGrade = finalGrade
                });

                _subjectGrades.Add(new SubjectGradeBreakdown
                {
                    PeriodTitle = "Periodo Actual",
                    SubjectName = course.CourseName,
                    Eval1 = units.Count > 0 && unit1Avg > 0 ? unit1Avg.ToString("0.#") : "-",
                    Eval2 = units.Count > 1 && unit2Avg > 0 ? unit2Avg.ToString("0.#") : "-",
                    Eval3 = units.Count > 2 && unit3Avg > 0 ? unit3Avg.ToString("0.#") : "-",
                    Extra = "-",
                    Final = finalGrade > 0 ? finalGrade.ToString("0.#") : "-"
                });
            }

            _semesters.Add(new SemesterRecord
            {
                SemesterTitle = "Periodo Actual",
                IsExpanded = false,
                Subjects = currentSemesterSubjects
            });
        }

        double allGradesSum = _semesters.SelectMany(s => s.Subjects).Where(m => m.FinalGrade > 0).Select(m => m.FinalGrade).DefaultIfEmpty(0).Average();
        double generalAverage = Math.Round(allGradesSum, 2);

        LblGeneralAverage.Text = generalAverage.ToString("0.00");
        LblPerfGeneralAvg.Text = generalAverage.ToString("0.00");

        double currentTermAvg = _semesters.LastOrDefault()?.Subjects.Where(m => m.FinalGrade > 0).Select(m => m.FinalGrade).DefaultIfEmpty(generalAverage).Average() ?? generalAverage;
        LblPerfCurrentTermAvg.Text = Math.Round(currentTermAvg, 2).ToString("0.00");
        LblPerfTermName.Text = _semesters.LastOrDefault()?.SemesterTitle ?? "Periodo actual";

        if (_semesters.Count >= 2)
        {
            var prev = _semesters[_semesters.Count - 2].Subjects.Where(m => m.FinalGrade > 0).Select(m => m.FinalGrade).DefaultIfEmpty(0).Average();
            double diff = Math.Round(currentTermAvg - prev, 2);
            LblPerfVsPrevious.Text = diff >= 0 ? $"vs. anterior: +{diff:0.00}" : $"vs. anterior: {diff:0.00}";
        }
        else
        {
            LblPerfVsPrevious.Text = "vs. anterior: --";
        }

        // Tareas entregadas vs asignadas
        int totalTasks = 0;
        int submittedTasks = 0;
        if (_studentId > 0 && _databaseService != null && courses != null)
        {
            foreach (var course in courses)
            {
                var items = await _databaseService.GetCourseContentItemsAsync(course.Id);
                var tasks = items.Where(i => i.ItemType == "Tarea").ToList();
                totalTasks += tasks.Count;

                foreach (var task in tasks)
                {
                    var sub = await _databaseService.GetTaskSubmissionAsync(task.Id, _studentId);
                    if (sub != null) submittedTasks++;
                }
            }
        }

        int compliancePercent = totalTasks > 0 ? (int)Math.Round((double)submittedTasks / totalTasks * 100) : 100;
        LblTasksRatio.Text = $"{submittedTasks} / {totalTasks}";
        LblTasksCompliance.Text = $"{compliancePercent}% cumplimiento";

        int attendancePercent = 95;
        LblAttendancePercent.Text = $"{attendancePercent}%";
        LblAttendanceDetail.Text = "Registro del periodo";

        // Semáforo académico (Verde, Amarillo o Rojo)
        Color trafficColor;
        string trafficStatus;
        string trafficDesc;

        if (generalAverage >= 8.0 && compliancePercent >= 75 && attendancePercent >= 80)
        {
            trafficColor = Color.FromArgb("#86EFAC");
            trafficStatus = "Verde";
            trafficDesc = "Rendimiento sólido y constante en el periodo.";
        }
        else if (generalAverage >= 6.0 && compliancePercent >= 50)
        {
            trafficColor = Color.FromArgb("#FBBF24");
            trafficStatus = "Amarillo";
            trafficDesc = "Atención recomendada en calificaciones o entregas.";
        }
        else
        {
            trafficColor = Color.FromArgb("#EF4444");
            trafficStatus = "Rojo";
            trafficDesc = "Riesgo académico: requiere regularización inmediata.";
        }

        DotAcademicTrafficLight.Color = trafficColor;
        LblAcademicTrafficLight.Text = $"Estado: {trafficStatus}";
        BorderTrafficBadge.BackgroundColor = trafficColor;
        LblTrafficTitle.Text = $"Semáforo Académico: {trafficStatus}";
        LblTrafficDescription.Text = trafficDesc;

        // Tendencia y comparativa por semestre
        var semesterAverages = _semesters.Select(s =>
        {
            var graded = s.Subjects.Where(sub => sub.FinalGrade > 0).Select(sub => sub.FinalGrade).ToList();
            double avg = graded.Any() ? Math.Round(graded.Average(), 2) : 0;
            return (s.SemesterTitle, avg);
        }).ToList();

        _chartDrawable.Points = semesterAverages.Select(x => x.avg).ToList();
        _chartDrawable.Labels = semesterAverages.Select(x => x.SemesterTitle).ToList();
        TrendChartView.Invalidate();

        ContainerSemesterComparison.Children.Clear();
        foreach (var item in semesterAverages)
        {
            var rowGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 12
            };

            var lblTitle = new Label
            {
                Text = item.SemesterTitle,
                FontSize = 13,
                TextColor = Color.FromArgb("#94A3B8"),
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(lblTitle, 0);

            var lblAvg = new Label
            {
                Text = item.avg.ToString("0.00"),
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#5EEAD4"),
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(lblAvg, 1);

            rowGrid.Children.Add(lblTitle);
            rowGrid.Children.Add(lblAvg);
            ContainerSemesterComparison.Children.Add(rowGrid);
        }

        var periodOptions = new List<string> { "Todos los periodos" };
        periodOptions.AddRange(_semesters.Select(s => s.SemesterTitle).Distinct());
        PickerGradePeriod.ItemsSource = periodOptions;
        PickerGradePeriod.SelectedIndex = 0;
        ApplyGradesFilter("Todos los periodos");

        RenderCurrentPeriodBreakdown();
    }

    private void OnGradePeriodChanged(object sender, EventArgs e)
    {
        if (PickerGradePeriod.SelectedItem is string selectedPeriod)
        {
            ApplyGradesFilter(selectedPeriod);
        }
    }

    private void ApplyGradesFilter(string selectedPeriod)
    {
        var filtered = string.IsNullOrWhiteSpace(selectedPeriod) || selectedPeriod == "Todos los periodos"
            ? _subjectGrades
            : _subjectGrades.Where(g => g.PeriodTitle == selectedPeriod).ToList();

        CvSubjectGrades.ItemsSource = filtered;
        LblGradesSubjectCount.Text = filtered.Count.ToString();

        var numericFinals = filtered
            .Where(g => double.TryParse(g.Final, out _))
            .Select(g => double.Parse(g.Final))
            .ToList();

        double periodAvg = numericFinals.Any() ? Math.Round(numericFinals.Average(), 2) : 0;
        LblGradesPeriodAvg.Text = periodAvg.ToString("0.00");
    }

    private void RenderCurrentPeriodBreakdown()
    {
        ContainerSemesters.Children.Clear();

        foreach (var sem in _semesters)
        {
            var semCopy = sem;
            var subjectsStack = new VerticalStackLayout
            {
                Spacing = 8,
                IsVisible = semCopy.IsExpanded,
                Margin = new Thickness(0, 10, 0, 0)
            };

            foreach (var subj in semCopy.Subjects)
            {
                var rowGrid = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    Padding = new Thickness(4, 2)
                };

                var lblName = new Label
                {
                    Text = subj.SubjectName,
                    FontSize = 14,
                    TextColor = Color.FromArgb("#E2E8F0"),
                    VerticalOptions = LayoutOptions.Center
                };
                Grid.SetColumn(lblName, 0);

                var lblGrade = new Label
                {
                    Text = subj.FinalGrade > 0 ? subj.FinalGrade.ToString("0.#") : "-",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = subj.FinalGrade >= 6 ? Color.FromArgb("#5EEAD4") : Color.FromArgb("#EF4444"),
                    VerticalOptions = LayoutOptions.Center
                };
                Grid.SetColumn(lblGrade, 1);

                rowGrid.Children.Add(lblName);
                rowGrid.Children.Add(lblGrade);
                subjectsStack.Children.Add(rowGrid);
            }

            var headerGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };

            var lblSemTitle = new Label
            {
                Text = $"Desglose · {semCopy.SemesterTitle}",
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#F8FAFC"),
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(lblSemTitle, 0);

            var lblArrow = new Label
            {
                Text = semCopy.IsExpanded ? "∧" : "∨",
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#5EEAD4"),
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(lblArrow, 1);

            headerGrid.Children.Add(lblSemTitle);
            headerGrid.Children.Add(lblArrow);

            var cardStack = new VerticalStackLayout { Spacing = 0 };
            cardStack.Children.Add(headerGrid);
            cardStack.Children.Add(subjectsStack);

            var border = new Border
            {
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#2D3748"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                Padding = new Thickness(16),
                BackgroundColor = Color.FromArgb("#1E232B"),
                Content = cardStack
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) =>
            {
                semCopy.IsExpanded = !semCopy.IsExpanded;
                subjectsStack.IsVisible = semCopy.IsExpanded;
                lblArrow.Text = semCopy.IsExpanded ? "∧" : "∨";
            };
            border.GestureRecognizers.Add(tap);

            ContainerSemesters.Children.Add(border);
        }
    }

    private void OnTabPerformanceClicked(object sender, EventArgs e)
    {
        ScrollPerformance.IsVisible = true;
        ScrollGrades.IsVisible = false;

        IndicatorPerformance.Color = Color.FromArgb("#5EEAD4");
        IndicatorGrades.Color = Colors.Transparent;

        BtnTabPerformance.TextColor = Color.FromArgb("#5EEAD4");
        BtnTabGrades.TextColor = Color.FromArgb("#94A3B8");

        TrendChartView.Invalidate();
    }

    private void OnTabGradesClicked(object sender, EventArgs e)
    {
        ScrollPerformance.IsVisible = false;
        ScrollGrades.IsVisible = true;

        IndicatorPerformance.Color = Colors.Transparent;
        IndicatorGrades.Color = Color.FromArgb("#5EEAD4");

        BtnTabPerformance.TextColor = Color.FromArgb("#94A3B8");
        BtnTabGrades.TextColor = Color.FromArgb("#5EEAD4");
    }
}

public class SemesterRecord
{
    public string SemesterTitle { get; set; }
    public bool IsExpanded { get; set; }
    public List<KardexSubjectItem> Subjects { get; set; } = new();
}

public class KardexSubjectItem
{
    public string SubjectName { get; set; }
    public double FinalGrade { get; set; }
}

public class SubjectGradeBreakdown
{
    public string PeriodTitle { get; set; } = "Periodo Actual";
    public string SubjectName { get; set; }
    public string Eval1 { get; set; } = "-";
    public string Eval2 { get; set; } = "-";
    public string Eval3 { get; set; } = "-";
    public string Extra { get; set; } = "-";
    public string Final { get; set; } = "-";
    public string FinalGradeText => Final == "-" ? "Sin nota" : $"Final: {Final}";
}

public class TrendChartDrawable : IDrawable
{
    public List<double> Points { get; set; } = new();
    public List<string> Labels { get; set; } = new();

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.SaveState();

        float leftPad = 24f;
        float rightPad = 24f;
        float topPad = 20f;
        float bottomPad = 28f;

        float width = Math.Max(10f, dirtyRect.Width - leftPad - rightPad);
        float height = Math.Max(10f, dirtyRect.Height - topPad - bottomPad);

        // Línea base
        canvas.StrokeColor = Color.FromArgb("#2D3748");
        canvas.StrokeSize = 1;
        canvas.DrawLine(leftPad, topPad + height, leftPad + width, topPad + height);

        if (Points == null || Points.Count == 0)
        {
            canvas.RestoreState();
            return;
        }

        double maxVal = Math.Max(10.0, Points.Max());
        var coords = new List<PointF>();

        for (int i = 0; i < Points.Count; i++)
        {
            float x = Points.Count == 1
                ? leftPad + width / 2f
                : leftPad + (width * i / (Points.Count - 1f));

            float ratio = (float)(Points[i] / maxVal);
            float y = topPad + height - (height * Math.Clamp(ratio, 0f, 1f));
            coords.Add(new PointF(x, y));
        }

        if (coords.Count > 1)
        {
            var path = new PathF();
            path.MoveTo(coords[0]);
            for (int i = 1; i < coords.Count; i++)
            {
                path.LineTo(coords[i]);
            }

            canvas.StrokeColor = Color.FromArgb("#5EEAD4");
            canvas.StrokeSize = 2.5f;
            canvas.DrawPath(path);
        }

        for (int i = 0; i < coords.Count; i++)
        {
            canvas.FillColor = Color.FromArgb("#86EFAC");
            canvas.FillCircle(coords[i].X, coords[i].Y, 4.5f);
        }

        canvas.RestoreState();
    }
}