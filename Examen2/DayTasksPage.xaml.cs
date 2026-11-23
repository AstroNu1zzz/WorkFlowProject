using Examen2.Data;
using Examen2.ViewModels;

namespace Examen2;

public partial class DayTasksPage : ContentPage
{
    private readonly DateTime _selectedDate;
    private readonly List<ActivityItemViewModel> _dayTasks;
    private readonly DatabaseService _databaseService;
    private readonly MainViewModel _mainViewModel;

    public DayTasksPage(DateTime selectedDate, List<ActivityItemViewModel> dayTasks, DatabaseService databaseService, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _selectedDate = selectedDate;
        _dayTasks = dayTasks;
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;

        LblSelectedDate.Text = $"📅 Entregas del {selectedDate:dd 'de' MMMM 'de' yyyy}";
        CvDayTasks.ItemsSource = _dayTasks;
    }

    private async void OnTaskItemTapped(object sender, TappedEventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is ActivityItemViewModel activity && activity.OriginalItem != null)
        {
            string role = await _databaseService.GetUserRoleInCourseAsync(activity.OriginalItem.CourseId, _mainViewModel.CurrentUser.Id);
            bool isTeacher = string.Equals(role, "Teacher", StringComparison.OrdinalIgnoreCase);

            if (isTeacher)
            {
                await Navigation.PushAsync(new TeacherTaskOverviewPage(activity.OriginalItem, activity.OriginalItem.CourseId, _databaseService, _mainViewModel));
            }
            else
            {
                await Navigation.PushAsync(new ContentDetailPage(activity.OriginalItem, _databaseService, _mainViewModel));
            }
        }
    }
}