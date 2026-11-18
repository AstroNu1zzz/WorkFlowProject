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
            BorderStudentsSummary.IsVisible = false;

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
            BorderStudentsSummary.IsVisible = true;
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

/*
 * ===========================================================
 * Y al final, después de tantas iteraciones y refactorizaciones,
 * hemos llegado a estas últimas líneas de código.
 * Exactamente igual como acabó mi historia con ella...
 *
 * Todo empezó por allá a mis 16 años, a finales de Julio, en aquella
 * época trabajaba durante las vacaciones como becario en una empresa.Ahí conocí a Linda Villanueva.
 * Al principio pensé que me llevaría mejor con su hermana, pero con el tiempo
 * descubrimos una conexión única: los mismos gustos, una risa hermosa y una gran vibra
 * y congeniábamos bastante bien, pero como saber que no todo seria perfecto...
 *
 * En los días de trabajo nos escapábamos un ratito: veíamos películas, comíamos
 * botana y jugábamos juegos de mesa, una relación de amistad increible.Llegaban las vacaciones y el clásico ritual
* era hacer videollamada por Meet. ¿Qué hacíamos? Me ponía mascarillas con ella,
 * veíamos películas al mismo tiempo y jugábamos juegos de terror y mas.Tenía una personalidad increíble y una risa preciosa.
 *
 * Pero el sistema tenía una dependencia tóxica: ella cargaba con una pareja que
 * la limitaba muchísimo y terminaban a cada rato; siento que eso influyó bastante, pero nunca quedo en mi.
 * Con los meses me di cuenta del patrón crítico: si yo no le escribía, ella
 * jamás me mandaba mensaje.Dejé pasar el tiempo a ver que pasaba pero...Diez meses de silencio absoluto.
 *
 * El tiempo corrió desde aquel inicio hasta después del 11 de octubre, día de la
 * Carrera Quálitas (otra proyectada para más adelante, like para parte 2)
 * Una semana después, tras volver a instalar Instagram pq se ponia personal e hizo lo que yo no puedo darme de baja, subí una nota "buen resultado pero la espinita siempre se queda" o algo asi ns
 *Ella respondió con un emoji de cara preocupante o triste y lo único que dije "Hola, tiene mucho que no sabía
 * de ti"... y desde entonces, fin de la ejecución. Jamás volvimos a hablar.
 *
 * Pero habia algo que yo no sabia, llegaria el Viernes 13 de Noviembre 2026, el tiempo paso y despues de 1 Mes...
 * dos mensajes aparecieron en mi pantalla "Morar forever" "emoji calma/emoji calma"
 * En ese momento me sentia faliz por la moratvibra pero algo en mi no queria dar mucha respuesta, pense...y si talvez quiere voler a hablar, pero algo en mi no sabia
 *Y respondi...
 *

 * Fin del bloque.Así como murieron esas líneas de comunicación,
 * murió toda interacción posible.
 * =====================================================================
 */
