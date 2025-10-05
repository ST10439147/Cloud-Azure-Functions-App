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
    public class ProductFunction
    {
        private readonly ILogger<ProductFunction> _logger;
        private readonly TableService _tableService;
        private readonly BlobService _blobService;

        public ProductFunction(ILogger<ProductFunction> logger, TableService tableService, BlobService blobService)
        {
            _logger = logger;
            _tableService = tableService;
            _blobService = blobService;
        }

        // POST: Create a new product
        // POST: Create a new product
        [Function("CreateProduct")]
        public async Task<HttpResponseData> CreateProduct(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "products")] HttpRequestData req)
        {
            _logger.LogInformation("Creating new product");

            try
            {
                // Parse multipart form data
                var formData = await req.ReadMultipartAsync();

                _logger.LogInformation("Form data parsed successfully");

                // Log all received fields for debugging
                _logger.LogInformation($"Name: {formData.GetField("Name")}");
                _logger.LogInformation($"Description: {formData.GetField("Description")}");
                _logger.LogInformation($"Price: {formData.GetField("Price")}");
                _logger.LogInformation($"StockQuantity: {formData.GetField("StockQuantity")}");
                _logger.LogInformation($"Files count: {formData.Files.Count}");

                // Validate and parse Price
                var priceField = formData.GetField("Price");
                if (string.IsNullOrEmpty(priceField))
                {
                    _logger.LogError("Price field is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "Price is required" });
                    return badResponse;
                }

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

                // Validate and parse StockQuantity
                var stockField = formData.GetField("StockQuantity");
                if (string.IsNullOrEmpty(stockField))
                {
                    _logger.LogError("StockQuantity field is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "Stock quantity is required" });
                    return badResponse;
                }

                if (!int.TryParse(stockField, out var stockQuantity))
                {
                    _logger.LogError($"Failed to parse stock quantity: {stockField}");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = $"Invalid stock quantity format: {stockField}" });
                    return badResponse;
                }

                _logger.LogInformation($"StockQuantity parsed successfully: {stockQuantity}");

                var product = new Product
                {
                    PartitionKey = "Product",
                    RowKey = Guid.NewGuid().ToString(),
                    Name = formData.GetField("Name"),
                    Description = formData.GetField("Description"),
                    Price = price,
                    StockQuantity = stockQuantity
                };

                _logger.LogInformation($"Product object created with RowKey: {product.RowKey}");

                // Validate required fields
                if (string.IsNullOrEmpty(product.Name))
                {
                    _logger.LogError("Product name is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "Product name is required" });
                    return badResponse;
                }

                // Handle image upload if provided
                if (formData.Files.Count > 0)
                {
                    try
                    {
                        var fileData = formData.Files[0];
                        _logger.LogInformation($"Uploading image: {fileData.FileName}, Size: {fileData.Length}");

                        // Convert FileData to IFormFile for BlobService
                        var formFile = new FormFileWrapper(fileData);
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

                _logger.LogInformation("Inserting product into table storage");
                await _tableService.InsertProductAsync(product);
                _logger.LogInformation($"Product inserted successfully: {product.RowKey}");

                var response = req.CreateResponse(HttpStatusCode.OK);

                // Create a simple response object to avoid serialization issues
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
                _logger.LogError(ex, $"Error creating product: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    _logger.LogError($"Inner exception: {ex.InnerException.Message}");
                    _logger.LogError($"Inner stack trace: {ex.InnerException.StackTrace}");
                }

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

        // GET: Retrieve all products
        [Function("GetAllProducts")]
        public async Task<HttpResponseData> GetAllProducts(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "products")] HttpRequestData req)
        {
            _logger.LogInformation("Retrieving all products");

            try
            {
                var products = await _tableService.GetAllProductsAsync();

                _logger.LogInformation($"Retrieved {products.Count} products");

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

        // GET: Retrieve a product by ID
        [Function("GetProductById")]
        public async Task<HttpResponseData> GetProductById(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "products/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"Retrieving product: {partitionKey}/{rowKey}");

            try
            {
                var product = await _tableService.GetProductByIdAsync(partitionKey, rowKey);

                _logger.LogInformation($"Product retrieved successfully: {rowKey}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(product);
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("retrieve product"))
            {
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

        // PUT: Update an existing product
        [Function("UpdateProduct")]
        public async Task<HttpResponseData> UpdateProduct(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "products/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"Updating product: {partitionKey}/{rowKey}");

            try
            {
                // Get existing product first
                var existingProduct = await _tableService.GetProductByIdAsync(partitionKey, rowKey);

                // Parse multipart form data
                var formData = await req.ReadMultipartAsync();

                // Validate and parse Price
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

                // Validate and parse StockQuantity
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

                // Update other fields
                var name = formData.GetField("Name");
                if (!string.IsNullOrEmpty(name))
                    existingProduct.Name = name;

                var description = formData.GetField("Description");
                if (!string.IsNullOrEmpty(description))
                    existingProduct.Description = description;

                // Handle new image upload if provided
                if (formData.Files.Count > 0)
                {
                    var fileData = formData.Files[0];
                    _logger.LogInformation($"Uploading new image: {fileData.FileName}");

                    // Delete old image if it exists
                    if (!string.IsNullOrEmpty(existingProduct.ImageUrl))
                    {
                        _logger.LogInformation("Deleting old image");
                        await _blobService.DeleteImageAsync(existingProduct.ImageUrl);
                    }

                    // Upload new image
                    var formFile = new FormFileWrapper(fileData);
                    existingProduct.ImageUrl = await _blobService.UploadImageAsync(formFile);
                    _logger.LogInformation($"New image uploaded successfully: {existingProduct.ImageUrl}");
                }

                await _tableService.UpdateProductAsync(existingProduct);

                _logger.LogInformation($"Product updated successfully: {rowKey}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(existingProduct);
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("no longer exists"))
            {
                _logger.LogWarning($"Product not found: {partitionKey}/{rowKey}");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Product not found" });
                return notFoundResponse;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("modified by another user"))
            {
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

        // DELETE: Delete a product
        [Function("DeleteProduct")]
        public async Task<HttpResponseData> DeleteProduct(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "products/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"Deleting product: {partitionKey}/{rowKey}");

            try
            {
                // Get product first to retrieve image URL
                var product = await _tableService.GetProductByIdAsync(partitionKey, rowKey);

                // Delete associated image if it exists
                if (!string.IsNullOrEmpty(product.ImageUrl))
                {
                    _logger.LogInformation("Deleting associated image");
                    await _blobService.DeleteImageAsync(product.ImageUrl);
                }

                // Delete product from table
                await _tableService.DeleteProductAsync(partitionKey, rowKey);

                _logger.LogInformation($"Product deleted successfully: {rowKey}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { message = "Product deleted successfully" });
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("retrieve product") || ex.Message.Contains("delete product"))
            {
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

    // Wrapper class to convert FileData to IFormFile
    internal class FormFileWrapper : Microsoft.AspNetCore.Http.IFormFile
    {
        private readonly FileData _fileData;

        public FormFileWrapper(FileData fileData)
        {
            _fileData = fileData;
        }

        public string ContentType => _fileData.ContentType;
        public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{_fileData.FileName}\"";
        public Microsoft.AspNetCore.Http.IHeaderDictionary Headers => new Microsoft.AspNetCore.Http.HeaderDictionary();
        public long Length => _fileData.Length;
        public string Name => _fileData.Name;
        public string FileName => _fileData.FileName;

        public Stream OpenReadStream() => _fileData.OpenReadStream();

        public void CopyTo(Stream target)
        {
            _fileData.Stream.CopyTo(target);
            _fileData.Stream.Position = 0;
        }

        public Task CopyToAsync(Stream target, System.Threading.CancellationToken cancellationToken = default)
        {
            return _fileData.Stream.CopyToAsync(target, cancellationToken);
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//