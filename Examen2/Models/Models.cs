using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.Json.Serialization;

namespace Examen2.Data
{
    [Table("users")]
    public class User : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("username")]
        public string Username { get; set; }

        [Column("email")]
        public string Email { get; set; }

        [Column("password")]
        public string Password { get; set; }

        [Column("profile_picture_path")]
        public string ProfilePicturePath { get; set; }

        [Column("is_2fa_enabled")]
        public bool Is2FAEnabled { get; set; }
    }

    [Table("courses")]
    public class Course : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("course_name")]
        public string CourseName { get; set; }

        [Column("teacher_name")]
        public string TeacherName { get; set; }

        [Column("theme_color")]
        public string ThemeColor { get; set; }

        [Column("primary_teacher_id")]
        public int PrimaryTeacherId { get; set; }

        [Column("primary_teacher_name")]
        public string PrimaryTeacherName { get; set; }

        [Column("banner_image_path")]
        public string BannerImagePath { get; set; } = "default_course_bg.jpg";

        [Column("course_code")]
        public string CourseCode { get; set; }

        // Atributos para que los serializadores JSON omitan la propiedad al hacer Insert/Update
        [JsonIgnore]
        public string DisplayTeacherName => !string.IsNullOrWhiteSpace(TeacherName)
            ? TeacherName
            : (!string.IsNullOrWhiteSpace(PrimaryTeacherName) ? PrimaryTeacherName : "Sin Profesor Assig.");
    }

    [Table("course_members")]
    public class CourseMember : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("course_id")]
        public int CourseId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("role")]
        public string Role { get; set; }

        [Column("is_primary")]
        public bool IsPrimary { get; set; }
    }

    [Table("class_content_items")]
    public class ClassContentItem : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("course_id")]
        public int CourseId { get; set; }

        [Column("unit_id")]
        public int? UnitId { get; set; }

        [Column("criterion_id")]
        public int? CriterionId { get; set; }

        [Column("item_type")]
        public string ItemType { get; set; }

        [Column("title")]
        public string Title { get; set; }

        [Column("description")]
        public string Description { get; set; }

        [Column("points")]
        public int Points { get; set; } = 100;

        [Column("due_date")]
        public DateTime? DueDate { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("author_name")]
        public string AuthorName { get; set; }

        [Column("attached_file_path")]
        public string AttachedFilePath { get; set; }

        // Atributos para que el serializador JSON no envíe esta propiedad a Supabase
        [JsonIgnore]
        public bool IsTask => ItemType == "Tarea";
    }

    [Table("task_submissions")]
    public class TaskSubmission : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("content_item_id")]
        public int ContentItemId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("file_path")]
        public string FilePath { get; set; }

        [Column("submitted_at")]
        public DateTime SubmittedAt { get; set; } = DateTime.Now;

        [Column("grade")]
        public double? Grade { get; set; }

        [Column("feedback")]
        public string Feedback { get; set; }
    }

    [Table("course_units")]
    public class CourseUnit : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("course_id")]
        public int CourseId { get; set; }

        [Column("title")]
        public string Title { get; set; }

        [Column("start_date")]
        public DateTime StartDate { get; set; }

        [Column("end_date")]
        public DateTime EndDate { get; set; }
    }
    [Table("unit_evaluation_criteria")]
    public class UnitEvaluationCriterion : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("unit_id")]
        public int UnitId { get; set; }

        [Column("name")]
        public string Name { get; set; }

        [Column("percentage")]
        public double Percentage { get; set; }
    }
}