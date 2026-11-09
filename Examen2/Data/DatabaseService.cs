using Supabase;
using Supabase.Postgrest;
using Examen2.ViewModels;

namespace Examen2.Data
{
    public class DatabaseService
    {
        private readonly Supabase.Client _client;
        private bool _isInitialized;

        public DatabaseService()
        {
            var url = "https://ebndevmaperaoshfrblu.supabase.co";
            var key = "sb_publishable_JrSXPoMHeeJYEe3jgJQ5ZQ_OGWoE-A1";

            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = true
            };

            _client = new Supabase.Client(url, key, options);
        }

        private async Task InitAsync()
        {
            if (_isInitialized) return;
            await _client.InitializeAsync();
            _isInitialized = true;
        }

        public async Task<(string Url, string ErrorMessage)> UploadFileStreamAsync(Stream stream, string originalFileName)
        {
            await InitAsync();

            if (stream == null)
                return (null, "No se pudo abrir el archivo seleccionado.");

            try
            {
                var storage = _client.Storage.From("Workflow-files");
                string fileName = $"{Guid.NewGuid()}_{originalFileName}";

                using (var memoryStream = new MemoryStream())
                {
                    await stream.CopyToAsync(memoryStream);
                    byte[] fileBytes = memoryStream.ToArray();

                    await storage.Upload(fileBytes, fileName);
                }

                string publicUrl = storage.GetPublicUrl(fileName);
                return (publicUrl, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error Supabase Storage: {ex.Message}");
                return (null, ex.Message);
            }
        }

        // --- UNIRSE A UNA CLASE POR CÓDIGO ---
        public async Task<(bool Success, string Message)> JoinCourseWithCodeAsync(string courseCode, int userId)
        {
            await InitAsync();

            if (string.IsNullOrWhiteSpace(courseCode))
                return (false, "El código no puede estar vacío.");

            try
            {
                var courseResponse = await _client.From<Course>()
                                                  .Where(c => c.CourseCode == courseCode)
                                                  .Get();

                var course = courseResponse.Models.FirstOrDefault();
                if (course == null)
                    return (false, "No se encontró ninguna clase con ese código.");

                var memberResponse = await _client.From<CourseMember>()
                                                  .Where(m => m.CourseId == course.Id && m.UserId == userId)
                                                  .Get();

                if (memberResponse.Models.Any())
                    return (false, "Ya te encuentras inscrito en esta clase.");

                var newStudent = new CourseMember
                {
                    CourseId = course.Id,
                    UserId = userId,
                    Role = "Student",
                    IsPrimary = false
                };

                await _client.From<CourseMember>().Insert(newStudent);
                return (true, $"Te has unido a la clase '{course.CourseName}' exitosamente.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al unirse: {ex.Message}");
                return (false, "Ocurrió un error al intentar unirse a la clase.");
            }
        }

        public async Task<List<UnitEvaluationCriterion>> GetUnitCriteriaAsync(int unitId)
        {
            await InitAsync();
            var response = await _client.From<UnitEvaluationCriterion>()
                                       .Where(c => c.UnitId == unitId)
                                       .Get();
            return response.Models;
        }

        public async Task<bool> AddUnitCriterionAsync(UnitEvaluationCriterion criterion)
        {
            await InitAsync();
            var response = await _client.From<UnitEvaluationCriterion>().Insert(criterion);
            return response.Models.Count > 0;
        }

        public async Task<bool> DeleteUnitCriterionAsync(int criterionId)
        {
            await InitAsync();
            try
            {
                await _client.From<UnitEvaluationCriterion>().Where(c => c.Id == criterionId).Delete();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // --- CÁLCULO DE PROMEDIO DE UNIDAD PARA UN ALUMNO ---
        public async Task<double> CalculateStudentUnitGradeAsync(int unitId, int userId)
        {
            await InitAsync();
            var criteria = await GetUnitCriteriaAsync(unitId);
            if (!criteria.Any()) return 0;

            // Obtener tareas pertenecientes a la unidad
            var unitItems = await _client.From<ClassContentItem>()
                                         .Where(i => i.UnitId == unitId && i.ItemType == "Tarea")
                                         .Get();

            double finalGrade = 0;

            foreach (var criterion in criteria)
            {
                var criterionTaskIds = unitItems.Models.Where(i => i.CriterionId == criterion.Id).Select(i => i.Id).ToList();

                if (!criterionTaskIds.Any()) continue;

                // Obtener entregas calificadas del alumno para esas tareas
                var submissions = await _client.From<TaskSubmission>()
                                               .Where(s => s.UserId == userId)
                                               .Get();

                var gradedSubmissions = submissions.Models.Where(s => criterionTaskIds.Contains(s.ContentItemId) && s.Grade.HasValue).ToList();

                if (gradedSubmissions.Any())
                {
                    double criterionAverage = gradedSubmissions.Average(s => s.Grade.Value);
                    // Ponderar sobre el 100% del criterio
                    finalGrade += (criterionAverage * (criterion.Percentage / 100.0));
                }
            }

            return Math.Round(finalGrade, 2);
        }

        public async Task<string> GetUserRoleInCourseAsync(int courseId, int userId)
        {
            await InitAsync();
            var response = await _client.From<CourseMember>()
                                       .Where(m => m.CourseId == courseId && m.UserId == userId)
                                       .Get();
            return response.Models.FirstOrDefault()?.Role ?? "Student";
        }

        // --- OBTENER ALUMNOS DE LA CLASE ---
        public async Task<List<User>> GetCourseStudentsAsync(int courseId)
        {
            await InitAsync();
            var memberResponse = await _client.From<CourseMember>()
                                               .Where(m => m.CourseId == courseId && m.Role == "Student")
                                               .Get();

            var userIds = memberResponse.Models.Select(m => m.UserId).ToList();
            if (!userIds.Any()) return new List<User>();

            var usersResponse = await _client.From<User>()
                                              .Filter("id", Constants.Operator.In, userIds)
                                              .Get();

            return usersResponse.Models;
        }

        // --- OBTENER PROFESORES DE LA CLASE ---
        public async Task<List<User>> GetCourseTeachersAsync(int courseId)
        {
            await InitAsync();
            var memberResponse = await _client.From<CourseMember>()
                                               .Where(m => m.CourseId == courseId && m.Role == "Teacher")
                                               .Get();

            var userIds = memberResponse.Models.Select(m => m.UserId).ToList();
            if (!userIds.Any()) return new List<User>();

            var usersResponse = await _client.From<User>()
                                              .Filter("id", Constants.Operator.In, userIds)
                                              .Get();

            return usersResponse.Models;
        }

        // --- AUTENTICACIÓN Y USUARIOS ---
        public async Task<User> ValidateUserAsync(string usernameOrEmail, string password)
        {
            await InitAsync();
            var response = await _client.From<User>()
                                       .Where(u => u.Password == password && (u.Username == usernameOrEmail || u.Email == usernameOrEmail))
                                       .Get();
            return response.Models.FirstOrDefault();
        }

        public async Task<User> GetUserByUsernameOrEmailAsync(string usernameOrEmail)
        {
            await InitAsync();
            var response = await _client.From<User>()
                                       .Where(u => u.Username == usernameOrEmail || u.Email == usernameOrEmail)
                                       .Get();
            return response.Models.FirstOrDefault();
        }

        public async Task<User> GetUserByIdAsync(int id)
        {
            await InitAsync();
            var response = await _client.From<User>()
                                       .Where(u => u.Id == id)
                                       .Get();
            return response.Models.FirstOrDefault();
        }

        public async Task<bool> UserExistsAsync(string username, string email)
        {
            await InitAsync();
            var response = await _client.From<User>()
                                       .Where(u => u.Username == username || u.Email == email)
                                       .Get();
            return response.Models.Any();
        }

        public async Task<bool> RegisterUserAsync(User newUser)
        {
            await InitAsync();
            if (await UserExistsAsync(newUser.Username, newUser.Email))
                return false;

            var response = await _client.From<User>().Insert(newUser);
            return response.Models.Count > 0;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            await InitAsync();
            var response = await _client.From<User>().Update(user);
            return response.Models.Count > 0;
        }

        // --- CURSOS Y MIEMBROS ---
        public async Task<List<Course>> GetCoursesAsync(int userId)
        {
            await InitAsync();
            var memberResponse = await _client.From<CourseMember>()
                                               .Where(m => m.UserId == userId)
                                               .Get();

            var courseIds = memberResponse.Models.Select(m => m.CourseId).ToList();
            if (!courseIds.Any()) return new List<Course>();

            var coursesResponse = await _client.From<Course>()
                                   .Filter("id", Constants.Operator.In, courseIds)
                                   .Get();

            return coursesResponse.Models;
        }

        public async Task<bool> AddCourseAsync(Course course, User primaryTeacher)
        {
            await InitAsync();
            course.PrimaryTeacherId = primaryTeacher.Id;
            course.PrimaryTeacherName = primaryTeacher.Username;

            var courseResponse = await _client.From<Course>().Insert(course);
            var createdCourse = courseResponse.Models.FirstOrDefault();

            if (createdCourse != null)
            {
                var member = new CourseMember
                {
                    CourseId = createdCourse.Id,
                    UserId = primaryTeacher.Id,
                    Role = "Teacher",
                    IsPrimary = true
                };
                await _client.From<CourseMember>().Insert(member);
                return true;
            }
            return false;
        }

        public async Task<(bool Success, string Message)> AddTeacherToCourseAsync(int courseId, string teacherEmailOrUsername)
        {
            await InitAsync();

            var currentTeachers = await _client.From<CourseMember>()
                                               .Where(m => m.CourseId == courseId && m.Role == "Teacher")
                                               .Get();

            if (currentTeachers.Models.Count >= 5)
                return (false, "La clase ya alcanzó el límite máximo de 5 profesores.");

            var targetUser = await GetUserByUsernameOrEmailAsync(teacherEmailOrUsername);
            if (targetUser == null)
                return (false, "El usuario especificado no existe.");

            var existing = await _client.From<CourseMember>()
                                       .Where(m => m.CourseId == courseId && m.UserId == targetUser.Id)
                                       .Get();

            if (existing.Models.Any())
                return (false, "Este usuario ya forma parte de la clase.");

            var newMember = new CourseMember
            {
                CourseId = courseId,
                UserId = targetUser.Id,
                Role = "Teacher",
                IsPrimary = false
            };
            await _client.From<CourseMember>().Insert(newMember);

            return (true, $"Profesor {targetUser.Username} asignado correctamente.");
        }

        public async Task<bool> DeleteCourseAsync(int courseId)
        {
            await InitAsync();
            await _client.From<CourseMember>().Where(m => m.CourseId == courseId).Delete();
            await _client.From<Course>().Where(c => c.Id == courseId).Delete();
            return true;
        }

        // --- PUBLICACIONES Y CONTENIDOS ---
        public async Task<List<ClassContentItem>> GetCourseContentItemsAsync(int courseId)
        {
            await InitAsync();
            var response = await _client.From<ClassContentItem>()
                           .Where(i => i.CourseId == courseId)
                           .Order(i => i.CreatedAt, Constants.Ordering.Descending)
                           .Get();
            return response.Models;
        }

        public async Task<bool> AddClassContentItemAsync(ClassContentItem item)
        {
            await InitAsync();
            var response = await _client.From<ClassContentItem>().Insert(item);
            return response.Models.Count > 0;
        }

        // --- ACTUALIZAR ESTADO DE 2FA ---
        public async Task<bool> Update2FAStatusAsync(int userId, bool isEnabled)
        {
            await InitAsync();
            try
            {
                var user = await GetUserByIdAsync(userId);
                if (user == null) return false;

                user.Is2FAEnabled = isEnabled;
                var response = await _client.From<User>().Update(user);
                return response.Models.Count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al actualizar 2FA: {ex.Message}");
                return false;
            }
        }

        // --- OBTENER TODAS LAS ENTREGAS DE UNA TAREA ---
        public async Task<List<TaskSubmission>> GetTaskSubmissionsAsync(int contentItemId)
        {
            await InitAsync();
            var response = await _client.From<TaskSubmission>()
                                       .Where(s => s.ContentItemId == contentItemId)
                                       .Get();
            return response.Models;
        }

        // --- GUARDAR O ACTUALIZAR CALIFICACIÓN Y FEEDBACK ---
        public async Task<bool> UpdateTaskGradeAsync(int submissionId, double grade, string feedback)
        {
            await InitAsync();
            var response = await _client.From<TaskSubmission>()
                                       .Where(s => s.Id == submissionId)
                                       .Get();

            var submission = response.Models.FirstOrDefault();
            if (submission == null) return false;

            submission.Grade = grade;
            submission.Feedback = feedback;

            var updateResponse = await _client.From<TaskSubmission>().Update(submission);
            return updateResponse.Models.Count > 0;
        }

        public async Task<List<ClassContentItem>> GetGlobalActivitiesAsync(int userId)
        {
            await InitAsync();
            var memberResponse = await _client.From<CourseMember>()
                                               .Where(m => m.UserId == userId)
                                               .Get();

            var courseIds = memberResponse.Models.Select(m => m.CourseId).ToList();
            if (!courseIds.Any()) return new List<ClassContentItem>();

            var response = await _client.From<ClassContentItem>()
                            .Filter("course_id", Constants.Operator.In, courseIds)
                            .Order(i => i.CreatedAt, Constants.Ordering.Descending)
                            .Limit(20)
                            .Get();

            return response.Models;
        }

        // --- OBTENER UNIDADES DE UN CURSO ---
        public async Task<List<CourseUnit>> GetCourseUnitsAsync(int courseId)
        {
            await InitAsync();
            var response = await _client.From<CourseUnit>()
                                       .Where(u => u.CourseId == courseId)
                                       .Order(u => u.StartDate, Constants.Ordering.Ascending)
                                       .Get();
            return response.Models;
        }

        // --- CREAR UNIDAD ---
        public async Task<bool> AddCourseUnitAsync(CourseUnit unit)
        {
            await InitAsync();
            var response = await _client.From<CourseUnit>().Insert(unit);
            return response.Models.Count > 0;
        }

        // --- OBTENER UNIDAD ACTIVA SEGÚN LA FECHA DE PUBLICACIÓN ---
        public async Task<CourseUnit> GetActiveUnitForCourseAsync(int courseId, DateTime date)
        {
            await InitAsync();
            var units = await GetCourseUnitsAsync(courseId);
            return units.FirstOrDefault(u => date.Date >= u.StartDate.Date && date.Date <= u.EndDate.Date);
        }

        // --- ACTUALIZAR INSERTAR CONTENIDO (ASIGNACIÓN AUTOMÁTICA DE UNIDAD) ---
        public async Task<bool> AddClassContentItemWithAutoUnitAsync(ClassContentItem item)
        {
            await InitAsync();

            // Si no tiene unidad asignada explícitamente, busca la unidad según la fecha actual
            if (!item.UnitId.HasValue)
            {
                var activeUnit = await GetActiveUnitForCourseAsync(item.CourseId, DateTime.Now);
                if (activeUnit != null)
                {
                    item.UnitId = activeUnit.Id;
                }
            }

            var response = await _client.From<ClassContentItem>().Insert(item);
            return response.Models.Count > 0;
        }

        public async Task<bool> DeleteClassContentItemAsync(int itemId)
        {
            await InitAsync();
            try
            {
                await _client.From<ClassContentItem>().Where(i => i.Id == itemId).Delete();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al eliminar publicación: {ex.Message}");
                return false;
            }
        }

        // --- ENTREGAS DE TAREAS ---
        public async Task<TaskSubmission> GetTaskSubmissionAsync(int contentItemId, int userId)
        {
            await InitAsync();
            var response = await _client.From<TaskSubmission>()
                                       .Where(s => s.ContentItemId == contentItemId && s.UserId == userId)
                                       .Get();
            return response.Models.FirstOrDefault();
        }

        public async Task<bool> SaveTaskSubmissionAsync(TaskSubmission submission)
        {
            await InitAsync();
            var existing = await GetTaskSubmissionAsync(submission.ContentItemId, submission.UserId);

            if (existing != null)
            {
                existing.FilePath = submission.FilePath;
                existing.SubmittedAt = DateTime.Now;
                var updateResponse = await _client.From<TaskSubmission>().Update(existing);
                return updateResponse.Models.Count > 0;
            }

            var insertResponse = await _client.From<TaskSubmission>().Insert(submission);
            return insertResponse.Models.Count > 0;
        }
    }
}