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
                _logger.LogInformation("Retrieving all files with metadata from Azure File Share");

                var shareClient = _shareServiceClient.GetShareClient(DefaultShareName);

                if (!await shareClient.ExistsAsync())
                {
                    _logger.LogWarning($"Share '{DefaultShareName}' does not exist");
                    return new OkObjectResult(new List<object>());
                }

                var directoryClient = shareClient.GetRootDirectoryClient();
                var files = new List<object>();

                await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync())
                {
                    if (!item.IsDirectory)
                    {
                        try
                        {
                            var fileClient = directoryClient.GetFileClient(item.Name);
                            var properties = await fileClient.GetPropertiesAsync();

                            // Extract metadata
                            var uploadedBy = properties.Value.Metadata.ContainsKey("UploadedBy")
                                ? properties.Value.Metadata["UploadedBy"]
                                : "Unknown";

                            var uploadedOn = properties.Value.Metadata.ContainsKey("UploadedOn")
                                ? DateTime.Parse(properties.Value.Metadata["UploadedOn"])
                                : properties.Value.LastModified.DateTime;

                            files.Add(new
                            {
                                fileName = item.Name,
                                fileSize = item.FileSize ?? 0,
                                uploadedBy = uploadedBy,
                                uploadedOn = uploadedOn
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Could not retrieve metadata for file: {item.Name}");
                            // Add file without metadata
                            files.Add(new
                            {
                                fileName = item.Name,
                                fileSize = item.FileSize ?? 0,
                                uploadedBy = "Unknown",
                                uploadedOn = DateTime.UtcNow
                            });
                        }
                    }
                }

                _logger.LogInformation($"Retrieved {files.Count} files from file share");
                return new OkObjectResult(files);
            }
            catch (Exception ex)
            {
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
        // Enhanced UploadFile function with uploader tracking
        [Function("UploadFile")]
        public async Task<IActionResult> UploadFile(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "files/upload")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("Processing file upload request");

                if (!req.HasFormContentType)
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "Request must be multipart/form-data"
                    });
                }

                var form = await req.ReadFormAsync();
                var file = form.Files.FirstOrDefault();

                // Get uploader email from form data
                var uploaderEmail = form["uploaderEmail"].ToString();
                if (string.IsNullOrEmpty(uploaderEmail))
                {
                    uploaderEmail = "Unknown";
                }

                if (file == null || file.Length == 0)
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "No file provided or file is empty"
                    });
                }

                var allowedExtensions = new[] { ".pdf", ".docx", ".txt", ".xlsx" };
                var fileExtension = Path.GetExtension(file.FileName).ToLower();

                if (!allowedExtensions.Contains(fileExtension))
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "Only PDF, DOCX, XLSX, and TXT files are allowed"
                    });
                }

                if (file.Length > 10 * 1024 * 1024)
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "File size must be less than 10MB"
                    });
                }

                _logger.LogInformation($"Uploading file: {file.FileName}, Size: {file.Length} bytes, Uploader: {uploaderEmail}");

                var shareClient = _shareServiceClient.GetShareClient(DefaultShareName);
                await shareClient.CreateIfNotExistsAsync();

                var directoryClient = shareClient.GetRootDirectoryClient();
                var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
                var fileClient = directoryClient.GetFileClient(uniqueFileName);

                using var stream = file.OpenReadStream();
                await fileClient.CreateAsync(stream.Length);

                // Set file metadata including uploader
                var metadata = new Dictionary<string, string>
        {
            { "UploadedBy", uploaderEmail },
            { "UploadedOn", DateTime.UtcNow.ToString("o") },
            { "OriginalFileName", file.FileName }
        };

                await fileClient.SetMetadataAsync(metadata);
                await fileClient.UploadAsync(stream);

                _logger.LogInformation($"File uploaded successfully: {uniqueFileName} by {uploaderEmail}");

                return new OkObjectResult(new
                {
                    success = true,
                    message = $"File '{file.FileName}' uploaded successfully",
                    fileName = uniqueFileName,
                    originalFileName = file.FileName,
                    fileSize = file.Length,
                    fileType = fileExtension.TrimStart('.'),
                    uploadedBy = uploaderEmail
                });
            }
            catch (Exception ex)
            {
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