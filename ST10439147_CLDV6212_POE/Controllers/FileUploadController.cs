using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class FileUploadController : Controller
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<FileUploadController> _logger;
        private readonly string _functionBaseUrl;
        private readonly string _functionKey;

        private readonly HashSet<string> _allowedExtensions = new HashSet<string>
        {
            ".pdf", ".docx", ".txt", ".xlsx"
        };

        public FileUploadController(IHttpClientFactory httpClientFactory, ILogger<FileUploadController> logger, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _functionBaseUrl = configuration["AzureFunctions:BaseUrl"]
                ?? throw new ArgumentNullException("AzureFunctions:BaseUrl configuration is missing");
            _functionKey = configuration["AzureFunctions:FunctionKey"]
                ?? throw new ArgumentNullException("AzureFunctions:FunctionKey configuration is missing");

            _logger.LogInformation($"FileUploadController initialized with base URL: {_functionBaseUrl}");
        }

        /// <summary>
        /// GET: FileUpload/Index - Display all uploaded files
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index()
        {
            try
            {
                _logger.LogInformation("Retrieving all files from Azure Function API");

                // Get current user's email for display purposes
                var currentUserEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "Unknown";

                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/files");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation($"Received response: {content}");

                    // Try to deserialize as list of file metadata objects first
                    try
                    {
                        var fileDataList = JsonSerializer.Deserialize<List<FileMetadata>>(content, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (fileDataList != null && fileDataList.Any())
                        {
                            var fileUploads = fileDataList.Select(data => new FileUpload
                            {
                                FileId = Guid.NewGuid().ToString(),
                                FileName = data.FileName ?? "Unknown",
                                FileUrl = data.FileName ?? "",
                                FileType = GetFileType(data.FileName ?? ""),
                                FileSize = data.FileSize,
                                UploadedOn = data.UploadedOn,
                                UploadedBy = data.UploadedBy ?? "Unknown"
                            }).ToList();

                            _logger.LogInformation($"Successfully retrieved {fileUploads.Count} files with metadata");
                            return View(fileUploads);
                        }
                    }
                    catch (JsonException)
                    {
                        // If that fails, try deserializing as simple string list (old format)
                        _logger.LogInformation("Falling back to simple file name list format");
                    }

                    // Fallback: deserialize as simple list of file names
                    var fileNames = JsonSerializer.Deserialize<List<string>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<string>();

                    var simpleFileUploads = fileNames.Select(fileName => new FileUpload
                    {
                        FileId = Guid.NewGuid().ToString(),
                        FileName = fileName,
                        FileUrl = fileName,
                        FileType = GetFileType(fileName),
                        UploadedOn = DateTime.Now,
                        UploadedBy = currentUserEmail // Show current user as fallback
                    }).ToList();

                    _logger.LogInformation($"Successfully retrieved {simpleFileUploads.Count} files (simple format)");
                    return View(simpleFileUploads);
                }

                _logger.LogError($"Error retrieving files from API: {response.StatusCode}");
                ViewBag.Error = $"Unable to load files. Status: {response.StatusCode}";
                return View(new List<FileUpload>());
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, "HTTP error retrieving files from Azure Function");
                ViewBag.Error = "Unable to connect to the file service. Please check if the Azure Function is running.";
                return View(new List<FileUpload>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving files");
                ViewBag.Error = "An unexpected error occurred while loading files.";
                return View(new List<FileUpload>());
            }
        }

        /// <summary>
        /// GET: FileUpload/Upload
        /// </summary>
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult Upload()
        {
            return View();
        }

        /// <summary>
        /// POST: FileUpload/Upload - Upload a file to Azure
        /// </summary>
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return View();
            }

            var fileExtension = Path.GetExtension(file.FileName).ToLower();
            if (!_allowedExtensions.Contains(fileExtension))
            {
                TempData["Error"] = "Only PDF, DOCX, XLSX, and TXT files are allowed.";
                return View();
            }

            if (file.Length > 10 * 1024 * 1024)
            {
                TempData["Error"] = "File size must be less than 10MB.";
                return View();
            }

            try
            {
                // Get the logged-in user's email
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "Unknown";

                _logger.LogInformation($"Uploading file: {file.FileName} ({file.Length} bytes) by {userEmail}");

                using var formData = new MultipartFormDataContent();
                using var fileStream = file.OpenReadStream();

                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

                formData.Add(fileContent, "file", file.FileName);

                // Add uploader email to form data for Azure Function to store
                formData.Add(new StringContent(userEmail), "uploaderEmail");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_functionBaseUrl}/files/upload");
                request.Headers.Add("x-functions-key", _functionKey);
                request.Content = formData;

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation($"Upload response: {response.StatusCode} - {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<FileUploadResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    TempData["Success"] = result?.Message ?? $"File '{file.FileName}' uploaded successfully!";
                    _logger.LogInformation($"File uploaded successfully: {file.FileName}");
                    return RedirectToAction(nameof(Index));
                }

                try
                {
                    var errorObj = JsonSerializer.Deserialize<FileUploadResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    TempData["Error"] = errorObj?.Message ?? "Error uploading file. Please try again.";
                }
                catch
                {
                    TempData["Error"] = $"Error uploading file: {response.StatusCode}";
                }

                _logger.LogError($"Error uploading file: {response.StatusCode} - {responseContent}");
                return View();
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, $"HTTP error uploading file: {file.FileName}");
                TempData["Error"] = "Unable to connect to the file service. Please check if the Azure Function is running.";
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading file: {file.FileName}");
                TempData["Error"] = $"An unexpected error occurred: {ex.Message}";
                return View();
            }
        }

        /// <summary>
        /// GET: FileUpload/Download - Download a file
        /// </summary>
        [Authorize(Roles = "Admin")]
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

                var encodedFileName = Uri.EscapeDataString(fileName);
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/files/download/{encodedFileName}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var fileStream = await response.Content.ReadAsStreamAsync();
                    var contentType = GetContentType(fileName);

                    _logger.LogInformation($"File downloaded successfully: {fileName}");
                    return File(fileStream, contentType, fileName);
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = $"File '{fileName}' not found.";
                    _logger.LogWarning($"File not found: {fileName}");
                    return RedirectToAction(nameof(Index));
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Error downloading file: {response.StatusCode} - {errorContent}");
                TempData["Error"] = $"Error downloading file: {response.StatusCode}";
                return RedirectToAction(nameof(Index));
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, $"HTTP error downloading file: {fileName}");
                TempData["Error"] = "Unable to connect to the file service. Please check if the Azure Function is running.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading file: {fileName}");
                TempData["Error"] = $"An unexpected error occurred: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// POST: FileUpload/Delete - Delete a file
        /// </summary>
        [Authorize(Roles = "Admin")]
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

                var encodedFileName = Uri.EscapeDataString(fileName);
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{_functionBaseUrl}/files/{encodedFileName}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation($"Delete response: {response.StatusCode} - {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    TempData["Success"] = $"File '{fileName}' deleted successfully!";
                    _logger.LogInformation($"File deleted successfully: {fileName}");
                    return RedirectToAction(nameof(Index));
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = $"File '{fileName}' not found or could not be deleted.";
                    _logger.LogWarning($"File not found for deletion: {fileName}");
                    return RedirectToAction(nameof(Index));
                }

                _logger.LogError($"Error deleting file: {response.StatusCode} - {responseContent}");
                TempData["Error"] = $"Error deleting file: {response.StatusCode}";
                return RedirectToAction(nameof(Index));
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, $"HTTP error deleting file: {fileName}");
                TempData["Error"] = "Unable to connect to the file service. Please check if the Azure Function is running.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting file: {fileName}");
                TempData["Error"] = $"An unexpected error occurred: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

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

        // Helper class for deserializing file metadata from Azure Function
        private class FileMetadata
        {
            public string FileName { get; set; }
            public long FileSize { get; set; }
            public string UploadedBy { get; set; }
            public DateTime UploadedOn { get; set; }
        }

        private class FileUploadResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string FileName { get; set; }
            public string OriginalFileName { get; set; }
            public long FileSize { get; set; }
            public string FileType { get; set; }
            public string Error { get; set; }
            public string UploadedBy { get; set; }
        }
    }
}