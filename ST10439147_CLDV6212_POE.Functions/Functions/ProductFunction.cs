// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2

using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ST10439147_CLDV6212_POE.Services;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Extensions;
using Azure;

namespace ST10439147_CLDV6212_POE.Functions
{
    /// <summary>
    /// Azure Function class that provides HTTP endpoints for product CRUD operations
    /// Integrates with Azure Table Storage for data persistence and Azure Blob Storage for images
    /// </summary>
    public class ProductFunction
    {
        // Logger for recording function execution and debugging information
        private readonly ILogger<ProductFunction> _logger;

        // Service for interacting with Azure Table Storage
        private readonly TableService _tableService;

        // Service for managing image uploads/deletions in Azure Blob Storage
        private readonly BlobService _blobService;

        /// <summary>
        /// Constructor - Dependency injection of required services
        /// </summary>
        /// <param name="logger">Logger instance for this function</param>
        /// <param name="tableService">Service for Azure Table Storage operations</param>
        /// <param name="blobService">Service for Azure Blob Storage operations</param>
        public ProductFunction(ILogger<ProductFunction> logger, TableService tableService, BlobService blobService)
        {
            _logger = logger;
            _tableService = tableService;
            _blobService = blobService;
        }

        /// <summary>
        /// POST: Creates a new product with optional image upload
        /// Route: POST /api/products
        /// Requires function-level authorization
        /// </summary>
        /// <param name="req">HTTP request containing multipart form data with product details and image</param>
        /// <returns>HTTP response with created product data or error message</returns>
        [Function("CreateProduct")]
        public async Task<HttpResponseData> CreateProduct(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "products")] HttpRequestData req)
        {
            _logger.LogInformation("Creating new product");

            try
            {
                // Parse the incoming multipart/form-data request
                var formData = await req.ReadMultipartAsync();

                _logger.LogInformation("Form data parsed successfully");

                // Log all received form fields for debugging purposes
                _logger.LogInformation($"Name: {formData.GetField("Name")}");
                _logger.LogInformation($"Description: {formData.GetField("Description")}");
                _logger.LogInformation($"Price: {formData.GetField("Price")}");
                _logger.LogInformation($"StockQuantity: {formData.GetField("StockQuantity")}");
                _logger.LogInformation($"Files count: {formData.Files.Count}");

                // Validate that Price field exists
                var priceField = formData.GetField("Price");
                if (string.IsNullOrEmpty(priceField))
                {
                    _logger.LogError("Price field is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "Price is required" });
                    return badResponse;
                }

                // Parse Price field using InvariantCulture to handle decimal points correctly
                if (!double.TryParse(priceField,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var price))
                {
                    _logger.LogError($"Failed to parse price: {priceField}");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = $"Invalid price format: {priceField}" });
                    return badResponse;
                }

                _logger.LogInformation($"Price parsed successfully: {price}");

                // Validate that StockQuantity field exists
                var stockField = formData.GetField("StockQuantity");
                if (string.IsNullOrEmpty(stockField))
                {
                    _logger.LogError("StockQuantity field is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "Stock quantity is required" });
                    return badResponse;
                }

                // Parse StockQuantity as integer
                if (!int.TryParse(stockField, out var stockQuantity))
                {
                    _logger.LogError($"Failed to parse stock quantity: {stockField}");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = $"Invalid stock quantity format: {stockField}" });
                    return badResponse;
                }

                _logger.LogInformation($"StockQuantity parsed successfully: {stockQuantity}");

                // Create new Product entity with Azure Table Storage keys
                var product = new Product
                {
                    PartitionKey = "Product", // All products share the same partition key
                    RowKey = Guid.NewGuid().ToString(), // Unique identifier for this product
                    Name = formData.GetField("Name"),
                    Description = formData.GetField("Description"),
                    Price = price,
                    StockQuantity = stockQuantity
                };

                _logger.LogInformation($"Product object created with RowKey: {product.RowKey}");

                // Validate that product name was provided
                if (string.IsNullOrEmpty(product.Name))
                {
                    _logger.LogError("Product name is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "Product name is required" });
                    return badResponse;
                }

                // Handle image upload if a file was included in the request
                if (formData.Files.Count > 0)
                {
                    try
                    {
                        var fileData = formData.Files[0];
                        _logger.LogInformation($"Uploading image: {fileData.FileName}, Size: {fileData.Length}");

                        // Convert Azure Function FileData to IFormFile interface for BlobService
                        var formFile = new FormFileWrapper(fileData);

                        // Upload to Azure Blob Storage and get the public URL
                        product.ImageUrl = await _blobService.UploadImageAsync(formFile);
                        _logger.LogInformation($"Image uploaded successfully: {product.ImageUrl}");
                    }
                    catch (Exception imgEx)
                    {
                        _logger.LogError(imgEx, "Error uploading image");
                        var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                        await errorResponse.WriteAsJsonAsync(new { error = $"Failed to upload image: {imgEx.Message}" });
                        return errorResponse;
                    }
                }
                else
                {
                    _logger.LogInformation("No image file provided");
                }

                // Insert the product into Azure Table Storage
                _logger.LogInformation("Inserting product into table storage");
                await _tableService.InsertProductAsync(product);
                _logger.LogInformation($"Product inserted successfully: {product.RowKey}");

                var response = req.CreateResponse(HttpStatusCode.OK);

                // Create a simplified response object to avoid serialization issues with Azure Table entities
                var responseData = new
                {
                    rowKey = product.RowKey,
                    partitionKey = product.PartitionKey,
                    name = product.Name,
                    description = product.Description,
                    price = product.Price,
                    stockQuantity = product.StockQuantity,
                    imageUrl = product.ImageUrl
                };

                _logger.LogInformation("Writing response");
                await response.WriteAsJsonAsync(responseData);
                _logger.LogInformation("Response written successfully");

                return response;
            }
            catch (Exception ex)
            {
                // Comprehensive error logging with stack trace and inner exceptions
                _logger.LogError(ex, $"Error creating product: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    _logger.LogError($"Inner exception: {ex.InnerException.Message}");
                    _logger.LogError($"Inner stack trace: {ex.InnerException.StackTrace}");
                }

                // Return detailed error information for debugging
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new
                {
                    error = ex.Message,
                    innerError = ex.InnerException?.Message,
                    type = ex.GetType().Name
                });
                return errorResponse;
            }
        }

        /// <summary>
        /// GET: Retrieves all products from the database
        /// Route: GET /api/products
        /// Requires function-level authorization
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <returns>HTTP response with list of all products</returns>
        [Function("GetAllProducts")]
        public async Task<HttpResponseData> GetAllProducts(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "products")] HttpRequestData req)
        {
            _logger.LogInformation("Retrieving all products");

            try
            {
                // Query Azure Table Storage for all products
                var products = await _tableService.GetAllProductsAsync();

                _logger.LogInformation($"Retrieved {products.Count} products");

                // Return products as JSON array
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(products);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving products: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
                return errorResponse;
            }
        }

        /// <summary>
        /// GET: Retrieves a specific product by its partition key and row key
        /// Route: GET /api/products/{partitionKey}/{rowKey}
        /// Requires function-level authorization
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <param name="partitionKey">Partition key (typically "Product")</param>
        /// <param name="rowKey">Unique product identifier (GUID)</param>
        /// <returns>HTTP response with product data or 404 if not found</returns>
        [Function("GetProductById")]
        public async Task<HttpResponseData> GetProductById(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "products/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"Retrieving product: {partitionKey}/{rowKey}");

            try
            {
                // Fetch product from Azure Table Storage using both keys
                var product = await _tableService.GetProductByIdAsync(partitionKey, rowKey);

                _logger.LogInformation($"Product retrieved successfully: {rowKey}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(product);
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("retrieve product"))
            {
                // Handle case where product doesn't exist
                _logger.LogWarning($"Product not found: {partitionKey}/{rowKey}");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Product not found" });
                return notFoundResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving product: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
                return errorResponse;
            }
        }

        /// <summary>
        /// PUT: Updates an existing product
        /// Route: PUT /api/products/{partitionKey}/{rowKey}
        /// Requires function-level authorization
        /// Supports partial updates - only provided fields are updated
        /// </summary>
        /// <param name="req">HTTP request containing multipart form data with updated values</param>
        /// <param name="partitionKey">Partition key of the product to update</param>
        /// <param name="rowKey">Row key of the product to update</param>
        /// <returns>HTTP response with updated product data or error message</returns>
        [Function("UpdateProduct")]
        public async Task<HttpResponseData> UpdateProduct(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "products/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"Updating product: {partitionKey}/{rowKey}");

            try
            {
                // Retrieve existing product to update its properties
                var existingProduct = await _tableService.GetProductByIdAsync(partitionKey, rowKey);

                // Parse multipart form data containing update values
                var formData = await req.ReadMultipartAsync();

                // Update Price if provided
                var priceField = formData.GetField("Price");
                if (!string.IsNullOrEmpty(priceField))
                {
                    if (!double.TryParse(priceField,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var price))
                    {
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        await badResponse.WriteAsJsonAsync(new { error = "Invalid price format" });
                        return badResponse;
                    }
                    existingProduct.Price = price;
                }

                // Update StockQuantity if provided
                var stockField = formData.GetField("StockQuantity");
                if (!string.IsNullOrEmpty(stockField))
                {
                    if (!int.TryParse(stockField, out var stockQuantity))
                    {
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        await badResponse.WriteAsJsonAsync(new { error = "Invalid stock quantity format" });
                        return badResponse;
                    }
                    existingProduct.StockQuantity = stockQuantity;
                }

                // Update Name if provided
                var name = formData.GetField("Name");
                if (!string.IsNullOrEmpty(name))
                    existingProduct.Name = name;

                // Update Description if provided
                var description = formData.GetField("Description");
                if (!string.IsNullOrEmpty(description))
                    existingProduct.Description = description;

                // Handle new image upload if a file was provided
                if (formData.Files.Count > 0)
                {
                    var fileData = formData.Files[0];
                    _logger.LogInformation($"Uploading new image: {fileData.FileName}");

                    // Delete the old image from blob storage to avoid orphaned files
                    if (!string.IsNullOrEmpty(existingProduct.ImageUrl))
                    {
                        _logger.LogInformation("Deleting old image");
                        await _blobService.DeleteImageAsync(existingProduct.ImageUrl);
                    }

                    // Upload new image and update URL
                    var formFile = new FormFileWrapper(fileData);
                    existingProduct.ImageUrl = await _blobService.UploadImageAsync(formFile);
                    _logger.LogInformation($"New image uploaded successfully: {existingProduct.ImageUrl}");
                }

                // Persist changes to Azure Table Storage
                await _tableService.UpdateProductAsync(existingProduct);

                _logger.LogInformation($"Product updated successfully: {rowKey}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(existingProduct);
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("no longer exists"))
            {
                // Product was deleted before update could complete
                _logger.LogWarning($"Product not found: {partitionKey}/{rowKey}");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Product not found" });
                return notFoundResponse;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("modified by another user"))
            {
                // Optimistic concurrency conflict - another user modified this product
                _logger.LogWarning($"Concurrency conflict for product: {partitionKey}/{rowKey}");
                var conflictResponse = req.CreateResponse(HttpStatusCode.Conflict);
                await conflictResponse.WriteAsJsonAsync(new { error = "Product has been modified by another user" });
                return conflictResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating product: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
                return errorResponse;
            }
        }

        /// <summary>
        /// DELETE: Deletes a product and its associated image
        /// Route: DELETE /api/products/{partitionKey}/{rowKey}
        /// Requires function-level authorization
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <param name="partitionKey">Partition key of the product to delete</param>
        /// <param name="rowKey">Row key of the product to delete</param>
        /// <returns>HTTP response with success message or error</returns>
        [Function("DeleteProduct")]
        public async Task<HttpResponseData> DeleteProduct(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "products/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"Deleting product: {partitionKey}/{rowKey}");

            try
            {
                // Retrieve product first to get the image URL before deletion
                var product = await _tableService.GetProductByIdAsync(partitionKey, rowKey);

                // Delete associated image from blob storage if it exists
                if (!string.IsNullOrEmpty(product.ImageUrl))
                {
                    _logger.LogInformation("Deleting associated image");
                    await _blobService.DeleteImageAsync(product.ImageUrl);
                }

                // Delete product record from Azure Table Storage
                await _tableService.DeleteProductAsync(partitionKey, rowKey);

                _logger.LogInformation($"Product deleted successfully: {rowKey}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { message = "Product deleted successfully" });
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("retrieve product") || ex.Message.Contains("delete product"))
            {
                // Product doesn't exist or was already deleted
                _logger.LogWarning($"Product not found: {partitionKey}/{rowKey}");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Product not found" });
                return notFoundResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting product: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
                return errorResponse;
            }
        }
    }

    /// <summary>
    /// Wrapper class to convert Azure Functions FileData to IFormFile interface
    /// This allows the BlobService to work with files from Azure Functions HTTP triggers
    /// </summary>
    internal class FormFileWrapper : Microsoft.AspNetCore.Http.IFormFile
    {
        private readonly FileData _fileData;

        public FormFileWrapper(FileData fileData)
        {
            _fileData = fileData;
        }

        // Expose file content type (e.g., "image/jpeg")
        public string ContentType => _fileData.ContentType;

        // Content disposition header for HTTP form data
        public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{_fileData.FileName}\"";

        // Empty header dictionary (not used for blob uploads)
        public Microsoft.AspNetCore.Http.IHeaderDictionary Headers => new Microsoft.AspNetCore.Http.HeaderDictionary();

        // File size in bytes
        public long Length => _fileData.Length;

        // Form field name
        public string Name => _fileData.Name;

        // Original filename from upload
        public string FileName => _fileData.FileName;

        // Open the file stream for reading
        public Stream OpenReadStream() => _fileData.OpenReadStream();

        // Synchronous copy to target stream
        public void CopyTo(Stream target)
        {
            _fileData.Stream.CopyTo(target);
            _fileData.Stream.Position = 0; // Reset position for potential reuse
        }

        // Asynchronous copy to target stream
        public Task CopyToAsync(Stream target, System.Threading.CancellationToken cancellationToken = default)
        {
            return _fileData.Stream.CopyToAsync(target, cancellationToken);
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//