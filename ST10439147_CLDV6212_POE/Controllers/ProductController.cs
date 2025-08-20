using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class ProductController : Controller
    {
        private readonly TableService _tableService;
        private readonly BlobService _blobService;
        private readonly ILogger<ProductController> _logger;

        public ProductController(TableService tableService, BlobService blobService, ILogger<ProductController> logger)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // GET: Product
        public async Task<IActionResult> Index()
        {
            try
            {
                _logger.LogInformation("Retrieving all products");
                var products = await _tableService.GetAllProductsAsync();
                return View(products);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving products");
                ViewBag.Error = "Unable to load products. Please try again.";
                return View(new List<Product>());
            }
        }

        // GET: Product/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Product/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Product product, IFormFile imageFile)
        {
            // Remove ImageUrl from ModelState validation since it's auto-generated
            ModelState.Remove("ImageUrl");

            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation("Creating new product: {ProductName}", product.Name);

                    // Generate RowKey if not set
                    if (string.IsNullOrEmpty(product.RowKey))
                    {
                        product.RowKey = Guid.NewGuid().ToString();
                    }

                    // Handle image upload
                    if (imageFile != null && imageFile.Length > 0)
                    {
                        // Validate image file
                        if (!IsValidImageFile(imageFile, out string errorMessage))
                        {
                            ModelState.AddModelError("imageFile", errorMessage);
                            return View(product);
                        }

                        try
                        {
                            product.ImageUrl = await _blobService.UploadImageAsync(imageFile);
                            _logger.LogInformation("Image uploaded successfully for product: {ProductName}, URL: {ImageUrl}", product.Name, product.ImageUrl);
                        }
                        catch (Exception imgEx)
                        {
                            _logger.LogError(imgEx, "Failed to upload image for product: {ProductName}", product.Name);
                            ModelState.AddModelError("imageFile", "Failed to upload image. Please try again.");
                            return View(product);
                        }
                    }
                    else
                    {
                        // Set a default placeholder if no image is provided
                        product.ImageUrl = "/images/no-image.png"; // or leave as null if you prefer
                    }

                    await _tableService.InsertProductAsync(product);
                    _logger.LogInformation("Product created successfully: {ProductName} with ID: {ProductId}", product.Name, product.RowKey);

                    TempData["Success"] = "Product added successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating product: {ProductName}", product.Name);
                    ModelState.AddModelError("", "Unable to save product. Please try again.");
                }
            }
            else
            {
                // Log model state errors for debugging
                foreach (var modelError in ModelState.Where(x => x.Key != "ImageUrl").SelectMany(x => x.Value.Errors))
                {
                    _logger.LogWarning("Model validation error: {Error}", modelError.ErrorMessage);
                }
            }

            // If we got this far, something failed, redisplay form
            return View(product);
        }

        // GET: Product/Details/5
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogWarning("Product Details called with null or empty ID");
                return NotFound();
            }

            try
            {
                var product = await _tableService.GetProductByIdAsync("Product", id);

                if (product == null)
                {
                    _logger.LogWarning("Product not found: {ProductId}", id);
                    return NotFound();
                }

                return View(product);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving product details for ID: {ProductId}", id);
                ViewBag.Error = "Unable to load product details.";
                return View();
            }
        }

        // GET: Product/Edit/5
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
                var product = await _tableService.GetProductByIdAsync("Product", id);

                if (product == null)
                {
                    _logger.LogWarning("Product not found for edit: {ProductId}", id);
                    return NotFound();
                }

                return View(product);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving product for edit: {ProductId}", id);
                TempData["Error"] = "Unable to load product for editing.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Product/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, Product product, IFormFile imageFile)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            if (id != product.RowKey)
            {
                _logger.LogWarning("ID mismatch in Edit: URL ID {UrlId}, Product RowKey {RowKey}", id, product.RowKey);
                return BadRequest("ID mismatch");
            }

            // Remove ImageUrl from ModelState validation since it's handled separately
            ModelState.Remove("ImageUrl");

            if (ModelState.IsValid)
            {
                try
                {
                    // Get the existing product to preserve ETag and existing image URL
                    var existingProduct = await _tableService.GetProductByIdAsync("Product", id);
                    if (existingProduct == null)
                    {
                        _logger.LogWarning("Existing product not found for update: {ProductId}", id);
                        return NotFound();
                    }

                    // Preserve the ETag for optimistic concurrency
                    product.ETag = existingProduct.ETag;

                    // Handle new image upload
                    if (imageFile != null && imageFile.Length > 0)
                    {
                        // Validate image file
                        if (!IsValidImageFile(imageFile, out string errorMessage))
                        {
                            ModelState.AddModelError("imageFile", errorMessage);
                            return View(product);
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
                                    _logger.LogInformation("Old image deleted for product: {ProductId}", id);
                                }
                                catch (Exception delEx)
                                {
                                    _logger.LogWarning(delEx, "Failed to delete old image: {ImageUrl}", existingProduct.ImageUrl);
                                    // Continue with update even if old image deletion fails
                                }
                            }

                            // Upload new image
                            product.ImageUrl = await _blobService.UploadImageAsync(imageFile);
                            _logger.LogInformation("New image uploaded for product: {ProductId}, URL: {ImageUrl}", id, product.ImageUrl);
                        }
                        catch (Exception imgEx)
                        {
                            _logger.LogError(imgEx, "Failed to upload new image for product: {ProductId}", id);
                            ModelState.AddModelError("imageFile", "Failed to upload new image. Please try again.");
                            return View(product);
                        }
                    }
                    else
                    {
                        // Preserve existing image URL if no new image uploaded
                        product.ImageUrl = existingProduct.ImageUrl;
                    }

                    await _tableService.UpdateProductAsync(product);
                    _logger.LogInformation("Product updated successfully: {ProductId}", id);

                    TempData["Success"] = "Product updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating product: {ProductId}", id);
                    ModelState.AddModelError("", "Unable to update product. Please try again.");
                }
            }
            else
            {
                // Log validation errors for debugging
                foreach (var modelError in ModelState.Where(x => x.Key != "ImageUrl").SelectMany(x => x.Value.Errors))
                {
                    _logger.LogWarning("Model validation error in Edit: {Error}", modelError.ErrorMessage);
                }
            }

            return View(product);
        }

        // GET: Product/Delete/5
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
                var product = await _tableService.GetProductByIdAsync("Product", id);

                if (product == null)
                {
                    _logger.LogWarning("Product not found for delete: {ProductId}", id);
                    return NotFound();
                }

                return View(product);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving product for delete confirmation: {ProductId}", id);
                TempData["Error"] = "Unable to load product for deletion.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Product/Delete/5
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
                var product = await _tableService.GetProductByIdAsync("Product", id);

                if (product != null)
                {
                    // Delete associated image if it exists and it's not a placeholder
                    if (!string.IsNullOrEmpty(product.ImageUrl) &&
                        !product.ImageUrl.StartsWith("/images/"))
                    {
                        try
                        {
                            await _blobService.DeleteImageAsync(product.ImageUrl);
                            _logger.LogInformation("Image deleted successfully for product: {ProductId}", id);
                        }
                        catch (Exception imgEx)
                        {
                            _logger.LogWarning(imgEx, "Failed to delete image for product: {ProductId}, URL: {ImageUrl}", id, product.ImageUrl);
                            // Continue with product deletion even if image deletion fails
                        }
                    }

                    // Delete the product from table storage
                    await _tableService.DeleteProductAsync("Product", id);

                    _logger.LogInformation("Product deleted successfully: {ProductId}", id);
                    TempData["Success"] = "Product deleted successfully!";
                }
                else
                {
                    _logger.LogWarning("Product not found for deletion: {ProductId}", id);
                    TempData["Error"] = "Product not found.";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting product: {ProductId}", id);
                TempData["Error"] = "Unable to delete product. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        #region Helper Methods

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