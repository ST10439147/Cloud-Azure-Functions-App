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

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace ST10439147_CLDV6212_POE.Services
{
    public class BlobService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly BlobContainerClient _containerClient;
        private readonly ILogger<BlobService> _logger;
        private const string ContainerName = "products";

        public BlobService(IConfiguration configuration, ILogger<BlobService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var connectionString = configuration["AzureStorage:ConnectionString"];

            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("Azure Storage connection string is not configured");
                throw new InvalidOperationException("Azure Storage connection string is not configured.");
            }

            try
            {
                _blobServiceClient = new BlobServiceClient(connectionString);
                _containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);

                // Create container if it doesn't exist
                var createResponse = _containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob).GetAwaiter().GetResult();

                if (createResponse != null)
                {
                    _logger.LogInformation("Created blob container: {ContainerName}", ContainerName);
                }
                else
                {
                    _logger.LogInformation("Using existing blob container: {ContainerName}", ContainerName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure Blob Storage");
                throw new InvalidOperationException($"Failed to initialize Azure Blob Storage: {ex.Message}", ex);
            }
        }

        public async Task<string> UploadImageAsync(IFormFile imageFile)
        {
            if (imageFile == null || imageFile.Length == 0)
            {
                _logger.LogError("Image file is null or empty");
                throw new ArgumentException("Image file is null or empty");
            }

            try
            {
                // Generate unique filename
                var fileExtension = Path.GetExtension(imageFile.FileName);
                var fileName = $"{Guid.NewGuid()}{fileExtension}";

                _logger.LogInformation("Uploading image: {FileName}, Size: {FileSize} bytes", fileName, imageFile.Length);

                var blobClient = _containerClient.GetBlobClient(fileName);

                // Set content type
                var blobHttpHeaders = new BlobHttpHeaders
                {
                    ContentType = imageFile.ContentType
                };

                // Upload with progress tracking
                using (var stream = imageFile.OpenReadStream())
                {
                    var uploadOptions = new BlobUploadOptions
                    {
                        HttpHeaders = blobHttpHeaders,
                        Conditions = null, // No conditions for new upload
                        ProgressHandler = new Progress<long>(bytesUploaded =>
                        {
                            _logger.LogDebug("Uploaded {BytesUploaded} of {TotalBytes} bytes", bytesUploaded, imageFile.Length);
                        })
                    };

                    var response = await blobClient.UploadAsync(stream, uploadOptions);

                    if (response != null)
                    {
                        var imageUrl = blobClient.Uri.ToString();
                        _logger.LogInformation("Successfully uploaded image: {FileName} to URL: {ImageUrl}", fileName, imageUrl);
                        return imageUrl;
                    }
                    else
                    {
                        throw new InvalidOperationException("Upload response was null");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload image: {FileName}", imageFile.FileName);
                throw new InvalidOperationException($"Failed to upload image: {ex.Message}", ex);
            }
        }

        public async Task<bool> DeleteImageAsync(string imageUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(imageUrl))
                {
                    _logger.LogWarning("Image URL is null or empty, nothing to delete");
                    return false;
                }

                var uri = new Uri(imageUrl);
                var fileName = Path.GetFileName(uri.LocalPath);

                if (string.IsNullOrEmpty(fileName))
                {
                    _logger.LogWarning("Could not extract filename from URL: {ImageUrl}", imageUrl);
                    return false;
                }

                _logger.LogInformation("Deleting image: {FileName} from URL: {ImageUrl}", fileName, imageUrl);

                var blobClient = _containerClient.GetBlobClient(fileName);
                var response = await blobClient.DeleteIfExistsAsync();

                if (response.Value)
                {
                    _logger.LogInformation("Successfully deleted image: {FileName}", fileName);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Image not found for deletion: {FileName}", fileName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete image from URL: {ImageUrl}", imageUrl);
                throw new InvalidOperationException($"Failed to delete image: {ex.Message}", ex);
            }
        }

        public async Task<bool> ImageExistsAsync(string imageUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(imageUrl))
                    return false;

                var uri = new Uri(imageUrl);
                var fileName = Path.GetFileName(uri.LocalPath);

                if (string.IsNullOrEmpty(fileName))
                    return false;

                var blobClient = _containerClient.GetBlobClient(fileName);
                var response = await blobClient.ExistsAsync();

                return response.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if image exists: {ImageUrl}", imageUrl);
                return false;
            }
        }
    }
}