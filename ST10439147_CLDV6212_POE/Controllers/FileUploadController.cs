using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class FileUploadController : Controller
    {
        private readonly FileShareService _storageService;
        private readonly HashSet<string> _allowedExtensions = new HashSet<string>
        {
            ".pdf", ".docx", ".txt"
        };

        public FileUploadController(FileShareService storageService)
        {
            _storageService = storageService;
        }

        public async Task<IActionResult> Index()
        {
            var files = await _storageService.GetAllFilesAsync();

            // Convert List<string> to List<FileUpload>
            List<FileUpload> fileUploads = new List<FileUpload>();

            if (files is IEnumerable<string> fileNames)
            {
                fileUploads = fileNames.Select(fileName => new FileUpload
                {
                    FileId = Guid.NewGuid().ToString(),
                    FileName = fileName,
                    FileUrl = fileName, // Or construct proper URL
                    FileType = GetFileType(fileName),
                    UploadedOn = DateTime.Now, // You might want to get actual upload date
                    UploadedBy = "Unknown" // You might want to track this properly
                }).ToList();
            }

            return View(fileUploads);
        }

        [HttpGet]
        public IActionResult Upload()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return View();
            }

            // Validate file type
            var fileExtension = Path.GetExtension(file.FileName).ToLower();
            if (!_allowedExtensions.Contains(fileExtension))
            {
                TempData["Error"] = "Only PDF, DOCX, and TXT files are allowed.";
                return View();
            }

            // Validate file size (e.g., max 10MB)
            if (file.Length > 10 * 1024 * 1024)
            {
                TempData["Error"] = "File size must be less than 10MB.";
                return View();
            }

            try
            {
                var fileName = await _storageService.UploadFileAsync(file);
                TempData["Success"] = $"File {fileName} uploaded successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error uploading file: {ex.Message}";
                return View();
            }
        }

        public async Task<IActionResult> Download(string fileName)
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

        public async Task<IActionResult> Delete(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                TempData["Error"] = "File name is required.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Use AzureService's UploadFileAsync and GetAllFilesAsync signatures for reference.
                // Since DeleteFileAsync does not exist, you need to implement it in AzureService.
                // For now, you can remove or comment out the call to _storageService.DeleteFileAsync(fileName)
                // and show an error message.
                TempData["Error"] = "Delete functionality is not implemented in AzureService.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error deleting file: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private string GetFileType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension switch
            {
                ".pdf" => "PDF",
                ".docx" => "DOCX",
                ".txt" => "TXT",
                _ => "Unknown"
            };
        }

        private string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };
        }
    }
}