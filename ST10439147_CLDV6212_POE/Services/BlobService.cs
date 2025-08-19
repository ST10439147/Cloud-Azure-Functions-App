using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace ST10439147_CLDV6212_POE.Services
{
    public class BlobService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly BlobContainerClient _containerClient;
        private const string ContainerName = "products";

        public BlobService(IConfiguration configuration)
        {
            var connectionString = configuration["AzureStorage:ConnectionString"];

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Azure Storage connection string is not configured.");
            }

            try
            {
                _blobServiceClient = new BlobServiceClient(connectionString);
                _containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
                _containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob).Wait();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize Azure Blob Storage: {ex.Message}", ex);
            }
        }

        public async Task<string> UploadImageAsync(IFormFile imageFile)
        {
            try
            {
                if (imageFile == null || imageFile.Length == 0)
                {
                    throw new ArgumentException("Image file is null or empty");
                }

                // Generate unique filename
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
                var blobClient = _containerClient.GetBlobClient(fileName);

                // Set content type
                var blobHttpHeaders = new BlobHttpHeaders
                {
                    ContentType = imageFile.ContentType
                };

                using (var stream = imageFile.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, new BlobUploadOptions
                    {
                        HttpHeaders = blobHttpHeaders
                    });
                }

                return blobClient.Uri.ToString();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to upload image: {ex.Message}", ex);
            }
        }

        public async Task DeleteImageAsync(string imageUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(imageUrl))
                    return;

                var uri = new Uri(imageUrl);
                var fileName = Path.GetFileName(uri.LocalPath);
                var blobClient = _containerClient.GetBlobClient(fileName);

                await blobClient.DeleteIfExistsAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to delete image: {ex.Message}", ex);
            }
        }
    }
}