using System.ComponentModel.DataAnnotations;

namespace ST10439147_CLDV6212_POE.Models
{
    public class FileUpload
    {
        [Key]
        public string FileId { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [StringLength(255)]
        public string FileName { get; set; }

        [Required]
        public string FileUrl { get; set; }

        [StringLength(100)]
        public string UploadedBy { get; set; }

        public DateTime UploadedOn { get; set; } = DateTime.UtcNow;

        [StringLength(10)]
        public string FileType { get; set; }

        [Range(1, long.MaxValue)]
        public long FileSize { get; set; }

        // Additional properties for the specific file types you're working with
        public string ContentType { get; set; }

        // Computed property for display
        public string FileSizeFormatted
        {
            get
            {
                if (FileSize < 1024) return $"{FileSize} B";
                if (FileSize < 1024 * 1024) return $"{FileSize / 1024:F1} KB";
                return $"{FileSize / (1024 * 1024):F1} MB";
            }
        }

        // Helper method to check if file type is allowed
        public static bool IsAllowedFileType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension == ".pdf" || extension == ".docx" || extension == ".txt" || extension == ".xlsx";
        }

        // Helper method to get content type
        public static string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".txt" => "text/plain",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"
            };
        }
    }
}