// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2

//References:
// ClaudAI - https://claude.ai/
// ChatGPT - https://chat.openai.com/
// W3schools - https://www.w3schools.com/
// IIEVC School of Computer Science Youtube channel for Azure services setup and use https://www.youtube.com/@VCSOCS
// AzureApp project done in class with lecturer

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Controllers
{
    /// <summary>
    /// Controller responsible for managing file upload, download, and deletion operations.
    /// Communicates with Azure Functions to store and retrieve files from Azure Blob Storage.
    /// Implements file validation for security (file type and size restrictions).
    /// </summary>
    public class FileUploadController : Controller
    {
        // HTTP client for making requests to Azure Functions
        private readonly HttpClient _httpClient;

        // Logger for tracking operations and errors
        private readonly ILogger<FileUploadController> _logger;

        // Base URL for the Azure Function endpoints
        private readonly string _functionBaseUrl;

        // Authentication key for Azure Functions
        private readonly string _functionKey;

        /// <summary>
        /// HashSet containing permitted file extensions for upload.
        /// Using HashSet for O(1) lookup performance when validating file types.
        /// </summary>
        private readonly HashSet<string> _allowedExtensions = new HashSet<string>
        {
            ".pdf", ".docx", ".txt", ".xlsx"
        };

        /// <summary>
        /// Constructor that initializes the controller with required dependencies.
        /// Validates that critical Azure Function configuration exists at startup.
        /// </summary>
        /// <param name="httpClientFactory">Factory for creating HTTP clients</param>
        /// <param name="logger">Logger instance for this controller</param>
        /// <param name="configuration">Configuration to access app settings</param>
        /// <exception cref="ArgumentNullException">Thrown if logger or Azure configuration is missing</exception>
        public FileUploadController(IHttpClientFactory httpClientFactory, ILogger<FileUploadController> logger, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();

            // Ensure logger is provided (critical for debugging and monitoring)
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Get Azure Function configuration and validate it exists
            // Throw exceptions early if configuration is missing to fail fast during startup
            _functionBaseUrl = configuration["AzureFunctions:BaseUrl"]
                ?? throw new ArgumentNullException("AzureFunctions:BaseUrl configuration is missing");
            _functionKey = configuration["AzureFunctions:FunctionKey"]
                ?? throw new ArgumentNullException("AzureFunctions:FunctionKey configuration is missing");

            _logger.LogInformation($"FileUploadController initialized with base URL: {_functionBaseUrl}");
        }

        /// <summary>
        /// GET: FileUpload/Index
        /// Displays a list of all files currently stored in Azure Blob Storage.
        /// Retrieves file names from Azure Function and transforms them into FileUpload objects for display.
        /// </summary>
        /// <returns>View with list of uploaded files</returns>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index()
        {
            try
            {
                _logger.LogInformation("Retrieving all files from Azure Function API");

                // Create GET request to retrieve all file names from Azure Function
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/files");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    // Read and log the JSON response
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation($"Received response: {content}");

                    // Deserialize JSON array of file names
                    // Returns empty list if deserialization fails
                    var fileNames = JsonSerializer.Deserialize<List<string>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<string>();

                    // Transform simple string file names into FileUpload objects for richer display
                    // Includes metadata like file type, upload date, and uploader
                    var fileUploads = fileNames.Select(fileName => new FileUpload
                    {
                        FileId = Guid.NewGuid().ToString(), // Generate unique ID for display purposes
                        FileName = fileName,
                        FileUrl = fileName, // Store filename as URL reference
                        FileType = GetFileType(fileName), // Determine file type from extension
                        UploadedOn = DateTime.Now, // Current time as placeholder (actual upload time not tracked)
                        UploadedBy = "System" // Default uploader (user tracking not implemented)
                    }).ToList();

                    _logger.LogInformation($"Successfully retrieved {fileUploads.Count} files");
                    return View(fileUploads);
                }

                // Log error if the API request failed
                _logger.LogError($"Error retrieving files from API: {response.StatusCode}");
                ViewBag.Error = $"Unable to load files. Status: {response.StatusCode}";
                return View(new List<FileUpload>());
            }
            catch (HttpRequestException httpEx)
            {
                // Handle network-related errors (connection issues, timeouts, etc.)
                _logger.LogError(httpEx, "HTTP error retrieving files from Azure Function");
                ViewBag.Error = "Unable to connect to the file service. Please check if the Azure Function is running.";
                return View(new List<FileUpload>());
            }
            catch (Exception ex)
            {
                // Catch any other unexpected errors
                _logger.LogError(ex, "Error retrieving files");
                ViewBag.Error = "An unexpected error occurred while loading files.";
                return View(new List<FileUpload>());
            }
        }

        /// <summary>
        /// GET: FileUpload/Upload
        /// Displays the file upload form to the user.
        /// </summary>
        /// <returns>View with file upload form</returns>
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult Upload()
        {
            return View();
        }

        /// <summary>
        /// POST: FileUpload/Upload
        /// Processes file upload form submission with comprehensive validation.
        /// Validates file presence, type, and size before uploading to Azure Blob Storage via Azure Function.
        /// </summary>
        /// <param name="file">File uploaded from the form</param>
        /// <returns>Redirects to Index on success, returns to form on failure</returns
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken] // Protects against CSRF attacks
        public async Task<IActionResult> Upload(IFormFile file)
        {
            // Validate that a file was actually selected
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return View();
            }

            // Validate file type against whitelist for security
            // Prevents upload of potentially dangerous file types
            var fileExtension = Path.GetExtension(file.FileName).ToLower();
            if (!_allowedExtensions.Contains(fileExtension))
            {
                TempData["Error"] = "Only PDF, DOCX, XLSX, and TXT files are allowed.";
                return View();
            }

            // Validate file size to prevent resource exhaustion
            // 10MB limit protects storage costs and prevents abuse
            if (file.Length > 10 * 1024 * 1024)
            {
                TempData["Error"] = "File size must be less than 10MB.";
                return View();
            }

            try
            {
                _logger.LogInformation($"Uploading file: {file.FileName} ({file.Length} bytes)");

                // Create multipart/form-data content for file upload
                // This is required for sending files over HTTP
                using var formData = new MultipartFormDataContent();
                using var fileStream = file.OpenReadStream();

                // Wrap file stream in HTTP content with proper content type
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

                // Add file to form data with field name "file" and original filename
                formData.Add(fileContent, "file", file.FileName);

                // Create POST request to Azure Function upload endpoint
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_functionBaseUrl}/files/upload");
                request.Headers.Add("x-functions-key", _functionKey);
                request.Content = formData;

                // Send the upload request
                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation($"Upload response: {response.StatusCode} - {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    // Deserialize success response from Azure Function
                    var result = JsonSerializer.Deserialize<FileUploadResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    // Display success message (from API response or default)
                    TempData["Success"] = result?.Message ?? $"File '{file.FileName}' uploaded successfully!";
                    _logger.LogInformation($"File uploaded successfully: {file.FileName}");
                    return RedirectToAction(nameof(Index));
                }

                // Handle error response from Azure Function
                try
                {
                    // Try to deserialize error message from API response
                    var errorObj = JsonSerializer.Deserialize<FileUploadResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    TempData["Error"] = errorObj?.Message ?? "Error uploading file. Please try again.";
                }
                catch
                {
                    // If deserialization fails, use generic error message
                    TempData["Error"] = $"Error uploading file: {response.StatusCode}";
                }

                _logger.LogError($"Error uploading file: {response.StatusCode} - {responseContent}");
                return View();
            }
            catch (HttpRequestException httpEx)
            {
                // Handle network connectivity issues
                _logger.LogError(httpEx, $"HTTP error uploading file: {file.FileName}");
                TempData["Error"] = "Unable to connect to the file service. Please check if the Azure Function is running.";
                return View();
            }
            catch (Exception ex)
            {
                // Catch any unexpected errors during upload process
                _logger.LogError(ex, $"Error uploading file: {file.FileName}");
                TempData["Error"] = $"An unexpected error occurred: {ex.Message}";
                return View();
            }
        }

        /// <summary>
        /// GET: FileUpload/Download
        /// Downloads a file from Azure Blob Storage and streams it to the user's browser.
        /// Properly encodes filename to handle special characters in URLs.
        /// </summary>
        /// <param name="fileName">Name of the file to download</param>
        /// <returns>File stream for download or redirect with error message</returns>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Download(string fileName)
        {
            // Validate filename parameter
            if (string.IsNullOrEmpty(fileName))
            {
                TempData["Error"] = "File name is required.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _logger.LogInformation($"Downloading file: {fileName}");

                // URL encode the filename to handle special characters (spaces, etc.)
                var encodedFileName = Uri.EscapeDataString(fileName);

                // Create GET request to Azure Function download endpoint
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/files/download/{encodedFileName}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    // Get file stream from response
                    var fileStream = await response.Content.ReadAsStreamAsync();

                    // Determine correct MIME type based on file extension
                    var contentType = GetContentType(fileName);

                    _logger.LogInformation($"File downloaded successfully: {fileName}");

                    // Return file for download with proper content type and filename
                    return File(fileStream, contentType, fileName);
                }

                // Handle case where file doesn't exist in blob storage
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = $"File '{fileName}' not found.";
                    _logger.LogWarning($"File not found: {fileName}");
                    return RedirectToAction(nameof(Index));
                }

                // Log any other error responses
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Error downloading file: {response.StatusCode} - {errorContent}");
                TempData["Error"] = $"Error downloading file: {response.StatusCode}";
                return RedirectToAction(nameof(Index));
            }
            catch (HttpRequestException httpEx)
            {
                // Handle network connectivity issues
                _logger.LogError(httpEx, $"HTTP error downloading file: {fileName}");
                TempData["Error"] = "Unable to connect to the file service. Please check if the Azure Function is running.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                // Catch any unexpected errors during download
                _logger.LogError(ex, $"Error downloading file: {fileName}");
                TempData["Error"] = $"An unexpected error occurred: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// POST: FileUpload/Delete
        /// Deletes a file from Azure Blob Storage via Azure Function.
        /// Properly encodes filename to handle special characters in URLs.
        /// </summary>
        /// <param name="fileName">Name of the file to delete</param>
        /// <returns>Redirects to Index with success or error message</returns>
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken] // Protects against CSRF attacks
        public async Task<IActionResult> Delete(string fileName)
        {
            // Validate filename parameter
            if (string.IsNullOrEmpty(fileName))
            {
                TempData["Error"] = "File name is required.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _logger.LogInformation($"Deleting file: {fileName}");

                // URL encode the filename to handle special characters
                var encodedFileName = Uri.EscapeDataString(fileName);

                // Create DELETE request to Azure Function delete endpoint
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{_functionBaseUrl}/files/{encodedFileName}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation($"Delete response: {response.StatusCode} - {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    // File successfully deleted from blob storage
                    TempData["Success"] = $"File '{fileName}' deleted successfully!";
                    _logger.LogInformation($"File deleted successfully: {fileName}");
                    return RedirectToAction(nameof(Index));
                }

                // Handle case where file doesn't exist or was already deleted
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = $"File '{fileName}' not found or could not be deleted.";
                    _logger.LogWarning($"File not found for deletion: {fileName}");
                    return RedirectToAction(nameof(Index));
                }

                // Log any other error responses
                _logger.LogError($"Error deleting file: {response.StatusCode} - {responseContent}");
                TempData["Error"] = $"Error deleting file: {response.StatusCode}";
                return RedirectToAction(nameof(Index));
            }
            catch (HttpRequestException httpEx)
            {
                // Handle network connectivity issues
                _logger.LogError(httpEx, $"HTTP error deleting file: {fileName}");
                TempData["Error"] = "Unable to connect to the file service. Please check if the Azure Function is running.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                // Catch any unexpected errors during deletion
                _logger.LogError(ex, $"Error deleting file: {fileName}");
                TempData["Error"] = $"An unexpected error occurred: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// Helper method to determine user-friendly file type label from file extension.
        /// Used for display purposes in the UI.
        /// </summary>
        /// <param name="fileName">Full filename with extension</param>
        /// <returns>Uppercase file type string (PDF, DOCX, TXT, XLSX, or Unknown)</returns>
        private string GetFileType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension switch
            {
                ".pdf" => "PDF",
                ".docx" => "DOCX",
                ".txt" => "TXT",
                ".xlsx" => "XLSX",
                _ => "Unknown" // Fallback for any unrecognized extensions
            };
        }

        /// <summary>
        /// Helper method to determine the correct MIME content type from file extension.
        /// Essential for proper file download behavior in browsers.
        /// </summary>
        /// <param name="fileName">Full filename with extension</param>
        /// <returns>MIME type string for HTTP Content-Type header</returns>
        private string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".txt" => "text/plain",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream" // Generic binary stream for unknown types
            };
        }

        /// <summary>
        /// Internal model class for deserializing JSON responses from Azure Function.
        /// Maps to the response structure returned by the file upload/delete Azure Function endpoints.
        /// </summary>
        private class FileUploadResponse
        {
            // Indicates whether the operation was successful
            public bool Success { get; set; }

            // Human-readable message describing the operation result
            public string Message { get; set; }

            // Name of the file as stored in blob storage
            public string FileName { get; set; }

            // Original filename before any processing
            public string OriginalFileName { get; set; }

            // Size of the uploaded file in bytes
            public long FileSize { get; set; }

            // Type/extension of the file
            public string FileType { get; set; }

            // Error message if operation failed
            public string Error { get; set; }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//