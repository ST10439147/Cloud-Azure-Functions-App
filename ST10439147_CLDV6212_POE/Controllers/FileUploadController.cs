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
using System.Net.Http.Headers;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class FileUploadController : Controller
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<FileUploadController> _logger;
        private readonly string _functionBaseUrl;
        private readonly string _functionKey;

        // Allowed file extensions
        private readonly HashSet<string> _allowedExtensions = new HashSet<string>
        {
            ".pdf", ".docx", ".txt", ".xlsx"
        };

        public FileUploadController(IHttpClientFactory httpClientFactory, ILogger<FileUploadController> logger, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _functionBaseUrl = configuration["AzureFunctions:BaseUrl"];
            _functionKey = configuration["AzureFunctions:FunctionKey"];
        }

        // GET: FileUpload/Index
        public async Task<IActionResult> Index()
        {
            try
            {
                _logger.LogInformation("Retrieving all files from Azure Function");

                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/files");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var fileNames = JsonSerializer.Deserialize<List<string>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    // Convert List<string> to List<FileUpload>
                    var fileUploads = fileNames?.Select(fileName => new FileUpload
                    {
                        FileId = Guid.NewGuid().ToString(),
                        FileName = fileName,
                        FileUrl = fileName,
                        FileType = GetFileType(fileName),
                        UploadedOn = DateTime.Now,
                        UploadedBy = "Unknown"
                    }).ToList() ?? new List<FileUpload>();

                    return View(fileUploads);
                }

                _logger.LogError($"Error retrieving files: {response.StatusCode}");
                ViewBag.Error = "Unable to load files. Please try again.";
                return View(new List<FileUpload>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving files");
                ViewBag.Error = "Unable to load files. Please try again.";
                return View(new List<FileUpload>());
            }
        }

        // GET: FileUpload/Upload
        [HttpGet]
        public IActionResult Upload()
        {
            return View();
        }

        // POST: FileUpload/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
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
                TempData["Error"] = "Only PDF, DOCX, XLSX, and TXT files are allowed.";
                return View();
            }

            // Validate file size (max 10MB)
            if (file.Length > 10 * 1024 * 1024)
            {
                TempData["Error"] = "File size must be less than 10MB.";
                return View();
            }

            try
            {
                _logger.LogInformation($"Uploading file: {file.FileName}");

                // Create multipart form data
                using var formData = new MultipartFormDataContent();
                var fileContent = new StreamContent(file.OpenReadStream());
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                formData.Add(fileContent, "file", file.FileName);

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_functionBaseUrl}/files/upload");
                request.Headers.Add("x-functions-key", _functionKey);
                request.Content = formData;

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<FileUploadResponse>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    TempData["Success"] = result?.Message ?? $"File {file.FileName} uploaded successfully!";
                    return RedirectToAction(nameof(Index));
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Error uploading file: {response.StatusCode} - {errorContent}");

                // Try to parse error message
                try
                {
                    var errorObj = JsonSerializer.Deserialize<FileUploadResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    TempData["Error"] = errorObj?.Message ?? "Error uploading file. Please try again.";
                }
                catch
                {
                    TempData["Error"] = "Error uploading file. Please try again.";
                }

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading file: {file.FileName}");
                TempData["Error"] = $"Error uploading file: {ex.Message}";
                return View();
            }
        }

        // GET: FileUpload/Download
        public async Task<IActionResult> Download(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                TempData["Error"] = "File name is required.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _logger.LogInformation($"Downloading file: {fileName}");

                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/files/download/{Uri.EscapeDataString(fileName)}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var fileStream = await response.Content.ReadAsStreamAsync();
                    var contentType = GetContentType(fileName);
                    return File(fileStream, contentType, fileName);
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = $"File {fileName} not found.";
                    return RedirectToAction(nameof(Index));
                }

                throw new Exception($"Error downloading file: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading file: {fileName}");
                TempData["Error"] = $"Error downloading file: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: FileUpload/Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                TempData["Error"] = "File name is required.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _logger.LogInformation($"Deleting file: {fileName}");

                var request = new HttpRequestMessage(HttpMethod.Delete, $"{_functionBaseUrl}/files/{Uri.EscapeDataString(fileName)}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    TempData["Success"] = $"File {fileName} deleted successfully!";
                    return RedirectToAction(nameof(Index));
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = $"File {fileName} not found or could not be deleted.";
                    return RedirectToAction(nameof(Index));
                }

                throw new Exception($"Error deleting file: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting file: {fileName}");
                TempData["Error"] = $"Error deleting file: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // Helper methods
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

        // Response model for JSON deserialization
        private class FileUploadResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string FileName { get; set; }
            public string OriginalFileName { get; set; }
            public long FileSize { get; set; }
            public string FileType { get; set; }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//