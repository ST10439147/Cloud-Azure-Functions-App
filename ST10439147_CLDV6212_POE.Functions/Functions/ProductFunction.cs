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

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ST10439147_CLDV6212_POE.Services;
using ST10439147_CLDV6212_POE.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ST10439147_CLDV6212_POE.Functions.Functions
{
    public class ProductFunction
    {
        private readonly TableService _tableService;
        private readonly BlobService _blobService;
        private readonly ILogger<ProductFunction> _logger;

        public ProductFunction(TableService tableService, BlobService blobService, ILogger<ProductFunction> logger)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function("GetAllProducts")]
        public async Task<IActionResult> GetAllProducts(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "products")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("GetAllProducts function triggered");
                var products = await _tableService.GetAllProductsAsync();
                return new OkObjectResult(products);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving products");
                return new ObjectResult(new { error = "Unable to load products. Please try again.", message = ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        [Function("GetProductById")]
        public async Task<IActionResult> GetProductById(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "products/{partitionKey}/{rowKey}")]
            HttpRequest req,
            string partitionKey,
            string rowKey)
        {
            if (string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Product GetProductById called with null or empty ID");
                return new NotFoundObjectResult(new { error = "Product ID is required" });
            }

            try
            {
                _logger.LogInformation($"GetProductById function triggered for {partitionKey}/{rowKey}");

                var product = await _tableService.GetProductByIdAsync(partitionKey, rowKey);
                if (product == null)
                {
                    _logger.LogWarning("Product not found: {ProductId}", rowKey);
                    return new NotFoundObjectResult(new { error = "Product not found" });
                }

                return new OkObjectResult(product);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving product details for ID: {ProductId}", rowKey);
                return new ObjectResult(new { error = "Unable to load product details.", message = ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        [Function("CreateProduct")]
        public async Task<IActionResult> CreateProduct(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "products")]
            HttpRequest req)
        {
            try
            {
                _logger.LogInformation("CreateProduct function triggered");

                // Parse multipart form data
                if (!req.HasFormContentType)
                {
                    return new BadRequestObjectResult(new { error = "Request must be multipart/form-data" });
                }

                var form = await req.ReadFormAsync();

                // Extract product data from form
                var product = new Product
                {
                    Name = form["Name"].ToString(),
                    Description = form["Description"].ToString(),
                    Price = double.TryParse(form["Price"], out var price) ? price : 0,
                    StockQuantity = int.TryParse(form["StockQuantity"], out var stock) ? stock : 0,
                    PartitionKey = "Product"
                };

                // Validate required fields
                if (string.IsNullOrEmpty(product.Name))
                {
                    return new BadRequestObjectResult(new { error = "Product name is required" });
                }

                if (product.Price <= 0)
                {
                    return new BadRequestObjectResult(new { error = "Price must be greater than 0" });
                }

                if (product.StockQuantity < 0)
                {
                    return new BadRequestObjectResult(new { error = "Stock quantity cannot be negative" });
                }

                // Generate RowKey if not set
                if (string.IsNullOrEmpty(product.RowKey))
                {
                    product.RowKey = Guid.NewGuid().ToString();
                }

                // Handle image upload
                var imageFile = form.Files.GetFile("imageFile");
                if (imageFile != null && imageFile.Length > 0)
                {
                    // Validate image file
                    if (!IsValidImageFile(imageFile, out string errorMessage))
                    {
                        return new BadRequestObjectResult(new { error = errorMessage });
                    }

                    try
                    {
                        product.ImageUrl = await _blobService.UploadImageAsync(imageFile);
                        _logger.LogInformation("Image uploaded successfully for product: {ProductName}, URL: {ImageUrl}",
                            product.Name, product.ImageUrl);
                    }
                    catch (Exception imgEx)
                    {
                        _logger.LogError(imgEx, "Failed to upload image for product: {ProductName}", product.Name);
                        return new BadRequestObjectResult(new { error = "Failed to upload image. Please try again." });
                    }
                }
                else
                {
                    // Set a default placeholder if no image is provided
                    product.ImageUrl = "/images/no-image.png";
                }

                // Insert product into table storage
                await _tableService.InsertProductAsync(product);
                _logger.LogInformation("Product created successfully: {ProductName} with ID: {ProductId}",
                    product.Name, product.RowKey);

                return new CreatedResult($"/api/products/{product.PartitionKey}/{product.RowKey}", product);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating product");
                return new ObjectResult(new { error = "Unable to save product. Please try again.", message = ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        [Function("UpdateProduct")]
        public async Task<IActionResult> UpdateProduct(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "products/{partitionKey}/{rowKey}")]
            HttpRequest req,
            string partitionKey,
            string rowKey)
        {
            if (string.IsNullOrEmpty(rowKey))
            {
                return new NotFoundObjectResult(new { error = "Product ID is required" });
            }

            try
            {
                _logger.LogInformation($"UpdateProduct function triggered for {partitionKey}/{rowKey}");

                // Parse multipart form data
                if (!req.HasFormContentType)
                {
                    return new BadRequestObjectResult(new { error = "Request must be multipart/form-data" });
                }

                var form = await req.ReadFormAsync();

                // Get the existing product to preserve ETag and existing image URL
                var existingProduct = await _tableService.GetProductByIdAsync(partitionKey, rowKey);
                if (existingProduct == null)
                {
                    _logger.LogWarning("Existing product not found for update: {ProductId}", rowKey);
                    return new NotFoundObjectResult(new { error = "Product not found" });
                }

                // Update product fields from form
                existingProduct.Name = form["Name"].ToString();
                existingProduct.Description = form["Description"].ToString();
                existingProduct.Price = double.TryParse(form["Price"], out var price) ? price : existingProduct.Price;
                existingProduct.StockQuantity = int.TryParse(form["StockQuantity"], out var stock) ? stock : existingProduct.StockQuantity;

                // Validate fields
                if (string.IsNullOrEmpty(existingProduct.Name))
                {
                    return new BadRequestObjectResult(new { error = "Product name is required" });
                }

                if (existingProduct.Price <= 0)
                {
                    return new BadRequestObjectResult(new { error = "Price must be greater than 0" });
                }

                if (existingProduct.StockQuantity < 0)
                {
                    return new BadRequestObjectResult(new { error = "Stock quantity cannot be negative" });
                }

                // Handle new image upload
                var imageFile = form.Files.GetFile("imageFile");
                if (imageFile != null && imageFile.Length > 0)
                {
                    // Validate image file
                    if (!IsValidImageFile(imageFile, out string errorMessage))
                    {
                        return new BadRequestObjectResult(new { error = errorMessage });
                    }

                    try
                    {
                        // Delete old image if it exists and it's not a placeholder
                        if (!string.IsNullOrEmpty(existingProduct.ImageUrl) &&
                            !existingProduct.ImageUrl.StartsWith("/images/"))
                        {
                            try
                            {
                                await _blobService.DeleteImageAsync(existingProduct.ImageUrl);
                                _logger.LogInformation("Old image deleted for product: {ProductId}", rowKey);
                            }
                            catch (Exception delEx)
                            {
                                _logger.LogWarning(delEx, "Failed to delete old image: {ImageUrl}", existingProduct.ImageUrl);
                                // Continue with update even if old image deletion fails
                            }
                        }

                        // Upload new image
                        existingProduct.ImageUrl = await _blobService.UploadImageAsync(imageFile);
                        _logger.LogInformation("New image uploaded for product: {ProductId}, URL: {ImageUrl}",
                            rowKey, existingProduct.ImageUrl);
                    }
                    catch (Exception imgEx)
                    {
                        _logger.LogError(imgEx, "Failed to upload new image for product: {ProductId}", rowKey);
                        return new BadRequestObjectResult(new { error = "Failed to upload new image. Please try again." });
                    }
                }
                // If no new image uploaded, existing ImageUrl is preserved

                await _tableService.UpdateProductAsync(existingProduct);
                _logger.LogInformation("Product updated successfully: {ProductId}", rowKey);

                return new OkObjectResult(existingProduct);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("modified by another user"))
            {
                _logger.LogWarning(ex, "Concurrency conflict");
                return new ObjectResult(new { error = "Product has been modified by another user. Please refresh and try again." })
                {
                    StatusCode = StatusCodes.Status409Conflict
                };
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("no longer exists"))
            {
                _logger.LogWarning(ex, "Product not found");
                return new NotFoundObjectResult(new { error = "Product not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating product: {ProductId}", rowKey);
                return new ObjectResult(new { error = "Unable to update product. Please try again.", message = ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        [Function("DeleteProduct")]
        public async Task<IActionResult> DeleteProduct(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "products/{partitionKey}/{rowKey}")]
            HttpRequest req,
            string partitionKey,
            string rowKey)
        {
            if (string.IsNullOrEmpty(rowKey))
            {
                return new NotFoundObjectResult(new { error = "Product ID is required" });
            }

            try
            {
                _logger.LogInformation($"DeleteProduct function triggered for {partitionKey}/{rowKey}");

                var product = await _tableService.GetProductByIdAsync(partitionKey, rowKey);

                if (product != null)
                {
                    // Delete associated image if it exists and it's not a placeholder
                    if (!string.IsNullOrEmpty(product.ImageUrl) &&
                        !product.ImageUrl.StartsWith("/images/"))
                    {
                        try
                        {
                            await _blobService.DeleteImageAsync(product.ImageUrl);
                            _logger.LogInformation("Image deleted successfully for product: {ProductId}", rowKey);
                        }
                        catch (Exception imgEx)
                        {
                            _logger.LogWarning(imgEx, "Failed to delete image for product: {ProductId}, URL: {ImageUrl}",
                                rowKey, product.ImageUrl);
                            // Continue with product deletion even if image deletion fails
                        }
                    }

                    // Delete the product from table storage
                    await _tableService.DeleteProductAsync(partitionKey, rowKey);

                    _logger.LogInformation("Product deleted successfully: {ProductId}", rowKey);
                    return new OkObjectResult(new { message = "Product deleted successfully!" });
                }
                else
                {
                    _logger.LogWarning("Product not found for deletion: {ProductId}", rowKey);
                    return new NotFoundObjectResult(new { error = "Product not found" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting product: {ProductId}", rowKey);
                return new ObjectResult(new { error = "Unable to delete product. Please try again.", message = ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        #region Helper Methods

        // Validates the uploaded image file for correct type and size
        // Returns true if valid, false otherwise with an error message
        // Supports common image formats and limits size to 5MB
        private bool IsValidImageFile(IFormFile imageFile, out string errorMessage)
        {
            errorMessage = string.Empty;

            // Check if file exists
            if (imageFile == null || imageFile.Length == 0)
            {
                errorMessage = "Please select an image file.";
                return false;
            }

            // Validate file extension
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
            var extension = Path.GetExtension(imageFile.FileName)?.ToLowerInvariant();

            if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
            {
                errorMessage = "Please upload a valid image file (jpg, jpeg, png, gif, bmp, webp).";
                return false;
            }

            // Check file size (limit to 5MB)
            const int maxFileSize = 5 * 1024 * 1024; // 5MB
            if (imageFile.Length > maxFileSize)
            {
                errorMessage = "Image file size cannot exceed 5MB.";
                return false;
            }

            // Validate content type
            var allowedContentTypes = new[] {
                "image/jpeg",
                "image/jpg",
                "image/png",
                "image/gif",
                "image/bmp",
                "image/webp"
            };

            if (!allowedContentTypes.Contains(imageFile.ContentType?.ToLowerInvariant()))
            {
                errorMessage = "Invalid image file type.";
                return false;
            }

            return true;
        }

        #endregion
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//