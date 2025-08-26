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
        private readonly BlobServiceClient _blobServiceClient;// Client for interacting with the Blob service
        private readonly BlobContainerClient _containerClient;// Client for interacting with the blob container
        private readonly ILogger<BlobService> _logger;// Logger for logging information and errors
        private const string ContainerName = "products";// Name of the blob container
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Constructor to initialize BlobService with configuration and logger
        // Sets up the BlobServiceClient and BlobContainerClient
        // Creates the container if it does not exist
        // Throws InvalidOperationException if configuration is missing or initialization fails
        // Logs relevant information and errors
        public BlobService(IConfiguration configuration, ILogger<BlobService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));// Ensure logger is not null

            var connectionString = configuration["AzureStorage:ConnectionString"];// Get connection string from configuration

            if (string.IsNullOrEmpty(connectionString))// Check if connection string is null or empty
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

                if (createResponse != null)// If the container was created
                {
                    _logger.LogInformation("Created blob container: {ContainerName}", ContainerName);// Log that the container was created
                }
                else
                {
                    _logger.LogInformation("Using existing blob container: {ContainerName}", ContainerName);// Log that the existing container is being used
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure Blob Storage");// Log an error if initialization fails
                throw new InvalidOperationException($"Failed to initialize Azure Blob Storage: {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Uploads an image file to Azure Blob Storage
        // Validates the input file
        // Generates a unique filename to avoid conflicts
        // Sets appropriate content type for the blob
        // Tracks upload progress and logs it
        // Returns the URL of the uploaded image
        // Throws InvalidOperationException if upload fails
        // Logs relevant information and errors
        public async Task<string> UploadImageAsync(IFormFile imageFile)
        {
            if (imageFile == null || imageFile.Length == 0)// Check if the image file is null or empty
            {
                _logger.LogError("Image file is null or empty");// Log an error if the image file is invalid
                throw new ArgumentException("Image file is null or empty");// Throw an exception if the image file is invalid
            }

            try// Try to upload the image file
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
                    var uploadOptions = new BlobUploadOptions// Options for uploading the blob
                    {
                        HttpHeaders = blobHttpHeaders,// Set the content type
                        Conditions = null, // No conditions for new upload
                        // Progress handler to track upload progress
                        ProgressHandler = new Progress<long>(bytesUploaded =>
                        {
                            _logger.LogDebug("Uploaded {BytesUploaded} of {TotalBytes} bytes", bytesUploaded, imageFile.Length);
                        })
                    };

                    var response = await blobClient.UploadAsync(stream, uploadOptions);// Upload the blob

                    if (response != null)// If the upload was successful
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Deletes an image from Azure Blob Storage given its URL
        // Validates the input URL
        // Extracts the filename from the URL
        // Returns true if deletion was successful, false if the image was not found
        // Throws InvalidOperationException if deletion fails
        // Logs relevant information and errors
        // Tracks deletion progress and logs it
        public async Task<bool> DeleteImageAsync(string imageUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(imageUrl))// Check if the image URL is null or empty
                {
                    _logger.LogWarning("Image URL is null or empty, nothing to delete");// Log a warning if the image URL is invalid
                    return false;
                }

                var uri = new Uri(imageUrl);// Parse the image URL
                var fileName = Path.GetFileName(uri.LocalPath);// Extract the filename from the URL

                if (string.IsNullOrEmpty(fileName))// Check if the filename extraction was successful
                {
                    _logger.LogWarning("Could not extract filename from URL: {ImageUrl}", imageUrl);// Log a warning if the filename could not be extracted
                    return false;
                }

                _logger.LogInformation("Deleting image: {FileName} from URL: {ImageUrl}", fileName, imageUrl);// Log the deletion attempt

                var blobClient = _containerClient.GetBlobClient(fileName);// Get the blob client for the specified filename
                var response = await blobClient.DeleteIfExistsAsync();// Attempt to delete the blob if it exists

                if (response.Value)// If the blob was successfully deleted
                {
                    _logger.LogInformation("Successfully deleted image: {FileName}", fileName);// Log the successful deletion
                    return true;
                }
                else
                {
                    _logger.LogWarning("Image not found for deletion: {FileName}", fileName);// Log a warning if the image was not found
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete image from URL: {ImageUrl}", imageUrl);// Log an error if deletion fails
                throw new InvalidOperationException($"Failed to delete image: {ex.Message}", ex);// Throw an exception if deletion fails
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Checks if an image exists in Azure Blob Storage given its URL
        // Validates the input URL
        // Extracts the filename from the URL
        // Returns true if the image exists, false otherwise
        // Logs relevant information and errors
        // Catches exceptions and logs errors
        // Returns false if an error occurs
        public async Task<bool> ImageExistsAsync(string imageUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(imageUrl))// Check if the image URL is null or empty
                    return false;

                var uri = new Uri(imageUrl);
                var fileName = Path.GetFileName(uri.LocalPath);

                if (string.IsNullOrEmpty(fileName))// Check if the filename extraction was successful
                    return false;

                var blobClient = _containerClient.GetBlobClient(fileName);//Get the blob client for the specified filename
                var response = await blobClient.ExistsAsync();// Check if the blob exists

                return response.Value;// Return true if the blob exists, false otherwise
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if image exists: {ImageUrl}", imageUrl);// Log an error if an exception occurs
                return false;
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//