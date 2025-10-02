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
    public class FileShareFunction
    {
        private readonly ILogger<FileShareFunction> _logger;
        private readonly string _connectionString;
        private readonly ShareServiceClient _shareServiceClient;
        private const string DefaultShareName = "dummycontracts";

        public FileShareFunction(ILogger<FileShareFunction> logger)
        {
            _logger = logger;
            _connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            _shareServiceClient = new ShareServiceClient(_connectionString);
        }

        // GET: /api/files - Get all files from the file share
        [Function("GetFiles")]
        public async Task<IActionResult> GetFiles(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "files")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("Retrieving all files from Azure File Share");

                var shareClient = _shareServiceClient.GetShareClient(DefaultShareName);

                // Check if share exists
                if (!await shareClient.ExistsAsync())
                {
                    _logger.LogWarning($"Share '{DefaultShareName}' does not exist");
                    return new OkObjectResult(new List<string>());
                }

                var directoryClient = shareClient.GetRootDirectoryClient();
                var files = new List<string>();

                await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync())
                {
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
                _logger.LogError(ex, "Error retrieving files from file share");
                return new ObjectResult(new { error = "Error retrieving files", message = ex.Message })
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
        }

        // POST: /api/files/upload - Upload a file to the file share
        [Function("UploadFile")]
        public async Task<IActionResult> UploadFile(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "files/upload")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("Processing file upload request");

                // Check if the request contains multipart/form-data
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

                if (file == null || file.Length == 0)
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "No file provided or file is empty"
                    });
                }

                // Validate file type
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

                // Validate file size (max 10MB)
                if (file.Length > 10 * 1024 * 1024)
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "File size must be less than 10MB"
                    });
                }

                _logger.LogInformation($"Uploading file: {file.FileName}, Size: {file.Length} bytes");

                // Get or create the share
                var shareClient = _shareServiceClient.GetShareClient(DefaultShareName);
                await shareClient.CreateIfNotExistsAsync();

                var directoryClient = shareClient.GetRootDirectoryClient();

                // Generate unique filename
                var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
                var fileClient = directoryClient.GetFileClient(uniqueFileName);

                // Upload the file
                using var stream = file.OpenReadStream();
                await fileClient.CreateAsync(stream.Length);
                await fileClient.UploadAsync(stream);

                _logger.LogInformation($"File uploaded successfully: {uniqueFileName}");

                return new OkObjectResult(new
                {
                    success = true,
                    message = $"File '{file.FileName}' uploaded successfully",
                    fileName = uniqueFileName,
                    originalFileName = file.FileName,
                    fileSize = file.Length,
                    fileType = fileExtension.TrimStart('.')
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

        // GET: /api/files/download/{fileName} - Download a file from the file share
        [Function("DownloadFile")]
        public async Task<IActionResult> DownloadFile(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "files/download/{fileName}")] HttpRequest req,
            string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    return new BadRequestObjectResult(new { message = "File name is required" });
                }

                _logger.LogInformation($"Downloading file: {fileName}");

                var shareClient = _shareServiceClient.GetShareClient(DefaultShareName);

                if (!await shareClient.ExistsAsync())
                {
                    return new NotFoundObjectResult(new { message = $"Share '{DefaultShareName}' not found" });
                }

                var directoryClient = shareClient.GetRootDirectoryClient();
                var fileClient = directoryClient.GetFileClient(fileName);

                // Check if file exists
                if (!await fileClient.ExistsAsync())
                {
                    _logger.LogWarning($"File not found: {fileName}");
                    return new NotFoundObjectResult(new { message = $"File '{fileName}' not found" });
                }

                // Download the file
                var download = await fileClient.DownloadAsync();
                var fileStream = download.Value.Content;

                // Determine content type
                var contentType = GetContentType(fileName);

                _logger.LogInformation($"File downloaded successfully: {fileName}");

                return new FileStreamResult(fileStream, contentType)
                {
                    FileDownloadName = fileName
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading file: {fileName}");
                return new ObjectResult(new { error = "Error downloading file", message = ex.Message })
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
        }

        // DELETE: /api/files/{fileName} - Delete a file from the file share
        [Function("DeleteFile")]
        public async Task<IActionResult> DeleteFile(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "files/{fileName}")] HttpRequest req,
            string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    return new BadRequestObjectResult(new { message = "File name is required" });
                }

                _logger.LogInformation($"Deleting file: {fileName}");

                var shareClient = _shareServiceClient.GetShareClient(DefaultShareName);

                if (!await shareClient.ExistsAsync())
                {
                    return new NotFoundObjectResult(new { message = $"Share '{DefaultShareName}' not found" });
                }

                var directoryClient = shareClient.GetRootDirectoryClient();
                var fileClient = directoryClient.GetFileClient(fileName);

                // Delete the file
                var response = await fileClient.DeleteIfExistsAsync();

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

        // Helper method to get content type based on file extension
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