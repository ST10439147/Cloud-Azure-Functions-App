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
        [Function("CreateProduct")]
        public async Task<HttpResponseData> CreateProduct(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "products")] HttpRequestData req)
        {
            _logger.LogInformation("Creating new product");

            try
            {
                // Parse multipart form data
                var formData = await req.ReadMultipartAsync();

                var product = new Product
                {
                    PartitionKey = "Product",
                    RowKey = Guid.NewGuid().ToString(),
                    Name = formData.GetField("Name"),
                    Description = formData.GetField("Description"),
                    Price = double.Parse(formData.GetField("Price")),
                    StockQuantity = int.Parse(formData.GetField("StockQuantity"))
                };

                // Validate required fields
                if (string.IsNullOrEmpty(product.Name))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "Product name is required" });
                    return badResponse;
                }

                // Handle image upload if provided
                if (formData.Files.Count > 0)
                {
                    var fileData = formData.Files[0];
                    _logger.LogInformation($"Uploading image: {fileData.FileName}");

                    // Convert FileData to IFormFile for BlobService
                    var formFile = new FormFileWrapper(fileData);
                    product.ImageUrl = await _blobService.UploadImageAsync(formFile);
                    _logger.LogInformation($"Image uploaded successfully: {product.ImageUrl}");
                }

                await _tableService.InsertProductAsync(product);

                _logger.LogInformation($"Product created successfully: {product.RowKey}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(product);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating product: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
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

                // Update fields
                existingProduct.Name = formData.GetField("Name");
                existingProduct.Description = formData.GetField("Description");
                existingProduct.Price = double.Parse(formData.GetField("Price"));
                existingProduct.StockQuantity = int.Parse(formData.GetField("StockQuantity"));

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