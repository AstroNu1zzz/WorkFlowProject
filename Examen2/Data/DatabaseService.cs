using Supabase;
using Supabase.Postgrest;
using Examen2.ViewModels;
using Newtonsoft.Json;

namespace Examen2.Data
{
    public class DatabaseService
    {
        private readonly Supabase.Client _client;
        private bool _isInitialized;
        private string _authToken;

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
            if (!_isInitialized)
            {
                await _client.InitializeAsync();
                _isInitialized = true;
            }

            ApplyAuthHeader();
        }

        public void SetAuthToken(string jwt)
        {
            _authToken = jwt;
            ApplyAuthHeader();
        }

        private void ApplyAuthHeader()
        {
            if (_client == null) return;

            if (string.IsNullOrWhiteSpace(_authToken))
            {
                _client.Postgrest.Options.Headers.Remove("Authorization");
                _client.Storage.Headers.Remove("Authorization");
            }
            else
            {
                _client.Postgrest.Options.Headers["Authorization"] = $"Bearer {_authToken}";
                _client.Storage.Headers["Authorization"] = $"Bearer {_authToken}";
            }
        }

        // --- SUBIDA GENÉRICA DE ARCHIVOS A SUPABASE STORAGE ---
        public async Task<(string Url, string ErrorMessage)> UploadFileStreamAsync(Stream stream, string originalFileName)
        {
            await InitAsync();

            if (stream == null)
                return (null, "No se pudo abrir el archivo seleccionado.");

            try
            {
                var storage = _client.Storage.From("Workflow-files");
                string fileName = $"avatars/{Guid.NewGuid()}_{originalFileName}";

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

        // --- SUBIDA DE PORTADA DE CLASE ---
        public async Task<(string Url, string ErrorMessage)> UploadCourseBannerAsync(Stream stream, string originalFileName)
        {
            await InitAsync();

            if (stream == null)
                return (null, "No se pudo abrir la imagen seleccionada.");

            try
            {
                var storage = _client.Storage.From("Workflow-files");
                string fileName = $"banners/{Guid.NewGuid()}_{originalFileName}";

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
                System.Diagnostics.Debug.WriteLine($"Error al subir portada: {ex.Message}");
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

            var unitItems = await _client.From<ClassContentItem>()
                                         .Where(i => i.UnitId == unitId && i.ItemType == "Tarea")
                                         .Get();

            double finalGrade = 0;

            foreach (var criterion in criteria)
            {
                var criterionTaskIds = unitItems.Models.Where(i => i.CriterionId == criterion.Id).Select(i => i.Id).ToList();

                if (!criterionTaskIds.Any()) continue;

                var submissions = await _client.From<TaskSubmission>()
                                               .Where(s => s.UserId == userId)
                                               .Get();

                var gradedSubmissions = submissions.Models.Where(s => criterionTaskIds.Contains(s.ContentItemId) && s.Grade.HasValue).ToList();

                if (gradedSubmissions.Any())
                {
                    double criterionAverage = gradedSubmissions.Average(s => s.Grade.Value);
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

        // --- OBTENER ALUMNOS DE LA CLASE (VÍA RPC SEGURA) ---
        public async Task<List<User>> GetCourseStudentsAsync(int courseId)
        {
            await InitAsync();
            var memberResponse = await _client.From<CourseMember>()
                                               .Where(m => m.CourseId == courseId && m.Role == "Student")
                                               .Get();

            var userIds = memberResponse.Models.Select(m => m.UserId).ToList();
            if (!userIds.Any()) return new List<User>();

            var rpc = await _client.Rpc("rpc_get_users_by_ids", new Dictionary<string, object>
            {
                { "p_user_ids", userIds }
            });

            return JsonConvert.DeserializeObject<List<User>>(rpc.Content) ?? new List<User>();
        }

        // --- OBTENER PROFESORES DE LA CLASE (VÍA RPC SEGURA) ---
        public async Task<List<User>> GetCourseTeachersAsync(int courseId)
        {
            await InitAsync();
            var memberResponse = await _client.From<CourseMember>()
                                               .Where(m => m.CourseId == courseId && m.Role == "Teacher")
                                               .Get();

            var userIds = memberResponse.Models.Select(m => m.UserId).ToList();
            if (!userIds.Any()) return new List<User>();

            var rpc = await _client.Rpc("rpc_get_users_by_ids", new Dictionary<string, object>
            {
                { "p_user_ids", userIds }
            });

            return JsonConvert.DeserializeObject<List<User>>(rpc.Content) ?? new List<User>();
        }

        // --- AUTENTICACIÓN Y USUARIOS (VÍA RPC SEGURAS) ---
        public async Task<User> GetUserByUsernameOrEmailAsync(string usernameOrEmail)
        {
            await InitAsync();
            var rpc = await _client.Rpc("rpc_get_user_by_identifier", new Dictionary<string, object>
            {
                { "p_identifier", usernameOrEmail }
            });

            return JsonConvert.DeserializeObject<List<User>>(rpc.Content)?.FirstOrDefault();
        }

        public async Task<User> GetUserByIdAsync(int id)
        {
            await InitAsync();
            var rpc = await _client.Rpc("rpc_get_user_by_id", new Dictionary<string, object>
            {
                { "p_user_id", id }
            });

            return JsonConvert.DeserializeObject<List<User>>(rpc.Content)?.FirstOrDefault();
        }

        public async Task<bool> UserExistsAsync(string username, string email)
        {
            return await GetUserByUsernameOrEmailAsync(username) != null
                || await GetUserByUsernameOrEmailAsync(email) != null;
        }

        public async Task<bool> RegisterUserAsync(User newUser)
        {
            await InitAsync();
            var rpc = await _client.Rpc("rpc_register_user", new Dictionary<string, object>
            {
                { "p_username", newUser.Username },
                { "p_email", newUser.Email },
                { "p_password", newUser.Password }
            });

            return bool.TryParse(rpc.Content?.Trim('"'), out bool created) && created;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            await InitAsync();
            try
            {
                var rpc = await _client.Rpc("rpc_update_user_profile", new Dictionary<string, object>
                {
                    { "p_user_id", user.Id },
                    { "p_username", user.Username },
                    { "p_email", user.Email },
                    { "p_profile_picture_path", user.ProfilePicturePath },
                    { "p_is_2fa_enabled", user.Is2FAEnabled },
                    { "p_is_email_verified", user.IsEmailVerified },
                    { "p_app_theme_preference", user.AppThemePreference ?? "Sistema" }
                });

                return bool.TryParse(rpc.Content?.Trim('"'), out bool updated) && updated;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al actualizar usuario: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> VerifyUserPasswordAsync(int userId, string password)
        {
            await InitAsync();
            try
            {
                var rpc = await _client.Rpc("rpc_verify_password", new Dictionary<string, object>
                {
                    { "p_user_id", userId },
                    { "p_password", password }
                });

                return bool.TryParse(rpc.Content?.Trim('"'), out bool valid) && valid;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UpdateUserPasswordAsync(int userId, string newPassword)
        {
            await InitAsync();
            try
            {
                var rpc = await _client.Rpc("rpc_change_password", new Dictionary<string, object>
                {
                    { "p_user_id", userId },
                    { "p_new_password", newPassword }
                });

                return bool.TryParse(rpc.Content?.Trim('"'), out bool updated) && updated;
            }
            catch
            {
                return false;
            }
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

        public async Task<bool> UpdateCourseAsync(Course course)
        {
            await InitAsync();
            try
            {
                var response = await _client.From<Course>().Update(course);
                return response.Models.Count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al actualizar clase: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RemoveMemberFromCourseAsync(int courseId, int userId)
        {
            await InitAsync();
            try
            {
                await _client.From<CourseMember>()
                             .Where(m => m.CourseId == courseId && m.UserId == userId)
                             .Delete();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al sacar miembro de la clase: {ex.Message}");
                return false;
            }
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
                return await UpdateUserAsync(user);
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

        public async Task<HashSet<int>> GetCompletedTaskIdsAsync(int userId)
        {
            await InitAsync();
            try
            {
                var response = await _client.From<TaskSubmission>()
                                            .Where(s => s.UserId == userId)
                                            .Get();

                var completedIds = new HashSet<int>();
                foreach (var sub in response.Models)
                {
                    if (sub.Grade.HasValue || !string.IsNullOrWhiteSpace(sub.FilePath) || sub.SubmittedAt != default)
                    {
                        completedIds.Add(sub.ContentItemId);
                    }
                }

                return completedIds;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al obtener tareas entregadas: {ex.Message}");
                return new HashSet<int>();
            }
        }

        public async Task<HashSet<int>> GetTeacherCourseIdsAsync(int userId)
        {
            await InitAsync();
            try
            {
                var response = await _client.From<CourseMember>()
                                            .Where(m => m.UserId == userId && m.Role == "Teacher")
                                            .Get();

                return response.Models.Select(m => m.CourseId).ToHashSet();
            }
            catch
            {
                return new HashSet<int>();
            }
        }

        public async Task<(bool Success, string Message)> SaveTaskSubmissionAsync(TaskSubmission submission)
        {
            await InitAsync();

            var itemResponse = await _client.From<ClassContentItem>()
                                            .Where(i => i.Id == submission.ContentItemId)
                                            .Get();
            var taskItem = itemResponse.Models.FirstOrDefault();

            if (taskItem != null && taskItem.DueDate.HasValue && DateTime.Now > taskItem.DueDate.Value)
            {
                return (false, "El plazo de entrega ha vencido. Ya no se permiten envíos.");
            }

            var existing = await GetTaskSubmissionAsync(submission.ContentItemId, submission.UserId);

            if (existing != null)
            {
                existing.FilePath = submission.FilePath;
                existing.SubmittedAt = DateTime.Now;
                var updateResponse = await _client.From<TaskSubmission>().Update(existing);
                return (updateResponse.Models.Count > 0, "Entrega actualizada correctamente.");
            }

            var insertResponse = await _client.From<TaskSubmission>().Insert(submission);
            return (insertResponse.Models.Count > 0, "Tarea entregada correctamente.");
        }
    }
}