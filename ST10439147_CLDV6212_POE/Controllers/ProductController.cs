// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2

//References:
// ClaudAI - https://claude.ai/
// ChatGPT - https://chat.openai.com/
// W3schools - https://www.w3schools.com/
// IIEVC School of Computer Science Youtube channel for Azure services setup and use https://www.youtube.com/@VCSOCS
// AzureApp project done in class with lecturer

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ST10439147_CLDV6212_POE.Models;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Controllers
{
    /// <summary>
    /// Controller responsible for managing product-related operations
    /// Communicates with Azure Functions to perform CRUD operations on products
    /// </summary>
    public class ProductController : Controller
    {
        // HTTP client for making requests to Azure Functions
        private readonly HttpClient _httpClient;

        // Logger for tracking operations and errors
        private readonly ILogger<ProductController> _logger;

        // Base URL for Azure Functions endpoint
        private readonly string _functionBaseUrl;

        // Security key for authenticating with Azure Functions
        private readonly string _functionKey;

        /// <summary>
        /// Constructor - Initializes the controller with required dependencies
        /// </summary>
        /// <param name="httpClientFactory">Factory for creating HTTP clients</param>
        /// <param name="logger">Logger for recording application events</param>
        /// <param name="configuration">Configuration containing Azure Function settings</param>
        public ProductController(IHttpClientFactory httpClientFactory, ILogger<ProductController> logger, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Load and validate Azure Function base URL from configuration
            _functionBaseUrl = configuration["AzureFunctions:BaseUrl"]?.TrimEnd('/')
                ?? throw new InvalidOperationException("AzureFunctions:BaseUrl is not configured");

            // Load and validate Azure Function security key from configuration
            _functionKey = configuration["AzureFunctions:FunctionKey"]
                ?? throw new InvalidOperationException("AzureFunctions:FunctionKey is not configured");
        }

        /// <summary>
        /// GET: Product/Index
        /// Retrieves and displays all products from the database
        /// </summary>
        /// <returns>View with list of all products</returns>
        public async Task<IActionResult> Index()
        {
            try
            {
                _logger.LogInformation("Retrieving all products from Azure Function");

                // Create HTTP GET request to retrieve all products
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/products");
                request.Headers.Add("x-functions-key", _functionKey);

                // Send request to Azure Function
                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    // Read and deserialize the JSON response into a list of products
                    var content = await response.Content.ReadAsStringAsync();
                    var products = JsonSerializer.Deserialize<List<Product>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true // Ignore case differences in property names
                    }) ?? new List<Product>();

                    return View(products);
                }

                // Log error if retrieval fails
                _logger.LogError("Error retrieving products: {StatusCode}", response.StatusCode);
                ViewBag.Error = "Unable to load products. Please try again.";
                return View(new List<Product>());
            }
            catch (Exception ex)
            {
                // Catch and log any unexpected errors
                _logger.LogError(ex, "Error retrieving products");
                ViewBag.Error = "Unable to load products. Please try again.";
                return View(new List<Product>());
            }
        }

        /// <summary>
        /// GET: Product/Create
        /// Displays the form for creating a new product
        /// </summary>
        /// <returns>Create product view</returns>
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        /// <summary>
        /// POST: Product/Create
        /// Processes the creation of a new product with image upload
        /// </summary>
        /// <param name="product">Product data from the form</param>
        /// <param name="imageFile">Image file uploaded for the product</param>
        /// <returns>Redirects to Index on success, or returns to Create view with errors</returns>
        [HttpPost]
        [ValidateAntiForgeryToken] // Protects against CSRF attacks
        public async Task<IActionResult> Create(Product product, IFormFile? imageFile)
        {
            // Remove validation for fields that will be set automatically
            ModelState.Remove("ImageUrl");
            ModelState.Remove("RowKey");

            // Validate that an image file was provided (required for new products)
            if (imageFile == null || imageFile.Length == 0)
            {
                ModelState.AddModelError("imageFile", "Please select an image file for the product.");
                _logger.LogWarning("Product creation attempted without image file: {ProductName}", product.Name);
                return View(product);
            }

            // Check if all other model validations passed
            if (!ModelState.IsValid)
            {
                return View(product);
            }

            try
            {
                _logger.LogInformation("Creating new product: {ProductName}", product.Name);

                // Validate image file properties
                if (imageFile.Length > 0)
                {
                    // Validate file size - maximum 5MB
                    const long maxFileSize = 5 * 1024 * 1024; // 5MB in bytes
                    if (imageFile.Length > maxFileSize)
                    {
                        ModelState.AddModelError("imageFile",
                            $"File size cannot exceed {maxFileSize / (1024 * 1024)}MB. Current size: {imageFile.Length / (1024 * 1024)}MB");
                        _logger.LogWarning("Image file too large: {FileName}, Size: {Size} bytes",
                            imageFile.FileName, imageFile.Length);
                        return View(product);
                    }

                    // Define allowed file extensions and content types
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                    var allowedContentTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };

                    var fileExtension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();

                    // Validate file extension
                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        ModelState.AddModelError("imageFile",
                            $"Invalid file type. Allowed types: {string.Join(", ", allowedExtensions)}");
                        _logger.LogWarning("Invalid file extension: {FileName}, Extension: {Extension}",
                            imageFile.FileName, fileExtension);
                        return View(product);
                    }

                    // Validate content type (MIME type)
                    if (!allowedContentTypes.Contains(imageFile.ContentType.ToLowerInvariant()))
                    {
                        ModelState.AddModelError("imageFile",
                            "Invalid file content type. Please upload a valid image file.");
                        _logger.LogWarning("Invalid content type: {FileName}, ContentType: {ContentType}",
                            imageFile.FileName, imageFile.ContentType);
                        return View(product);
                    }

                    // Validate actual file content by checking file signature (magic bytes)
                    try
                    {
                        using var stream = imageFile.OpenReadStream();
                        var buffer = new byte[8]; // Read first 8 bytes
                        await stream.ReadAsync(buffer, 0, 8);
                        stream.Position = 0; // Reset stream for later use

                        // Check if file signature matches the extension
                        bool isValidImage = IsValidImageSignature(buffer, fileExtension);
                        if (!isValidImage)
                        {
                            ModelState.AddModelError("imageFile",
                                "The uploaded file does not appear to be a valid image.");
                            _logger.LogWarning("Invalid image signature: {FileName}", imageFile.FileName);
                            return View(product);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error validating image file: {FileName}", imageFile.FileName);
                        ModelState.AddModelError("imageFile", "Unable to validate image file.");
                        return View(product);
                    }
                }

                // Prepare multipart form data for sending to Azure Function
                using var formData = new MultipartFormDataContent();

                // Add product properties to form data
                formData.Add(new StringContent(product.Name ?? string.Empty), "Name");
                formData.Add(new StringContent(product.Description ?? string.Empty), "Description");

                // Format price using invariant culture to ensure decimal point (not comma)
                var formattedPrice = product.Price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                formData.Add(new StringContent(formattedPrice), "Price");

                formData.Add(new StringContent(product.StockQuantity.ToString()), "StockQuantity");

                // Add image file to form data
                try
                {
                    var fileContent = new StreamContent(imageFile.OpenReadStream());
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue(imageFile.ContentType);
                    formData.Add(fileContent, "imageFile", imageFile.FileName);

                    _logger.LogInformation("Adding image file: {FileName}, Size: {Size} bytes, ContentType: {ContentType}",
                        imageFile.FileName, imageFile.Length, imageFile.ContentType);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading image file: {FileName}", imageFile.FileName);
                    ModelState.AddModelError("imageFile", "Unable to read the image file. Please try again.");
                    return View(product);
                }

                // Create HTTP POST request to Azure Function
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_functionBaseUrl}/products");
                request.Headers.Add("x-functions-key", _functionKey);
                request.Content = formData;

                // Set timeout of 2 minutes for file upload
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

                HttpResponseMessage response;
                try
                {
                    // Send request with cancellation token
                    response = await _httpClient.SendAsync(request, cts.Token);
                }
                catch (TaskCanceledException)
                {
                    // Handle timeout
                    _logger.LogError("Request timeout while creating product: {ProductName}", product.Name);
                    ModelState.AddModelError(string.Empty,
                        "The request took too long to complete. This may be due to a large image file. Please try with a smaller image.");
                    return View(product);
                }

                // Check if product was created successfully
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Product created successfully with status code: {StatusCode}",
                        response.StatusCode);
                    TempData["Success"] = "Product added successfully!";
                    return RedirectToAction(nameof(Index));
                }

                // Handle various error scenarios based on HTTP status codes
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Error creating product: {StatusCode} - {ErrorContent}",
                    response.StatusCode, errorContent);

                switch (response.StatusCode)
                {
                    case System.Net.HttpStatusCode.BadRequest:
                        // Parse and display specific error messages from Azure Function
                        try
                        {
                            var errorObj = JsonSerializer.Deserialize<Dictionary<string, string>>(errorContent,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (errorObj != null && errorObj.ContainsKey("error"))
                            {
                                var errorMessage = errorObj["error"];

                                // Check if error is related to blob storage
                                if (errorMessage.Contains("blob", StringComparison.OrdinalIgnoreCase) ||
                                    errorMessage.Contains("storage", StringComparison.OrdinalIgnoreCase) ||
                                    errorMessage.Contains("upload", StringComparison.OrdinalIgnoreCase) ||
                                    errorMessage.Contains("image", StringComparison.OrdinalIgnoreCase))
                                {
                                    ModelState.AddModelError("imageFile",
                                        "Failed to upload image to storage. Please try again or use a different image.");
                                    _logger.LogError("Blob upload failed for product: {ProductName}. Error: {Error}",
                                        product.Name, errorMessage);
                                }
                                else
                                {
                                    ModelState.AddModelError(string.Empty, errorMessage);
                                }
                            }
                            else
                            {
                                ModelState.AddModelError(string.Empty, "Invalid data provided. Please check your input.");
                            }
                        }
                        catch
                        {
                            ModelState.AddModelError(string.Empty, "Invalid data provided. Please check your input.");
                        }
                        break;

                    case System.Net.HttpStatusCode.RequestEntityTooLarge:
                        // File too large for blob storage
                        ModelState.AddModelError("imageFile",
                            "The image file is too large for blob storage. Please upload a smaller image (max 5MB).");
                        _logger.LogError("Image file too large for blob storage: {ProductName}, Size: {Size}",
                            product.Name, imageFile?.Length);
                        break;

                    case System.Net.HttpStatusCode.UnsupportedMediaType:
                        // Invalid file type for blob storage
                        ModelState.AddModelError("imageFile",
                            "The image file type is not supported by blob storage. Please upload a JPG, PNG, or GIF image.");
                        _logger.LogError("Unsupported media type for blob upload: {ProductName}, ContentType: {ContentType}",
                            product.Name, imageFile?.ContentType);
                        break;

                    case System.Net.HttpStatusCode.InternalServerError:
                        // Server error, check if related to blob storage
                        if (errorContent.Contains("blob", StringComparison.OrdinalIgnoreCase) ||
                            errorContent.Contains("storage", StringComparison.OrdinalIgnoreCase))
                        {
                            ModelState.AddModelError("imageFile",
                                "The image storage service encountered an error. Please try again later.");
                            _logger.LogError("Blob storage service error for product: {ProductName}. Details: {ErrorContent}",
                                product.Name, errorContent);
                        }
                        else
                        {
                            ModelState.AddModelError(string.Empty,
                                "A server error occurred. Please try again later.");
                        }
                        break;

                    case System.Net.HttpStatusCode.ServiceUnavailable:
                        // Service temporarily unavailable
                        ModelState.AddModelError("imageFile",
                            "The image storage service is temporarily unavailable. Please try again later.");
                        _logger.LogError("Blob storage service unavailable for product: {ProductName}", product.Name);
                        break;

                    case System.Net.HttpStatusCode.Conflict:
                        // File name already exists in blob storage
                        ModelState.AddModelError("imageFile",
                            "An image with this name already exists in storage. Please rename your file and try again.");
                        _logger.LogError("Blob name conflict for product: {ProductName}, FileName: {FileName}",
                            product.Name, imageFile?.FileName);
                        break;

                    default:
                        // Handle any other error status codes
                        try
                        {
                            var errorObj = JsonSerializer.Deserialize<Dictionary<string, string>>(errorContent,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (errorObj != null && errorObj.ContainsKey("error"))
                            {
                                ModelState.AddModelError(string.Empty, errorObj["error"]);
                            }
                            else
                            {
                                ModelState.AddModelError(string.Empty,
                                    $"Unable to save product. Server returned: {response.StatusCode}");
                            }
                        }
                        catch
                        {
                            ModelState.AddModelError(string.Empty,
                                $"Unable to save product. Server returned: {response.StatusCode}");
                        }
                        break;
                }
            }
            catch (HttpRequestException httpEx)
            {
                // Handle network-related errors
                _logger.LogError(httpEx, "HTTP request error creating product: {ProductName}", product.Name);
                ModelState.AddModelError(string.Empty,
                    "Unable to connect to the server. Please check your internet connection and try again.");
            }
            catch (IOException ioEx)
            {
                // Handle file reading errors
                _logger.LogError(ioEx, "IO error while processing image file for product: {ProductName}", product.Name);
                ModelState.AddModelError("imageFile",
                    "Error reading the image file. The file may be corrupted or in use by another program.");
            }
            catch (Exception ex)
            {
                // Catch any other unexpected errors
                _logger.LogError(ex, "Unexpected error creating product: {ProductName}", product.Name);
                ModelState.AddModelError(string.Empty,
                    "An unexpected error occurred. Please try again. If the problem persists, contact support.");
            }

            // Return to create view with validation errors
            return View(product);
        }

        /// <summary>
        /// Helper method to validate image file signatures (magic bytes)
        /// Prevents users from uploading non-image files with image extensions
        /// </summary>
        /// <param name="buffer">First 8 bytes of the file</param>
        /// <param name="extension">File extension</param>
        /// <returns>True if file signature matches the extension</returns>
        private bool IsValidImageSignature(byte[] buffer, string extension)
        {
            if (buffer.Length < 8) return false;

            // Check file signature (magic bytes) for each image type
            return extension switch
            {
                // JPEG files start with FF D8 FF
                ".jpg" or ".jpeg" => buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF,

                // PNG files start with 89 50 4E 47 (.PNG)
                ".png" => buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47,

                // GIF files start with 47 49 46 (GIF)
                ".gif" => buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46,

                // WebP files have RIFF signature and WEBP identifier
                ".webp" => buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46 &&
                           buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50,

                _ => false
            };
        }

        /// <summary>
        /// GET: Product/Details/5
        /// Displays detailed information for a specific product
        /// </summary>
        /// <param name="id">Product ID (RowKey)</param>
        /// <returns>Details view for the specified product</returns>
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogWarning("Product Details called with null or empty ID");
                return NotFound();
            }

            try
            {
                // Create HTTP GET request to retrieve specific product
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{_functionBaseUrl}/products/Product/{id}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                // Handle product not found
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Product not found: {ProductId}", id);
                    return NotFound();
                }

                if (response.IsSuccessStatusCode)
                {
                    // Deserialize product data from JSON
                    var content = await response.Content.ReadAsStringAsync();
                    var product = JsonSerializer.Deserialize<Product>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (product == null)
                    {
                        _logger.LogWarning("Failed to deserialize product: {ProductId}", id);
                        return NotFound();
                    }

                    return View(product);
                }

                // Log error if retrieval fails
                _logger.LogError("Error retrieving product: {StatusCode}", response.StatusCode);
                ViewBag.Error = "Unable to load product details.";
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving product details for ID: {ProductId}", id);
                ViewBag.Error = "Unable to load product details.";
                return View();
            }
        }

        /// <summary>
        /// GET: Product/Edit/5
        /// Displays the edit form for a specific product
        /// </summary>
        /// <param name="id">Product ID (RowKey)</param>
        /// <returns>Edit view with product data populated</returns>
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogWarning("Product Edit called with null or empty ID");
                return NotFound();
            }

            try
            {
                // Retrieve product data for editing
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{_functionBaseUrl}/products/Product/{id}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Product not found for edit: {ProductId}", id);
                    return NotFound();
                }

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var product = JsonSerializer.Deserialize<Product>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (product == null)
                    {
                        _logger.LogWarning("Failed to deserialize product for edit: {ProductId}", id);
                        return NotFound();
                    }

                    return View(product);
                }

                _logger.LogError("Error loading product: {StatusCode}", response.StatusCode);
                TempData["Error"] = "Unable to load product for editing.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving product for edit: {ProductId}", id);
                TempData["Error"] = "Unable to load product for editing.";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// POST: Product/Edit/5
        /// Processes the update of an existing product
        /// Allows updating product details and optionally uploading a new image
        /// </summary>
        /// <param name="id">Product ID from URL</param>
        /// <param name="product">Updated product data from form</param>
        /// <param name="imageFile">Optional new image file</param>
        /// <returns>Redirects to Index on success, or returns to Edit view with errors</returns>
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, Product product, IFormFile? imageFile)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            // Verify URL ID matches product ID to prevent tampering
            if (id != product.RowKey)
            {
                _logger.LogWarning("ID mismatch in Edit: URL ID {UrlId}, Product RowKey {RowKey}",
                    id, product.RowKey);
                return BadRequest("ID mismatch");
            }

            // Remove image validation - not required for updates
            ModelState.Remove("ImageUrl");

            if (!ModelState.IsValid)
            {
                return View(product);
            }

            try
            {
                _logger.LogInformation("Updating product: {ProductId}", id);

                // Prepare form data with updated product information
                using var formData = new MultipartFormDataContent();

                formData.Add(new StringContent(product.Name ?? string.Empty), "Name");
                formData.Add(new StringContent(product.Description ?? string.Empty), "Description");

                // Ensure correct decimal format (use period, not comma)
                var formattedPrice = product.Price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                formData.Add(new StringContent(formattedPrice), "Price");

                formData.Add(new StringContent(product.StockQuantity.ToString()), "StockQuantity");

                // Add new image file only if provided (optional for updates)
                if (imageFile is { Length: > 0 })
                {
                    var fileContent = new StreamContent(imageFile.OpenReadStream());
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue(imageFile.ContentType);
                    formData.Add(fileContent, "imageFile", imageFile.FileName);

                    _logger.LogInformation("Adding new image file for update: {FileName}, Size: {Size} bytes",
                        imageFile.FileName, imageFile.Length);
                }

                // Create HTTP PUT request to update the product
                var request = new HttpRequestMessage(HttpMethod.Put, $"{_functionBaseUrl}/products/Product/{id}")
                {
                    Content = formData
                };
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Product updated successfully: {ProductId}", id);
                    TempData["Success"] = "Product updated successfully!";
                    return RedirectToAction(nameof(Index));
                }

                // Handle various error scenarios
                var errorContent = await response.Content.ReadAsStringAsync();

                switch (response.StatusCode)
                {
                    case System.Net.HttpStatusCode.NotFound:
                        // Product was deleted by another user
                        _logger.LogWarning("Product not found during update: {ProductId}", id);
                        TempData["Error"] = "The product no longer exists.";
                        return RedirectToAction(nameof(Index));

                    case System.Net.HttpStatusCode.Conflict:
                        // Product was modified by another user (concurrency conflict)
                        _logger.LogWarning("Concurrency conflict updating product: {ProductId}", id);
                        ModelState.AddModelError(string.Empty,
                            "The product was modified by another user. Please refresh and try again.");
                        return View(product);

                    default:
                        // Handle other errors
                        _logger.LogError("Error updating product: {StatusCode} - {ErrorContent}",
                            response.StatusCode, errorContent);

                        try
                        {
                            var errorObj = JsonSerializer.Deserialize<Dictionary<string, string>>(errorContent,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (errorObj != null && errorObj.ContainsKey("error"))
                                ModelState.AddModelError(string.Empty, errorObj["error"]);
                            else
                                ModelState.AddModelError(string.Empty, "Unable to update product. Please try again.");
                        }
                        catch
                        {
                            ModelState.AddModelError(string.Empty,
                                $"Unable to update product. Server returned: {response.StatusCode}");
                        }
                        break;
                }
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, "HTTP request error updating product: {ProductId}", id);
                ModelState.AddModelError(string.Empty, "Unable to connect to the server. Please try again.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating product: {ProductId}", id);
                ModelState.AddModelError(string.Empty, "An unexpected error occurred. Please try again.");
            }

            // Return to edit view with errors
            return View(product);
        }

        /// <summary>
        /// GET: Product/Delete/5
        /// Displays confirmation page before deleting a product
        /// </summary>
        /// <param name="id">Product ID (RowKey)</param>
        /// <returns>Delete confirmation view with product details</returns>
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogWarning("Product Delete called with null or empty ID");
                return NotFound();
            }

            try
            {
                // Retrieve product data for delete confirmation
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{_functionBaseUrl}/products/Product/{id}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Product not found for delete: {ProductId}", id);
                    return NotFound();
                }

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var product = JsonSerializer.Deserialize<Product>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (product == null)
                    {
                        _logger.LogWarning("Failed to deserialize product for delete: {ProductId}", id);
                        return NotFound();
                    }

                    return View(product);
                }

                _logger.LogError("Error loading product for delete: {StatusCode}", response.StatusCode);
                TempData["Error"] = "Unable to load product for deletion.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving product for delete confirmation: {ProductId}", id);
                TempData["Error"] = "Unable to load product for deletion.";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// POST: Product/Delete/5
        /// Processes the deletion of a product after user confirmation
        /// Also deletes associated image from blob storage
        /// </summary>
        /// <param name="id">Product ID (RowKey)</param>
        /// <returns>Redirects to Index with success/error message</returns>
        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            try
            {
                _logger.LogInformation("Deleting product: {ProductId}", id);

                // Create HTTP DELETE request to remove the product
                var request = new HttpRequestMessage(HttpMethod.Delete,
                    $"{_functionBaseUrl}/products/Product/{id}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    // Product and associated image deleted successfully
                    _logger.LogInformation("Product deleted successfully: {ProductId}", id);
                    TempData["Success"] = "Product deleted successfully!";
                    return RedirectToAction(nameof(Index));
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Product no longer exists (may have been deleted by another user)
                    _logger.LogWarning("Product not found during delete: {ProductId}", id);
                    TempData["Error"] = "Product not found.";
                    return RedirectToAction(nameof(Index));
                }

                // Log any other errors that occur during deletion
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Error deleting product: {StatusCode} - {ErrorContent}",
                    response.StatusCode, errorContent);
                TempData["Error"] = "Unable to delete product. Please try again.";
                return RedirectToAction(nameof(Index));
            }
            catch (HttpRequestException httpEx)
            {
                // Handle network connectivity issues
                _logger.LogError(httpEx, "HTTP request error deleting product: {ProductId}", id);
                TempData["Error"] = "Unable to connect to the server. Please try again.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                // Catch any unexpected errors
                _logger.LogError(ex, "Error deleting product: {ProductId}", id);
                TempData["Error"] = "Unable to delete product. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//