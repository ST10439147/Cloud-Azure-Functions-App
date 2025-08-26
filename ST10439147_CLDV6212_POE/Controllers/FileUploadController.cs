// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 1

//References:
// ClaudAI - https://claude.ai/
// ChatGPT - https://chat.openai.com/
// W3schools - https://www.w3schools.com/
// IIEVC School of Computer Science Youtube channel for Azure services setup and use https://www.youtube.com/@VCSOCS
// AzureApp project done in class with lecturer

using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class FileUploadController : Controller
    {
        private readonly FileShareService _storageService;
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Allowed file extensions
        // hashset is used for O(1) lookup time, making it efficient for validation, especially when the list of allowed extensions is small and fixed.
        // 0(1) lookup time means that the time it takes to check if an item exists in the HashSet does not increase with the number of items in the set.
        // This is particularly useful in scenarios like file uploads where quick validation is necessary.
        private readonly HashSet<string> _allowedExtensions = new HashSet<string>
        {
            ".pdf", ".docx", ".txt", ".xlsx"
        };
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Constructor
        public FileUploadController(FileShareService storageService)
        {
            _storageService = storageService;// Dependency Injection, allows for better testing and separation of concerns
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        public async Task<IActionResult> Index()
        {
            var files = await _storageService.GetAllFilesAsync();// Get list of file names from Azure File Share

            // Convert List<string> to List<FileUpload>
            List<FileUpload> fileUploads = new List<FileUpload>();// Initialize an empty list

            if (files is IEnumerable<string> fileNames)// Check if files is IEnumerable<string>
            {
                fileUploads = fileNames.Select(fileName => new FileUpload// Map each file name to a FileUpload object
                {
                    FileId = Guid.NewGuid().ToString(),// Generate a new GUID for FileId
                    FileName = fileName,
                    FileUrl = fileName, // Or construct proper URL
                    FileType = GetFileType(fileName),
                    UploadedOn = DateTime.Now, // You might want to get actual upload date
                    UploadedBy = "Unknown" // You might want to track this properly
                }).ToList();
            }

            return View(fileUploads);// Pass the list to the view
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        [HttpGet]
        public IActionResult Upload()// Display the upload form
        {
            return View();
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        [HttpPost]
        // this method handles the file upload process. It first checks if a file was provided and validates its type and size.
        // If the file passes validation, it attempts to upload the file using the FileShareService.
        // Depending on the outcome, it sets appropriate success or error messages in TempData and redirects or returns the view accordingly.
        public async Task<IActionResult> Upload(IFormFile file)// Handle the file upload
        {
            if (file == null || file.Length == 0)// Validate file presence
            {
                TempData["Error"] = "Please select a file to upload.";// TempData is used to pass data between actions, especially after a redirect
                return View();
            }

            // Validate file type
            var fileExtension = Path.GetExtension(file.FileName).ToLower();// Get file extension and convert to lower case for case-insensitive comparison
            if (!_allowedExtensions.Contains(fileExtension))
            {
                TempData["Error"] = "Only PDF, DOCX, XLSX, and TXT files are allowed.";// Provide feedback to user, only specific file types are allowed
                return View();
            }

            // Validate file size (e.g., max 10MB)
            if (file.Length > 10 * 1024 * 1024)
            {
                TempData["Error"] = "File size must be less than 10MB.";
                return View();
            }

            try// Attempt to upload the file
            {
                var fileName = await _storageService.UploadFileAsync(file);
                TempData["Success"] = $"File {fileName} uploaded successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)// Handle any errors during upload
            {
                TempData["Error"] = $"Error uploading file: {ex.Message}";
                return View();
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // This method handles file download requests. It first checks if a valid file name is provided.
        // If the file name is valid, it attempts to download the file using the FileShareService.
        // Upon successful download, it returns the file to the user with the appropriate content type.
        public async Task<IActionResult> Download(string fileName)// Handle file download
        {
            if (string.IsNullOrEmpty(fileName))
            {
                TempData["Error"] = "File name is required.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var fileStream = await _storageService.DownloadFileAsync(fileName);
                var contentType = GetContentType(fileName);
                return File(fileStream, contentType, fileName);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error downloading file: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // This method handles file deletion requests. It first checks if a valid file name is provided.
        // If the file name is valid, it attempts to delete the file using the FileShareService.
        // Depending on the outcome, it sets appropriate success or error messages in TempData and redirects to the Index action.
        // The method is asynchronous to ensure non-blocking operations during file deletion.
        public async Task<IActionResult> Delete(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                TempData["Error"] = "File name is required.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var deleted = await _storageService.DeleteFileAsync(fileName);

                if (deleted)
                {
                    TempData["Success"] = $"File {fileName} deleted successfully!";
                }
                else
                {
                    TempData["Error"] = $"File {fileName} not found or could not be deleted.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error deleting file: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Helper methods to determine file type and content type based on extension
        // These methods use switch expressions for clarity
        private string GetFileType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension switch
            {
                ".pdf" => "PDF",
                ".docx" => "DOCX",
                ".txt" => "TXT",
                ".xlsx" => "XLSX",
                _ => "Unknown"
            };
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // This method determines the MIME content type of a file based on its extension.
        // It uses a switch expression to map common file extensions to their corresponding content types.
        // If the file extension is not recognized, it defaults to "application/octet-stream".
        private string GetContentType(string fileName)
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
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//