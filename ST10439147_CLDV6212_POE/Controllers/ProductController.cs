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

using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class ProductController : Controller
    {
        
        private readonly TableService _tableService;// Access to table storage operations
        private readonly BlobService _blobService;// Access to blob storage operations
        private readonly ILogger<ProductController> _logger;// Logger for tracking and debugging
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Constructor with dependency injection for services and logger
        // Ensures services are available for use in controller methods
        public ProductController(TableService tableService, BlobService blobService, ILogger<ProductController> logger)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Product
        // Retrieves and displays all products
        // Handles errors nicely and logs issues
        // Accessible via /Product/Index or /Product
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Product/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Product/Create
        // Handles product creation with image upload
        // Validates input and manages errors
        // Redirects to Index on success
        // This method is used to create a new product entry in the system.
        // It accepts a Product model and an optional image file for upload.
        // If the model is valid, it uploads the image (if provided), saves the product to table storage,
        // and redirects to the Index view with a success message.
        // If there are validation errors or exceptions, it logs the issues and redisplays the form with error messages.
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
                    // Insert product into table storage
                    await _tableService.InsertProductAsync(product);
                    _logger.LogInformation("Product created successfully: {ProductName} with ID: {ProductId}", product.Name, product.RowKey);
                    // Redirect to Index with success message
                    TempData["Success"] = "Product added successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)// Catch any exceptions during product creation
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Product/Details/5
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))// If ID is null or empty
            {
                _logger.LogWarning("Product Details called with null or empty ID");// Log warning
                return NotFound();
            }

            try
            {
                var product = await _tableService.GetProductByIdAsync("Product", id);// Retrieve the product by ID

                if (product == null)// If product not found
                {
                    _logger.LogWarning("Product not found: {ProductId}", id);
                    return NotFound();
                }

                return View(product);// Pass product to view
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving product details for ID: {ProductId}", id);
                ViewBag.Error = "Unable to load product details.";
                return View();
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Product/Edit/5
        // Displays the edit form for a specific product
        // Handles errors and logs issues
        // This method retrieves the product to be edited and displays it in a form.
        // It handles cases where the product ID is null or the product does not exist,
        // logging warnings and errors as appropriate.
        // If the product is found, it passes the product to the view for editing.
        // If an error occurs during retrieval, it logs the error and redirects to the Index view with an error message.
        // It expects the product ID as a parameter to identify which product to edit.
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Product/Edit/5
        // Handles product updates with optional image replacement
        // Validates input and manages errors
        // Redirects to Index on success
        // This method is used to update an existing product entry in the system.
        // It accepts a Product model and an optional new image file for upload.
        // It ensures the product ID in the URL matches the product's RowKey.
        // If the model is valid, it uploads the new image (if provided), updates the product in table storage,
        // and redirects to the Index view with a success message.
        // If there are validation errors or exceptions, it logs the issues and redisplays the form with error messages.
        // The method also preserves the existing image URL if no new image is uploaded.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, Product product, IFormFile imageFile)
        {
            if (string.IsNullOrEmpty(id))// If ID is null or empty
            {
                return NotFound();
            }

            if (id != product.RowKey)// Ensure the ID in the URL matches the product's RowKey
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

                    // Preserve the ETag
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

                    await _tableService.UpdateProductAsync(product);// Update product in table storage
                    _logger.LogInformation("Product updated successfully: {ProductId}", id);

                    TempData["Success"] = "Product updated successfully!";
                    return RedirectToAction(nameof(Index));// Redirect to Index on success
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Product/Delete/5
        // Displays confirmation page for product deletion
        // Handles errors and logs issues
        // Accessible via /Product/Delete/5
        // This method retrieves the product to be deleted and displays a confirmation view.
        // It handles cases where the product ID is null or the product does not exist,
        // logging warnings and errors.
        // If the product is found, it passes the product to the view for user confirmation.
        // If an error occurs during retrieval, it logs the error and redirects to the Index view with an error message.
        [HttpGet]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id))// If ID is null or empty
            {
                _logger.LogWarning("Product Delete called with null or empty ID");
                return NotFound();
            }

            try
            {
                var product = await _tableService.GetProductByIdAsync("Product", id);// Retrieve the product by ID

                if (product == null)// If product not found
                {
                    _logger.LogWarning("Product not found for delete: {ProductId}", id);
                    return NotFound();
                }

                return View(product);// Pass product to view for confirmation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving product for delete confirmation: {ProductId}", id);
                TempData["Error"] = "Unable to load product for deletion.";
                return RedirectToAction(nameof(Index));
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Product/Delete/5
        // Confirms and processes product deletion
        // Deletes associated image if applicable
        // Handles errors and logs actions
        // Redirects to Index after deletion
        // This method handles the confirmation and processing of product deletion.
        // It deletes the product from table storage and also removes the associated image from blob storage if it exists.
        // It manages errors gracefully and logs all significant actions for auditing and debugging purposes.
        // On successful deletion, it redirects to the Index view with a success message.
        // If the product is not found or an error occurs, it logs the issue and redirects with an error message.
        // The method is decorated with [HttpPost] and [ValidateAntiForgeryToken] to ensure secure form submission.
        // The ActionName attribute allows it to be called "Delete" in the view, matching the GET method.
        // It expects the product ID as a parameter to identify which product to delete.
        // It checks for null or empty IDs and handles them appropriately.
        // It uses the TableService to retrieve and delete the product and the BlobService to manage image deletion.
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
                var product = await _tableService.GetProductByIdAsync("Product", id);// Retrieve the product to get image URL

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

                    _logger.LogInformation("Product deleted successfully: {ProductId}", id);// Log success
                    TempData["Success"] = "Product deleted successfully!";
                }
                else
                {
                    _logger.LogWarning("Product not found for deletion: {ProductId}", id);
                    TempData["Error"] = "Product not found.";
                }

                return RedirectToAction(nameof(Index));// Redirect to Index after deletion
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting product: {ProductId}", id);
                TempData["Error"] = "Unable to delete product. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Using region to organize helper methods
        #region Helper Methods

        // Validates the uploaded image file for correct type and size
        // Returns true if valid, false otherwise with an error message
        // Supports common image formats and limits size to 5MB
        // This method checks if the provided IFormFile is a valid image file.
        // It verifies the file's existence, extension, size, and content type.
        // If the file is invalid, it sets an appropriate error message.
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

            if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))// If extension is not allowed
            {
                errorMessage = "Please upload a valid image file (jpg, jpeg, png, gif, bmp, webp).";
                return false;
            }

            // Check file size (limit to 5MB)
            const int maxFileSize = 5 * 1024 * 1024; // 5MB
            if (imageFile.Length > maxFileSize)// If file size exceeds limit
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

            if (!allowedContentTypes.Contains(imageFile.ContentType?.ToLowerInvariant()))// If content type is not allowed
            {
                errorMessage = "Invalid image file type.";
                return false;
            }

            return true;
        }

        #endregion // end of the region
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//