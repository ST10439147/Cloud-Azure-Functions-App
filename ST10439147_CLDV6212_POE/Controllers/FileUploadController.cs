// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - File Upload Controller

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Controllers
{
    /// <summary>
    /// Controller responsible for managing file uploads, downloads, and deletions.
    /// Communicates with Azure Functions to handle file storage operations in Azure Blob Storage.
    /// All operations are restricted to Admin users only.
    /// </summary>
    public class FileUploadController : Controller
    {
        // Private fields for dependency injection and configuration
        private readonly HttpClient _httpClient;
        private readonly ILogger<FileUploadController> _logger;
        private readonly string _functionBaseUrl;
        private readonly string _functionKey;

        /// <summary>
        /// Set of allowed file extensions for upload validation.
        /// Restricts uploads to document types only for security purposes.
        /// </summary>
        private readonly HashSet<string> _allowedExtensions = new HashSet<string>
        {
            ".pdf", ".docx", ".txt", ".xlsx"
        };

        /// <summary>
        /// Initializes a new instance of the FileUploadController with required services and configuration.
        /// </summary>
        /// <param name="httpClientFactory">Factory for creating HTTP clients to communicate with Azure Functions</param>
        /// <param name="logger">Logger for recording application events and errors</param>
        /// <param name="configuration">Configuration provider for accessing Azure Function settings</param>
        /// <exception cref="ArgumentNullException">Thrown when required configuration values are missing</exception>
        public FileUploadController(IHttpClientFactory httpClientFactory, ILogger<FileUploadController> logger, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Retrieve Azure Function base URL from configuration
            _functionBaseUrl = configuration["AzureFunctions:BaseUrl"]
                ?? throw new ArgumentNullException("AzureFunctions:BaseUrl configuration is missing");

            // Retrieve Azure Function authentication key from configuration
            _functionKey = configuration["AzureFunctions:FunctionKey"]
                ?? throw new ArgumentNullException("AzureFunctions:FunctionKey configuration is missing");

            _logger.LogInformation($"FileUploadController initialized with base URL: {_functionBaseUrl}");
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays a list of all uploaded files stored in Azure Blob Storage.
        /// Retrieves file metadata from Azure Function API and displays it in a table format.
        /// Supports both detailed metadata and simple file name list formats for backward compatibility.
        /// </summary>
        /// <returns>View with list of FileUpload objects, or empty list on error</returns>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index()
        {
            try
            {
                _logger.LogInformation("Retrieving all files from Azure Function API");

                // Get current user's email for display purposes when metadata is unavailable
                var currentUserEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "Unknown";

                // Create HTTP request to retrieve files from Azure Function
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/files");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation($"Received response: {content}");

                    // Attempt to deserialize as list of file metadata objects (new format with full details)
                    try
                    {
                        var fileDataList = JsonSerializer.Deserialize<List<FileMetadata>>(content, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        // If successful, map metadata to FileUpload view models
                        if (fileDataList != null && fileDataList.Any())
                        {
                            var fileUploads = fileDataList.Select(data => new FileUpload
                            {
                                FileId = Guid.NewGuid().ToString(),           // Generate unique ID for UI purposes
                                FileName = data.FileName ?? "Unknown",
                                FileUrl = data.FileName ?? "",                // File URL is the blob name
                                FileType = GetFileType(data.FileName ?? ""),  // Determine file type from extension
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
                        // If deserialization fails, fall back to simple format (backward compatibility)
                        _logger.LogInformation("Falling back to simple file name list format");
                    }

                    // Fallback: deserialize as simple list of file names (old format without metadata)
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
                        UploadedOn = DateTime.Now,              // Use current time as fallback
                        UploadedBy = currentUserEmail           // Show current user as fallback
                    }).ToList();

                    _logger.LogInformation($"Successfully retrieved {simpleFileUploads.Count} files (simple format)");
                    return View(simpleFileUploads);
                }

                // Handle unsuccessful API response
                _logger.LogError($"Error retrieving files from API: {response.StatusCode}");
                ViewBag.Error = $"Unable to load files. Status: {response.StatusCode}";
                return View(new List<FileUpload>());
            }
            catch (HttpRequestException httpEx)
            {
                // Handle network or connection errors
                _logger.LogError(httpEx, "HTTP error retrieving files from Azure Function");
                ViewBag.Error = "Unable to connect to the file service. Please check if the Azure Function is running.";
                return View(new List<FileUpload>());
            }
            catch (Exception ex)
            {
                // Handle unexpected errors
                _logger.LogError(ex, "Error retrieving files");
                ViewBag.Error = "An unexpected error occurred while loading files.";
                return View(new List<FileUpload>());
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays the file upload form.
        /// </summary>
        /// <returns>Upload view for file selection</returns>
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult Upload()
        {
            return View();
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Processes file upload to Azure Blob Storage via Azure Function.
        /// Validates file type, size, and uploads with user metadata.
        /// </summary>
        /// <param name="file">The file to upload from the form</param>
        /// <returns>Redirect to Index on success, or Upload view with error message on failure</returns>
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            // Validate that a file was selected
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return View();
            }

            // Validate file extension against allowed types
            var fileExtension = Path.GetExtension(file.FileName).ToLower();
            if (!_allowedExtensions.Contains(fileExtension))
            {
                TempData["Error"] = "Only PDF, DOCX, XLSX, and TXT files are allowed.";
                return View();
            }

            // Validate file size (10MB maximum)
            if (file.Length > 10 * 1024 * 1024)
            {
                TempData["Error"] = "File size must be less than 10MB.";
                return View();
            }

            try
            {
                // Get the logged-in user's email to track who uploaded the file
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "Unknown";

                _logger.LogInformation($"Uploading file: {file.FileName} ({file.Length} bytes) by {userEmail}");

                // Create multipart form data for file upload
                using var formData = new MultipartFormDataContent();
                using var fileStream = file.OpenReadStream();

                // Add file content to form data
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                formData.Add(fileContent, "file", file.FileName);

                // Add uploader email to form data so Azure Function can store it with file metadata
                formData.Add(new StringContent(userEmail), "uploaderEmail");

                // Create HTTP request to upload file to Azure Function
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_functionBaseUrl}/files/upload");
                request.Headers.Add("x-functions-key", _functionKey);
                request.Content = formData;

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation($"Upload response: {response.StatusCode} - {responseContent}");

                // Handle successful upload
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

                // Handle upload failure - attempt to parse error message from response
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
                    // If error response can't be parsed, show generic error with status code
                    TempData["Error"] = $"Error uploading file: {response.StatusCode}";
                }

                _logger.LogError($"Error uploading file: {response.StatusCode} - {responseContent}");
                return View();
            }
            catch (HttpRequestException httpEx)
            {
                // Handle network or connection errors
                _logger.LogError(httpEx, $"HTTP error uploading file: {file.FileName}");
                TempData["Error"] = "Unable to connect to the file service. Please check if the Azure Function is running.";
                return View();
            }
            catch (Exception ex)
            {
                // Handle unexpected errors
                _logger.LogError(ex, $"Error uploading file: {file.FileName}");
                TempData["Error"] = $"An unexpected error occurred: {ex.Message}";
                return View();
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Downloads a file from Azure Blob Storage via Azure Function.
        /// Returns the file with appropriate content type for browser download.
        /// </summary>
        /// <param name="fileName">Name of the file to download</param>
        /// <returns>File stream for download, or redirect to Index with error message on failure</returns>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Download(string fileName)
        {
            // Validate file name parameter
            if (string.IsNullOrEmpty(fileName))
            {
                TempData["Error"] = "File name is required.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _logger.LogInformation($"Downloading file: {fileName}");

                // URL encode file name to handle special characters
                var encodedFileName = Uri.EscapeDataString(fileName);
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/files/download/{encodedFileName}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                // Handle successful download
                if (response.IsSuccessStatusCode)
                {
                    var fileStream = await response.Content.ReadAsStreamAsync();
                    var contentType = GetContentType(fileName);

                    _logger.LogInformation($"File downloaded successfully: {fileName}");
                    return File(fileStream, contentType, fileName);
                }

                // Handle file not found error
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = $"File '{fileName}' not found.";
                    _logger.LogWarning($"File not found: {fileName}");
                    return RedirectToAction(nameof(Index));
                }

                // Handle other errors
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Error downloading file: {response.StatusCode} - {errorContent}");
                TempData["Error"] = $"Error downloading file: {response.StatusCode}";
                return RedirectToAction(nameof(Index));
            }
            catch (HttpRequestException httpEx)
            {
                // Handle network or connection errors
                _logger.LogError(httpEx, $"HTTP error downloading file: {fileName}");
                TempData["Error"] = "Unable to connect to the file service. Please check if the Azure Function is running.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                // Handle unexpected errors
                _logger.LogError(ex, $"Error downloading file: {fileName}");
                TempData["Error"] = $"An unexpected error occurred: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Deletes a file from Azure Blob Storage via Azure Function.
        /// Permanently removes the file and its metadata.
        /// </summary>
        /// <param name="fileName">Name of the file to delete</param>
        /// <returns>Redirect to Index with success or error message</returns>
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string fileName)
        {
            // Validate file name parameter
            if (string.IsNullOrEmpty(fileName))
            {
                TempData["Error"] = "File name is required.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _logger.LogInformation($"Deleting file: {fileName}");

                // URL encode file name to handle special characters
                var encodedFileName = Uri.EscapeDataString(fileName);
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{_functionBaseUrl}/files/{encodedFileName}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation($"Delete response: {response.StatusCode} - {responseContent}");

                // Handle successful deletion
                if (response.IsSuccessStatusCode)
                {
                    TempData["Success"] = $"File '{fileName}' deleted successfully!";
                    _logger.LogInformation($"File deleted successfully: {fileName}");
                    return RedirectToAction(nameof(Index));
                }

                // Handle file not found error
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = $"File '{fileName}' not found or could not be deleted.";
                    _logger.LogWarning($"File not found for deletion: {fileName}");
                    return RedirectToAction(nameof(Index));
                }

                // Handle other errors
                _logger.LogError($"Error deleting file: {response.StatusCode} - {responseContent}");
                TempData["Error"] = $"Error deleting file: {response.StatusCode}";
                return RedirectToAction(nameof(Index));
            }
            catch (HttpRequestException httpEx)
            {
                // Handle network or connection errors
                _logger.LogError(httpEx, $"HTTP error deleting file: {fileName}");
                TempData["Error"] = "Unable to connect to the file service. Please check if the Azure Function is running.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                // Handle unexpected errors
                _logger.LogError(ex, $"Error deleting file: {fileName}");
                TempData["Error"] = $"An unexpected error occurred: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Determines the file type display name based on file extension.
        /// Used for displaying file type in the UI.
        /// </summary>
        /// <param name="fileName">Name of the file including extension</param>
        /// <returns>Display-friendly file type string (e.g., "PDF", "DOCX")</returns>
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
        /// <summary>
        /// Determines the MIME content type based on file extension.
        /// Used for setting proper Content-Type header when downloading files.
        /// </summary>
        /// <param name="fileName">Name of the file including extension</param>
        /// <returns>MIME type string (e.g., "application/pdf")</returns>
        private string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".txt" => "text/plain",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"  // Default binary content type for unknown files
            };
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Helper class for deserializing file metadata returned by Azure Function.
        /// Contains detailed information about uploaded files including size and uploader.
        /// </summary>
        private class FileMetadata
        {
            /// <summary>Gets or sets the name of the file including extension.</summary>
            public string FileName { get; set; }

            /// <summary>Gets or sets the file size in bytes.</summary>
            public long FileSize { get; set; }

            /// <summary>Gets or sets the email address of the user who uploaded the file.</summary>
            public string UploadedBy { get; set; }

            /// <summary>Gets or sets the date and time when the file was uploaded.</summary>
            public DateTime UploadedOn { get; set; }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Helper class for deserializing Azure Function API responses.
        /// Contains response status, messages, and file information.
        /// </summary>
        private class FileUploadResponse
        {
            /// <summary>Gets or sets whether the operation was successful.</summary>
            public bool Success { get; set; }

            /// <summary>Gets or sets the response message.</summary>
            public string Message { get; set; }

            /// <summary>Gets or sets the stored file name (may be different from original if sanitized).</summary>
            public string FileName { get; set; }

            /// <summary>Gets or sets the original file name provided by the user.</summary>
            public string OriginalFileName { get; set; }

            /// <summary>Gets or sets the file size in bytes.</summary>
            public long FileSize { get; set; }

            /// <summary>Gets or sets the file type/extension.</summary>
            public string FileType { get; set; }

            /// <summary>Gets or sets the error message if operation failed.</summary>
            public string Error { get; set; }

            /// <summary>Gets or sets the email address of the user who uploaded the file.</summary>
            public string UploadedBy { get; set; }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//