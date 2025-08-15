namespace ST10439147_CLDV6212_POE.Models
{
    public class FileUpload
    {
        public string FileId { get; set; }        // Unique ID (GUID)
        public string FileName { get; set; }      // Original file name
        public string FileUrl { get; set; }       // Path/URL in Azure File Storage
        public string UploadedBy { get; set; }    // CustomerId or Admin who uploaded
        public DateTime UploadedOn { get; set; }  // Upload timestamp

        public string FileType { get; set; }      // e.g. PDF, DOCX, JPG
        public long FileSize { get; set; }        // Size in bytes
    }
}
