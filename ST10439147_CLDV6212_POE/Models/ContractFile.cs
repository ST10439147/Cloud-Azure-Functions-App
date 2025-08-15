namespace ST10439147_CLDV6212_POE.Models
{
    public class ContractFile
    {
        public string FileName { get; set; }
        public string FilePath { get; set; } // Location in Azure Files
        public DateTime UploadDate { get; set; }
        public string UploadedBy { get; set; }
    }
}
