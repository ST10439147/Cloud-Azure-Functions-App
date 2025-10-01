// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2

// References:
// ClaudAI - https://claude.ai/
// Microsoft Azure Functions Documentation - https://docs.microsoft.com/en-us/azure/azure-functions/

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Azure.Storage.Files.Shares;

namespace ST10439147_CLDV6212_POE.Functions
{
    public static class FileUploadFunction
    {
        // Allowed file extensions
        private static readonly HashSet<string> AllowedExtensions = new HashSet<string>
        {
            ".pdf", ".docx", ".txt", ".xlsx"
        };

        // Maximum file size (10MB)
        private const long MaxFileSize = 10 * 1024 * 1024;
        private const string ShareName = "dummycontracts";

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Azure Function to get all files from Azure File Share
        /// HTTP GET endpoint
        /// </summary>
        [FunctionName("GetAllFiles")]
        public static async Task<IActionResult> GetAllFiles(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "files")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Get all files function triggered.");

            try
            {
                var connectionString = Environment.GetEnvironmentVariable("AzureStorage:ConnectionString");
                if (string.IsNullOrEmpty(connectionString))
                {
                    log.LogError("Azure Storage connection string not configured.");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }

                var fileNames = await GetAllFilesFromShare(connectionString, log);

                return new OkObjectResult(fileNames);
            }
            catch (Exception ex)
            {
                log.LogError($"Error retrieving files: {ex.Message}");
                return new ObjectResult(new
                {
                    success = false,
                    message = $"Error retrieving files: {ex.Message}"
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Azure Function to upload a file to Azure File Share
        /// HTTP POST endpoint that accepts multipart/form-data with a file
        /// </summary>
        [FunctionName("UploadFile")]
        public static async Task<IActionResult> UploadFile(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "files/upload")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("File upload function triggered.");

            try
            {
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

                // Validate file presence
                if (file == null || file.Length == 0)
                {
                    log.LogWarning("No file provided in request.");
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "Please select a file to upload."
                    });
                }

                // Validate file type
                var fileExtension = Path.GetExtension(file.FileName).ToLower();
                if (!AllowedExtensions.Contains(fileExtension))
                {
                    log.LogWarning($"Invalid file type attempted: {fileExtension}");
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "Only PDF, DOCX, XLSX, and TXT files are allowed."
                    });
                }

                // Validate file size
                if (file.Length > MaxFileSize)
                {
                    log.LogWarning($"File too large: {file.Length} bytes");
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "File size must be less than 10MB."
                    });
                }

                // Get connection string from environment variables
                var connectionString = Environment.GetEnvironmentVariable("AzureStorage:ConnectionString");
                if (string.IsNullOrEmpty(connectionString))
                {
                    log.LogError("Azure Storage connection string not configured.");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }

                // Upload file to Azure File Share
                var fileName = await UploadToAzureFileShare(file, connectionString, log);

                log.LogInformation($"File uploaded successfully: {fileName}");

                return new OkObjectResult(new
                {
                    success = true,
                    message = $"File {fileName} uploaded successfully!",
                    fileName = fileName,
                    originalFileName = file.FileName,
                    fileSize = file.Length,
                    fileType = GetFileType(fileExtension)
                });
            }
            catch (Exception ex)
            {
                log.LogError($"Error uploading file: {ex.Message}");
                return new ObjectResult(new
                {
                    success = false,
                    message = $"Error uploading file: {ex.Message}"
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Azure Function to download a file from Azure File Share
        /// HTTP GET endpoint
        /// </summary>
        [FunctionName("DownloadFile")]
        public static async Task<IActionResult> DownloadFile(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "files/download/{fileName}")] HttpRequest req,
            string fileName,
            ILogger log)
        {
            log.LogInformation($"File download function triggered for: {fileName}");

            try
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "File name is required."
                    });
                }

                var connectionString = Environment.GetEnvironmentVariable("AzureStorage:ConnectionString");
                if (string.IsNullOrEmpty(connectionString))
                {
                    log.LogError("Azure Storage connection string not configured.");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }

                var fileStream = await DownloadFromAzureFileShare(fileName, connectionString, log);

                if (fileStream == null)
                {
                    return new NotFoundObjectResult(new
                    {
                        success = false,
                        message = $"File {fileName} not found."
                    });
                }

                var contentType = GetContentType(Path.GetExtension(fileName));
                return new FileStreamResult(fileStream, contentType)
                {
                    FileDownloadName = fileName
                };
            }
            catch (Exception ex)
            {
                log.LogError($"Error downloading file: {ex.Message}");
                return new ObjectResult(new
                {
                    success = false,
                    message = $"Error downloading file: {ex.Message}"
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Azure Function to delete a file from Azure File Share
        /// HTTP DELETE endpoint
        /// </summary>
        [FunctionName("DeleteFile")]
        public static async Task<IActionResult> DeleteFile(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "files/{fileName}")] HttpRequest req,
            string fileName,
            ILogger log)
        {
            log.LogInformation($"File delete function triggered for: {fileName}");

            try
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "File name is required."
                    });
                }

                var connectionString = Environment.GetEnvironmentVariable("AzureStorage:ConnectionString");
                if (string.IsNullOrEmpty(connectionString))
                {
                    log.LogError("Azure Storage connection string not configured.");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }

                var deleted = await DeleteFromAzureFileShare(fileName, connectionString, log);

                if (deleted)
                {
                    log.LogInformation($"File deleted successfully: {fileName}");
                    return new OkObjectResult(new
                    {
                        success = true,
                        message = $"File {fileName} deleted successfully!"
                    });
                }
                else
                {
                    return new NotFoundObjectResult(new
                    {
                        success = false,
                        message = $"File {fileName} not found or could not be deleted."
                    });
                }
            }
            catch (Exception ex)
            {
                log.LogError($"Error deleting file: {ex.Message}");
                return new ObjectResult(new
                {
                    success = false,
                    message = $"Error deleting file: {ex.Message}"
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        #region Helper Methods

        /// <summary>
        /// Gets all files from Azure File Share
        /// </summary>
        private static async Task<List<string>> GetAllFilesFromShare(string connectionString, ILogger log)
        {
            var fileNames = new List<string>();

            var shareServiceClient = new ShareServiceClient(connectionString);
            var shareClient = shareServiceClient.GetShareClient(ShareName);

            // Check if share exists
            if (!await shareClient.ExistsAsync())
            {
                log.LogWarning($"Share {ShareName} does not exist.");
                return fileNames;
            }

            var directoryClient = shareClient.GetRootDirectoryClient();

            await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync())
            {
                if (!item.IsDirectory)
                {
                    fileNames.Add(item.Name);
                }
            }

            log.LogInformation($"Retrieved {fileNames.Count} files from share.");
            return fileNames;
        }

        /// <summary>
        /// Uploads a file to Azure File Share
        /// </summary>
        private static async Task<string> UploadToAzureFileShare(IFormFile file, string connectionString, ILogger log)
        {
            // Initialize Azure File Share client
            var shareServiceClient = new ShareServiceClient(connectionString);
            var shareClient = shareServiceClient.GetShareClient(ShareName);

            // Create share if it doesn't exist
            await shareClient.CreateIfNotExistsAsync();
            log.LogInformation($"Using Azure File Share: {ShareName}");

            // Get root directory client
            var directoryClient = shareClient.GetRootDirectoryClient();

            // Create unique file name to avoid collisions
            var fileName = $"{Guid.NewGuid()}_{file.FileName}";
            var fileClient = directoryClient.GetFileClient(fileName);

            // Upload file
            using var stream = file.OpenReadStream();
            await fileClient.CreateAsync(stream.Length);
            await fileClient.UploadAsync(stream);

            log.LogInformation($"File uploaded to Azure File Share: {fileName}");

            return fileName;
        }

        /// <summary>
        /// Downloads a file from Azure File Share
        /// </summary>
        private static async Task<Stream> DownloadFromAzureFileShare(string fileName, string connectionString, ILogger log)
        {
            var shareServiceClient = new ShareServiceClient(connectionString);
            var shareClient = shareServiceClient.GetShareClient(ShareName);

            if (!await shareClient.ExistsAsync())
            {
                log.LogWarning($"Share {ShareName} does not exist.");
                return null;
            }

            var directoryClient = shareClient.GetRootDirectoryClient();
            var fileClient = directoryClient.GetFileClient(fileName);

            if (!await fileClient.ExistsAsync())
            {
                log.LogWarning($"File {fileName} does not exist.");
                return null;
            }

            var download = await fileClient.DownloadAsync();
            var memoryStream = new MemoryStream();
            await download.Value.Content.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            log.LogInformation($"File downloaded from Azure File Share: {fileName}");
            return memoryStream;
        }

        /// <summary>
        /// Deletes a file from Azure File Share
        /// </summary>
        private static async Task<bool> DeleteFromAzureFileShare(string fileName, string connectionString, ILogger log)
        {
            var shareServiceClient = new ShareServiceClient(connectionString);
            var shareClient = shareServiceClient.GetShareClient(ShareName);

            if (!await shareClient.ExistsAsync())
            {
                log.LogWarning($"Share {ShareName} does not exist.");
                return false;
            }

            var directoryClient = shareClient.GetRootDirectoryClient();
            var fileClient = directoryClient.GetFileClient(fileName);

            if (!await fileClient.ExistsAsync())
            {
                log.LogWarning($"File {fileName} does not exist.");
                return false;
            }

            await fileClient.DeleteAsync();
            log.LogInformation($"File deleted from Azure File Share: {fileName}");
            return true;
        }

        /// <summary>
        /// Helper method to determine file type based on extension
        /// </summary>
        private static string GetFileType(string extension)
        {
            return extension.ToLower() switch
            {
                ".pdf" => "PDF",
                ".docx" => "DOCX",
                ".txt" => "TXT",
                ".xlsx" => "XLSX",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Helper method to get content type based on extension
        /// </summary>
        private static string GetContentType(string extension)
        {
            return extension.ToLower() switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".txt" => "text/plain",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"
            };
        }

        #endregion
    }
}//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//