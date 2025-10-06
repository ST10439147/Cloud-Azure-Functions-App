// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Storage.Files.Shares;
using System.Net;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Functions
{
    /// <summary>
    /// Azure Function class that handles HTTP-triggered file operations for Azure File Share.
    /// This class provides RESTful API endpoints for uploading, downloading, listing, and deleting files
    /// stored in Azure File Share storage.
    /// </summary>
    public class FileShareFunction
    {
        private readonly ILogger<FileShareFunction> _logger;
        private readonly string _connectionString;
        private readonly ShareServiceClient _shareServiceClient;
        private const string DefaultShareName = "dummycontracts";

        /// <summary>
        /// Constructor that initializes the FileShareFunction with logging and Azure File Share connection.
        /// </summary>
        /// <param name="logger">Logger for tracking function execution and debugging</param>
        public FileShareFunction(ILogger<FileShareFunction> logger)
        {
            _logger = logger;
            // Retrieve Azure Storage connection string from environment variables
            _connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            // Initialize the Azure File Share service client for managing file operations
            _shareServiceClient = new ShareServiceClient(_connectionString);
        }

        /// <summary>
        /// HTTP GET endpoint to retrieve a list of all files in the Azure File Share.
        /// Route: GET /api/files
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <returns>List of file names in the file share or error message</returns>
        [Function("GetFiles")]
        public async Task<IActionResult> GetFiles(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "files")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("Retrieving all files from Azure File Share");

                // Get a client for the specific file share
                var shareClient = _shareServiceClient.GetShareClient(DefaultShareName);

                // Check if the file share exists before attempting to list files
                if (!await shareClient.ExistsAsync())
                {
                    _logger.LogWarning($"Share '{DefaultShareName}' does not exist");
                    // Return empty list if share doesn't exist (rather than error)
                    return new OkObjectResult(new List<string>());
                }

                // Get the root directory client to access files
                var directoryClient = shareClient.GetRootDirectoryClient();
                var files = new List<string>();

                // Iterate through all items in the root directory
                await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync())
                {
                    // Only add files to the list (exclude directories)
                    if (!item.IsDirectory)
                    {
                        files.Add(item.Name);
                    }
                }

                _logger.LogInformation($"Retrieved {files.Count} files from file share");
                return new OkObjectResult(files);
            }
            catch (Exception ex)
            {
                // Log the error and return a 500 Internal Server Error response
                _logger.LogError(ex, "Error retrieving files from file share");
                return new ObjectResult(new { error = "Error retrieving files", message = ex.Message })
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
        }

        /// <summary>
        /// HTTP POST endpoint to upload a file to the Azure File Share.
        /// Route: POST /api/files/upload
        /// Accepts multipart/form-data with file attachment.
        /// </summary>
        /// <param name="req">HTTP request containing the file as form data</param>
        /// <returns>Upload success response with file details or error message</returns>
        [Function("UploadFile")]
        public async Task<IActionResult> UploadFile(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "files/upload")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("Processing file upload request");

                // Validate that the request contains form data (required for file uploads)
                if (!req.HasFormContentType)
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "Request must be multipart/form-data"
                    });
                }

                // Read the form data from the request
                var form = await req.ReadFormAsync();
                // Get the first file from the form (if multiple files, only first is processed)
                var file = form.Files.FirstOrDefault();

                // Validate that a file was provided and has content
                if (file == null || file.Length == 0)
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "No file provided or file is empty"
                    });
                }

                // Define allowed file extensions for security
                var allowedExtensions = new[] { ".pdf", ".docx", ".txt", ".xlsx" };
                var fileExtension = Path.GetExtension(file.FileName).ToLower();

                // Validate that the file type is allowed
                if (!allowedExtensions.Contains(fileExtension))
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "Only PDF, DOCX, XLSX, and TXT files are allowed"
                    });
                }

                // Validate file size (max 10MB to prevent excessive storage usage)
                if (file.Length > 10 * 1024 * 1024)
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "File size must be less than 10MB"
                    });
                }

                _logger.LogInformation($"Uploading file: {file.FileName}, Size: {file.Length} bytes");

                // Get the file share client and create the share if it doesn't exist
                var shareClient = _shareServiceClient.GetShareClient(DefaultShareName);
                await shareClient.CreateIfNotExistsAsync();

                // Get the root directory client for file operations
                var directoryClient = shareClient.GetRootDirectoryClient();

                // Generate a unique filename to prevent overwriting and conflicts
                var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
                var fileClient = directoryClient.GetFileClient(uniqueFileName);

                // Upload the file in two steps: create file entry, then upload content
                using var stream = file.OpenReadStream();
                await fileClient.CreateAsync(stream.Length); // Allocate space for the file
                await fileClient.UploadAsync(stream); // Upload the actual file content

                _logger.LogInformation($"File uploaded successfully: {uniqueFileName}");

                // Return success response with file metadata
                return new OkObjectResult(new
                {
                    success = true,
                    message = $"File '{file.FileName}' uploaded successfully",
                    fileName = uniqueFileName, // Server-side unique name
                    originalFileName = file.FileName, // Original name from client
                    fileSize = file.Length,
                    fileType = fileExtension.TrimStart('.') // Extension without dot
                });
            }
            catch (Exception ex)
            {
                // Log the error and return a 500 Internal Server Error response
                _logger.LogError(ex, "Error uploading file to file share");
                return new ObjectResult(new
                {
                    success = false,
                    message = "Error uploading file",
                    error = ex.Message
                })
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
        }

        /// <summary>
        /// HTTP GET endpoint to download a specific file from the Azure File Share.
        /// Route: GET /api/files/download/{fileName}
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <param name="fileName">Name of the file to download (from route parameter)</param>
        /// <returns>File stream with appropriate content type or error message</returns>
        [Function("DownloadFile")]
        public async Task<IActionResult> DownloadFile(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "files/download/{fileName}")] HttpRequest req,
            string fileName)
        {
            try
            {
                // Validate that a filename was provided
                if (string.IsNullOrEmpty(fileName))
                {
                    return new BadRequestObjectResult(new { message = "File name is required" });
                }

                _logger.LogInformation($"Downloading file: {fileName}");

                // Get the file share client
                var shareClient = _shareServiceClient.GetShareClient(DefaultShareName);

                // Check if the file share exists
                if (!await shareClient.ExistsAsync())
                {
                    return new NotFoundObjectResult(new { message = $"Share '{DefaultShareName}' not found" });
                }

                // Get the root directory and file clients
                var directoryClient = shareClient.GetRootDirectoryClient();
                var fileClient = directoryClient.GetFileClient(fileName);

                // Check if the specific file exists before attempting download
                if (!await fileClient.ExistsAsync())
                {
                    _logger.LogWarning($"File not found: {fileName}");
                    return new NotFoundObjectResult(new { message = $"File '{fileName}' not found" });
                }

                // Download the file content as a stream
                var download = await fileClient.DownloadAsync();
                var fileStream = download.Value.Content;

                // Determine the appropriate MIME type based on file extension
                var contentType = GetContentType(fileName);

                _logger.LogInformation($"File downloaded successfully: {fileName}");

                // Return the file stream with proper content type and download name
                return new FileStreamResult(fileStream, contentType)
                {
                    FileDownloadName = fileName // Suggests filename for browser download
                };
            }
            catch (Exception ex)
            {
                // Log the error and return a 500 Internal Server Error response
                _logger.LogError(ex, $"Error downloading file: {fileName}");
                return new ObjectResult(new { error = "Error downloading file", message = ex.Message })
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
        }

        /// <summary>
        /// HTTP DELETE endpoint to remove a file from the Azure File Share.
        /// Route: DELETE /api/files/{fileName}
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <param name="fileName">Name of the file to delete (from route parameter)</param>
        /// <returns>Success message or error response if file not found</returns>
        [Function("DeleteFile")]
        public async Task<IActionResult> DeleteFile(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "files/{fileName}")] HttpRequest req,
            string fileName)
        {
            try
            {
                // Validate that a filename was provided
                if (string.IsNullOrEmpty(fileName))
                {
                    return new BadRequestObjectResult(new { message = "File name is required" });
                }

                _logger.LogInformation($"Deleting file: {fileName}");

                // Get the file share client
                var shareClient = _shareServiceClient.GetShareClient(DefaultShareName);

                // Check if the file share exists
                if (!await shareClient.ExistsAsync())
                {
                    return new NotFoundObjectResult(new { message = $"Share '{DefaultShareName}' not found" });
                }

                // Get the root directory and file clients
                var directoryClient = shareClient.GetRootDirectoryClient();
                var fileClient = directoryClient.GetFileClient(fileName);

                // Attempt to delete the file (returns false if file doesn't exist)
                var response = await fileClient.DeleteIfExistsAsync();

                // Check if deletion was successful
                if (response.Value)
                {
                    _logger.LogInformation($"File deleted successfully: {fileName}");
                    return new OkObjectResult(new
                    {
                        success = true,
                        message = $"File '{fileName}' deleted successfully"
                    });
                }
                else
                {
                    // File didn't exist - return 404 Not Found
                    _logger.LogWarning($"File not found for deletion: {fileName}");
                    return new NotFoundObjectResult(new
                    {
                        success = false,
                        message = $"File '{fileName}' not found"
                    });
                }
            }
            catch (Exception ex)
            {
                // Log the error and return a 500 Internal Server Error response
                _logger.LogError(ex, $"Error deleting file: {fileName}");
                return new ObjectResult(new
                {
                    success = false,
                    error = "Error deleting file",
                    message = ex.Message
                })
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
        }

        /// <summary>
        /// Helper method to determine the correct MIME content type based on file extension.
        /// Used for setting proper Content-Type headers when downloading files.
        /// </summary>
        /// <param name="fileName">Name of the file including extension</param>
        /// <returns>MIME content type string (e.g., "application/pdf")</returns>
        private string GetContentType(string fileName)
        {
            // Extract and normalize the file extension
            var extension = Path.GetExtension(fileName).ToLower();

            // Match extension to appropriate MIME type
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".txt" => "text/plain",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream" // Generic binary stream for unknown types
            };
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//