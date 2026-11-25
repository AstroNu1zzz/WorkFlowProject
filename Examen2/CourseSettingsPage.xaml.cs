using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Examen2.Data;
using Examen2.ViewModels;

namespace Examen2;

public class CourseMemberItemViewModel
{
    public User User { get; set; }
    public string Username => User?.Username ?? "Usuario";
    public string Email => User?.Email ?? "";
    public string RoleLabel { get; set; }
    public string RoleIcon { get; set; }
    public bool CanRemove { get; set; }
}

public partial class CourseSettingsPage : ContentPage, INotifyPropertyChanged
{
    private Course _course;
    public Course Course
    {
        get => _course;
        set
        {
            _course = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<CourseMemberItemViewModel> CourseMembers { get; set; } = new();

    private DatabaseService _databaseService;
    private MainViewModel _mainViewModel;

    public CourseSettingsPage(Course course, DatabaseService databaseService = null, MainViewModel mainViewModel = null)
    {
        InitializeComponent();
        Course = course;
        _databaseService = databaseService;
        _mainViewModel = mainViewModel;
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        EnsureServices();
        await LoadMembersAsync();
    }

    private void EnsureServices()
    {
        _databaseService ??= Handler?.MauiContext?.Services.GetService<DatabaseService>()
                            ?? Application.Current?.Handler?.MauiContext?.Services.GetService<DatabaseService>();

        _mainViewModel ??= Handler?.MauiContext?.Services.GetService<MainViewModel>()
                          ?? Application.Current?.Handler?.MauiContext?.Services.GetService<MainViewModel>();
    }

    private async Task LoadMembersAsync()
    {
        EnsureServices();
        if (_databaseService == null || Course == null) return;

        int currentUserId = _mainViewModel?.CurrentUser?.Id ?? 0;

        var teachers = await _databaseService.GetCourseTeachersAsync(Course.Id);
        var students = await _databaseService.GetCourseStudentsAsync(Course.Id);

        CourseMembers.Clear();

        foreach (var teacher in teachers)
        {
            bool isPrimary = teacher.Id == Course.PrimaryTeacherId;
            bool isCurrentUser = teacher.Id == currentUserId;

            CourseMembers.Add(new CourseMemberItemViewModel
            {
                User = teacher,
                RoleLabel = isPrimary ? "Profesor Titular" : "Profesor Colaborador",
                RoleIcon = "👨‍🏫",
                CanRemove = !isPrimary && !isCurrentUser
            });
        }

        foreach (var student in students)
        {
            CourseMembers.Add(new CourseMemberItemViewModel
            {
                User = student,
                RoleLabel = "Alumno",
                RoleIcon = "🎓",
                CanRemove = true
            });
        }
    }

    private async void OnBackButtonClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnChangeBannerClicked(object sender, EventArgs e)
    {
        EnsureServices();
        if (_databaseService == null || Course == null) return;

        try
        {
            var result = await MediaPicker.Default.PickPhotoAsync();
            if (result == null) return;

            using var stream = await result.OpenReadAsync();
            var (publicUrl, errorMessage) = await _databaseService.UploadCourseBannerAsync(stream, result.FileName);

            if (!string.IsNullOrEmpty(errorMessage) || string.IsNullOrEmpty(publicUrl))
            {
                await DisplayAlert("Error", $"No se pudo subir la imagen: {errorMessage}", "OK");
                return;
            }

            string previousBanner = Course.BannerImagePath;
            Course.BannerImagePath = publicUrl;

            bool updated = await _databaseService.UpdateCourseAsync(Course);
            if (updated)
            {
                ImgBannerPreview.Source = publicUrl;
                OnPropertyChanged(nameof(Course));

                if (_mainViewModel != null)
                {
                    await _mainViewModel.RefreshCoursesAsync();
                }

                await DisplayAlert("Éxito", "La imagen de la clase se actualizó correctamente.", "OK");
            }
            else
            {
                Course.BannerImagePath = previousBanner;
                await DisplayAlert("Error", "No se pudo guardar la nueva portada.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo seleccionar la imagen: {ex.Message}", "OK");
        }
    }

    private async void OnSaveNameClicked(object sender, EventArgs e)
    {
        EnsureServices();
        if (_databaseService == null || Course == null) return;

        string newName = EntryCourseName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            await DisplayAlert("Atención", "El nombre de la clase no puede estar vacío.", "OK");
            return;
        }

        string previousName = Course.CourseName;
        Course.CourseName = newName;

        bool updated = await _databaseService.UpdateCourseAsync(Course);
        if (updated)
        {
            OnPropertyChanged(nameof(Course));

            if (_mainViewModel != null)
            {
                await _mainViewModel.RefreshCoursesAsync();
            }

            await DisplayAlert("Éxito", "El nombre de la clase se guardó correctamente.", "OK");
        }
        else
        {
            Course.CourseName = previousName;
            await DisplayAlert("Error", "No se pudo actualizar el nombre de la clase.", "OK");
        }
    }

    private async void OnRemoveMemberClicked(object sender, EventArgs e)
    {
        EnsureServices();
        if (_databaseService == null || Course == null) return;

        var button = sender as Button;
        var member = button?.BindingContext as CourseMemberItemViewModel;
        if (member?.User == null) return;

        bool confirm = await DisplayAlert(
            "Confirmar expulsión",
            $"¿Estás seguro de que deseas sacar a '{member.Username}' de la clase '{Course.CourseName}'? Perderá el acceso a este curso.",
            "Sí, sacar",
            "Cancelar");

        if (!confirm) return;

        bool removed = await _databaseService.RemoveMemberFromCourseAsync(Course.Id, member.User.Id);
        if (removed)
        {
            CourseMembers.Remove(member);
            await DisplayAlert("Participante retirado", $"'{member.Username}' ha sido eliminado de la clase.", "OK");
        }
        else
        {
            await DisplayAlert("Error", "No se pudo sacar a la persona de la clase. Inténtalo de nuevo.", "OK");
        }
    }

    public new event PropertyChangedEventHandler PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}