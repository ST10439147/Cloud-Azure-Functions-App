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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Azure.Storage.Files.Shares;
using System.Collections.Generic;

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
        /// Uploads a file to Azure File Share
        /// </summary>
        private static async Task<string> UploadToAzureFileShare(IFormFile file, string connectionString, ILogger log)
        {
            const string shareName = "dummycontracts";

            // Initialize Azure File Share client
            var shareServiceClient = new ShareServiceClient(connectionString);
            var shareClient = shareServiceClient.GetShareClient(shareName);

            // Create share if it doesn't exist
            await shareClient.CreateIfNotExistsAsync();
            log.LogInformation($"Using Azure File Share: {shareName}");

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

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
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
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//