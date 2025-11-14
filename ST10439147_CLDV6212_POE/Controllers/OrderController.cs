// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - Updated with Customer/Admin Role Separation

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Controllers
{
    /// <summary>
    /// Controller responsible for managing customer orders throughout their lifecycle.
    /// Handles order creation, viewing, editing, and deletion with role-based access control.
    /// Integrates with Azure Table Storage for persistence, Azure Functions for processing,
    /// and Azure Queue Storage for event messaging and inventory tracking.
    /// </summary>
    public class OrderController : Controller
    {
        // Private fields for dependency injection
        private readonly TableService _tableService;
        private readonly QueueService _queueService;
        private readonly ILogger<OrderController> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the OrderController with required services.
        /// </summary>
        /// <param name="tableService">Service for accessing Azure Table Storage</param>
        /// <param name="queueService">Service for sending messages to Azure Queue Storage</param>
        /// <param name="logger">Logger for recording application events and errors</param>
        /// <param name="httpClient">HTTP client for calling Azure Functions</param>
        /// <param name="configuration">Configuration provider for accessing application settings</param>
        /// <exception cref="ArgumentNullException">Thrown when any required service is null</exception>
        public OrderController(
            TableService tableService,
            QueueService queueService,
            ILogger<OrderController> logger,
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays all orders in the system for administrators.
        /// Provides filtering by order status and enriches data with customer and product information.
        /// </summary>
        /// <param name="status">Optional status filter (e.g., "Pending", "Completed", "Cancelled")</param>
        /// <returns>Admin view with list of all orders, optionally filtered by status</returns>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(string status = null)
        {
            try
            {
                _logger.LogInformation("Admin retrieving all orders");

                // Retrieve all orders from Azure Table Storage
                var orders = await _tableService.GetAllOrdersAsync();

                // Apply status filter if provided
                if (!string.IsNullOrEmpty(status))
                {
                    orders = orders.Where(o => o.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                // Load all customers and products for display enrichment
                var customers = await _tableService.GetAllCustomersAsync();
                var products = await _tableService.GetAllProductsAsync();

                // Create dictionaries for efficient lookup by ID
                var customerDict = customers.ToDictionary(c => c.RowKey, c => c);
                var productDict = products.ToDictionary(p => p.RowKey, p => p);

                // Pass lookup dictionaries to view via ViewBag
                ViewBag.Customers = customerDict;
                ViewBag.Products = productDict;
                ViewBag.StatusFilter = status;

                return View("AdminIndex", orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving orders");
                ViewBag.Error = "Unable to load orders. Please try again.";
                return View("AdminIndex", new List<Order>());
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays orders for the currently logged-in customer.
        /// Only shows orders belonging to the authenticated customer with associated product details.
        /// </summary>
        /// <returns>Customer view with their personal order history</returns>
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<IActionResult> MyOrders()
        {
            // Extract customer ID from authentication claims
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;

            // Validate customer ID exists
            if (string.IsNullOrEmpty(customerIdClaim))
            {
                TempData["Error"] = "Customer information not found.";
                return RedirectToAction("Index", "Home");
            }

            try
            {
                _logger.LogInformation("Customer {CustomerId} retrieving their orders", customerIdClaim);

                // Get all orders and filter to current customer
                var allOrders = await _tableService.GetAllOrdersAsync();
                var customerOrders = allOrders.Where(o => o.CustomerId == customerIdClaim).ToList();

                // Load product details for each order - use dictionary to avoid duplicates
                var productsDict = new Dictionary<string, Product>();

                foreach (var order in customerOrders)
                {
                    // Skip orders with missing product IDs
                    if (string.IsNullOrEmpty(order.ProductId))
                    {
                        _logger.LogWarning("Order {OrderId} has null or empty ProductId", order.RowKey);
                        continue;
                    }

                    // Skip if product already loaded
                    if (productsDict.ContainsKey(order.ProductId))
                        continue;

                    try
                    {
                        // Retrieve product details from Table Storage
                        var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);
                        if (product != null)
                        {
                            productsDict[order.ProductId] = product;
                        }
                        else
                        {
                            _logger.LogWarning("Product {ProductId} not found for order {OrderId}",
                                order.ProductId, order.RowKey);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log warning but continue - product details are optional for display
                        _logger.LogWarning(ex, "Could not load product {ProductId} for order {OrderId}",
                            order.ProductId, order.RowKey);
                    }
                }

                // Always initialize ViewBag.Products to prevent null reference errors in view
                ViewBag.Products = productsDict;

                return View("MyOrders", customerOrders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading orders for customer {CustomerId}", customerIdClaim);
                TempData["Error"] = "Unable to load your orders.";

                // Initialize ViewBag.Products even on error to prevent view crashes
                ViewBag.Products = new Dictionary<string, Product>();
                return View("MyOrders", new List<Order>());
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays the order creation form with available products.
        /// </summary>
        /// <returns>Create view with product selection options</returns>
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            try
            {
                _logger.LogInformation("Loading data for order creation");

                // Retrieve all available products
                var products = await _tableService.GetAllProductsAsync();

                // Validate that products are available
                if (!products.Any())
                {
                    TempData["Error"] = "No products available. Please check back later.";
                    return RedirectToAction(nameof(MyOrders));
                }

                // Pass products to view for selection dropdown
                ViewBag.Products = products;

                var order = new Order();
                return View(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading data for order creation");
                TempData["Error"] = "Unable to load data for order creation. Please try again.";
                return RedirectToAction(nameof(MyOrders));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Processes new order creation submitted by customer.
        /// Validates stock availability, updates inventory, and queues processing messages.
        /// Supports both direct processing and Azure Function-based processing.
        /// </summary>
        /// <param name="ProductId">ID of the product being ordered</param>
        /// <param name="Quantity">Quantity of items to order</param>
        /// <param name="TotalPrice">Total price as string (handles culture-specific formatting)</param>
        /// <returns>Redirect to MyOrders on success, or Create view with errors on failure</returns>
        [Authorize(Roles = "Customer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string ProductId, int Quantity, string TotalPrice)
        {
            // Get customer ID from authentication claims
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;

            // Validate customer ID exists
            if (string.IsNullOrEmpty(customerIdClaim))
            {
                TempData["Error"] = "Customer information not found. Please log in again.";
                return RedirectToAction("Login", "Account");
            }

            // Parse TotalPrice manually with invariant culture to handle different decimal separators
            double totalPriceValue = 0;
            try
            {
                // Replace comma with period and parse using invariant culture (period as decimal separator)
                totalPriceValue = double.Parse(TotalPrice.Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse TotalPrice: {TotalPrice}", TotalPrice);
                TempData["Error"] = "Invalid price format. Please try again.";
                return RedirectToAction(nameof(Create));
            }

            // Log order details for debugging
            _logger.LogInformation("=== ORDER SUBMISSION DEBUG ===");
            _logger.LogInformation("ProductId received: {ProductId}", ProductId ?? "NULL");
            _logger.LogInformation("Quantity received: {Quantity}", Quantity);
            _logger.LogInformation("TotalPrice string received: {TotalPrice}", TotalPrice);
            _logger.LogInformation("TotalPrice parsed value: {TotalPriceValue}", totalPriceValue);

            // Create order object with parsed values
            var order = new Order
            {
                CustomerId = customerIdClaim,
                ProductId = ProductId,
                Quantity = Quantity,
                TotalPrice = totalPriceValue
            };

            _logger.LogInformation("Received order - ProductId: {ProductId}, Quantity: {Quantity}, TotalPrice: {TotalPrice}",
                ProductId, Quantity, totalPriceValue);

            // Validate product selection
            if (string.IsNullOrEmpty(ProductId))
            {
                TempData["Error"] = "Please select a product.";
                return RedirectToAction(nameof(Create));
            }

            // Validate quantity is positive
            if (Quantity <= 0)
            {
                TempData["Error"] = "Quantity must be greater than 0.";
                return RedirectToAction(nameof(Create));
            }

            // Validate total price is positive
            if (totalPriceValue <= 0)
            {
                TempData["Error"] = "Total price must be greater than 0.";
                return RedirectToAction(nameof(Create));
            }

            // Validate stock availability before processing order
            try
            {
                var product = await _tableService.GetProductByIdAsync("Product", ProductId);

                if (product == null)
                {
                    TempData["Error"] = "Product not found.";
                    return RedirectToAction(nameof(Create));
                }

                // Check if requested quantity exceeds available stock
                if (Quantity > product.StockQuantity)
                {
                    TempData["Error"] = $"Only {product.StockQuantity} items available in stock.";
                    return RedirectToAction(nameof(Create));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not validate stock for product: {ProductId}", ProductId);
                TempData["Error"] = "Unable to validate product availability.";
                return RedirectToAction(nameof(Create));
            }

            // Process order using configured method (Azure Function or direct)
            try
            {
                _logger.LogInformation("Creating new order for customer: {CustomerId}, product: {ProductId}",
                    order.CustomerId, order.ProductId);

                // Get Azure Function URL from configuration
                var functionUrl = _configuration["AzureFunctions:ProcessCompleteOrderUrl"];

                if (string.IsNullOrEmpty(functionUrl))
                {
                    // No function URL configured, use direct processing via services
                    _logger.LogWarning("Azure Function URL not configured, using direct service");
                    await ProcessOrderDirectly(order);
                    TempData["Success"] = "Order created successfully!";
                }
                else
                {
                    // Use Azure Function to process order (decoupled processing)
                    await ProcessOrderViaFunction(order, functionUrl);
                    TempData["Success"] = "Order created successfully!";
                }

                return RedirectToAction(nameof(MyOrders));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Insufficient stock"))
            {
                // Handle race condition where stock became unavailable
                _logger.LogWarning(ex, "Insufficient stock for product: {ProductId}", ProductId);
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Create));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating order");
                TempData["Error"] = "Unable to create order. Please try again.";
                return RedirectToAction(nameof(Create));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Processes order directly using service layer without Azure Functions.
        /// Updates inventory immediately, stores order, and queues monitoring messages.
        /// This is the fallback method when Azure Functions are not configured.
        /// </summary>
        /// <param name="order">Order to process</param>
        /// <exception cref="InvalidOperationException">Thrown when stock is insufficient or product not found</exception>
        private async Task ProcessOrderDirectly(Order order)
        {
            // Initialize order identifiers if not set
            if (string.IsNullOrEmpty(order.RowKey))
            {
                order.RowKey = Guid.NewGuid().ToString();
            }

            if (string.IsNullOrEmpty(order.PartitionKey))
            {
                order.PartitionKey = "Order";
            }

            // Set order metadata
            order.OrderDate = DateTime.UtcNow;
            order.Status = "Pending";

            // Update product stock IMMEDIATELY (critical section)
            try
            {
                _logger.LogInformation("Updating product stock for ProductId: {ProductId}, reducing by {Quantity}",
                    order.ProductId, order.Quantity);

                // Retrieve current product state
                var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

                if (product != null)
                {
                    int oldStock = product.StockQuantity;

                    // Double-check stock availability (race condition protection)
                    if (product.StockQuantity < order.Quantity)
                    {
                        throw new InvalidOperationException($"Insufficient stock. Available: {product.StockQuantity}, Requested: {order.Quantity}");
                    }

                    // Reduce stock by ordered quantity
                    product.StockQuantity -= order.Quantity;
                    await _tableService.UpdateProductAsync(product);

                    _logger.LogInformation("Product stock updated successfully. ProductId: {ProductId}, Old Stock: {OldStock}, New Stock: {NewStock}",
                        order.ProductId, oldStock, product.StockQuantity);
                }
                else
                {
                    throw new InvalidOperationException($"Product {order.ProductId} not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update product stock for ProductId: {ProductId}", order.ProductId);
                throw;
            }

            // Store order in Azure Table Storage
            await _tableService.InsertOrderAsync(order);

            // Small delay to ensure data consistency across Azure services
            await Task.Delay(1000);

            // Queue messages for MONITORING/LOGGING only (not for processing)
            var orderMessage = new OrderMessage
            {
                OrderId = order.RowKey,
                CustomerId = order.CustomerId,
                ProductId = order.ProductId,
                Quantity = order.Quantity,
                TotalPrice = order.TotalPrice,
                OrderDate = order.OrderDate,
                Action = "NewOrder"
            };

            await _queueService.SendOrderMessageAsync(orderMessage);

            // Queue inventory update notification
            var inventoryMessage = $"Stock reduced for order {order.RowKey} - Product: {order.ProductId}, Quantity: {order.Quantity}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
            await _queueService.SendInventoryMessageAsync(inventoryMessage);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Processes order via Azure Function for decoupled, scalable processing.
        /// Sends order data to Azure Function endpoint which handles inventory and storage.
        /// </summary>
        /// <param name="order">Order to process</param>
        /// <param name="functionUrl">URL of the Azure Function endpoint</param>
        /// <exception cref="InvalidOperationException">Thrown when Azure Function call fails</exception>
        private async Task ProcessOrderViaFunction(Order order, string functionUrl)
        {
            // Retrieve function authentication key
            var functionKey = _configuration["AzureFunctions:FunctionKey"];

            // Append function key to URL for authentication
            if (!string.IsNullOrEmpty(functionKey))
            {
                var separator = functionUrl.Contains("?") ? "&" : "?";
                functionUrl = $"{functionUrl}{separator}code={functionKey}";
            }

            // Serialize order to JSON with camelCase naming
            var jsonContent = JsonSerializer.Serialize(order, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Log function call (mask authentication key for security)
            _logger.LogInformation("Calling Azure Function at: {Url}", functionUrl.Replace(functionKey ?? "", "***"));
            var response = await _httpClient.PostAsync(functionUrl, content);

            // Handle function call failure
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Azure Function call failed: {StatusCode}, {Error}",
                    response.StatusCode, errorContent);
                throw new InvalidOperationException($"Failed to process order via Azure Function: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Order processed via Azure Function successfully: {Response}", responseContent);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays the order editing form.
        /// Customers can edit their own pending orders; Admins can only edit order status.
        /// </summary>
        /// <param name="partitionKey">Partition key of the order</param>
        /// <param name="rowKey">Row key (unique identifier) of the order</param>
        /// <returns>Edit view (CustomerEdit for customers, AdminEdit for admins)</returns>
        [Authorize(Roles = "Customer,Admin")]
        [HttpGet]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey)
        {
            // Validate parameters
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Order Edit called with null or empty parameters");
                return NotFound();
            }

            try
            {
                _logger.LogInformation("Loading order for edit: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);

                // Retrieve order from Table Storage
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order == null)
                {
                    _logger.LogWarning("Order not found for edit: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                    return NotFound();
                }

                // Customer editing: Verify ownership and provide full edit form
                if (User.IsInRole("Customer"))
                {
                    var customerIdClaim = User.FindFirst("CustomerId")?.Value;

                    // Ensure customer can only edit their own orders
                    if (order.CustomerId != customerIdClaim)
                    {
                        TempData["Error"] = "You can only edit your own orders.";
                        return RedirectToAction(nameof(MyOrders));
                    }

                    // Load products for selection dropdown
                    ViewBag.Products = await _tableService.GetAllProductsAsync();
                    return View("CustomerEdit", order);
                }

                // Admin view - only status editing allowed
                return View("AdminEdit", order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading order for edit: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                TempData["Error"] = "Unable to load order for editing.";

                // Redirect based on role
                if (User.IsInRole("Customer"))
                    return RedirectToAction(nameof(MyOrders));
                else
                    return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Processes order edit submission with role-based permissions.
        /// Customers can fully edit their orders (product, quantity, price) with automatic stock adjustments.
        /// Admins can only update order status.
        /// </summary>
        /// <param name="partitionKey">Partition key of the order</param>
        /// <param name="rowKey">Row key (unique identifier) of the order</param>
        /// <param name="order">Updated order data from form</param>
        /// <param name="newStatus">New status value for admin updates (optional)</param>
        /// <returns>Redirect to appropriate index on success, or edit view with errors on failure</returns>
        [Authorize(Roles = "Customer,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey, Order order, string? newStatus = null)
        {
            // Remove fields that are auto-generated or should not be validated
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");
            ModelState.Remove("OrderDate");
            ModelState.Remove("Timestamp");
            ModelState.Remove("ETag");

            // Validate route parameters match order data
            if (partitionKey != order.PartitionKey || rowKey != order.RowKey)
            {
                _logger.LogWarning("Route parameters don't match order data");
                return BadRequest("Route parameters don't match the order data.");
            }

            try
            {
                // Retrieve existing order from storage
                var existingOrder = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);
                if (existingOrder == null)
                {
                    return NotFound();
                }

                // Admin: Only update status (limited permissions)
                if (User.IsInRole("Admin"))
                {
                    if (!string.IsNullOrEmpty(newStatus))
                    {
                        existingOrder.Status = newStatus;
                        await _tableService.UpdateOrderAsync(existingOrder);

                        // Send update messages for audit logging
                        await SendOrderUpdateMessages(existingOrder, "AdminStatusUpdate");

                        TempData["Success"] = "Order status updated successfully!";
                        return RedirectToAction(nameof(Index));
                    }

                    return View("AdminEdit", existingOrder);
                }

                // Customer: Full edit with ownership verification
                var customerIdClaim = User.FindFirst("CustomerId")?.Value;
                if (existingOrder.CustomerId != customerIdClaim)
                {
                    TempData["Error"] = "You can only edit your own orders.";
                    return RedirectToAction(nameof(MyOrders));
                }

                if (ModelState.IsValid)
                {
                    _logger.LogInformation("Updating order: {OrderId}", order.RowKey);

                    // Handle stock adjustments if quantity or product changed
                    if (existingOrder.Quantity != order.Quantity || existingOrder.ProductId != order.ProductId)
                    {
                        // Restore stock from old order
                        var oldProduct = await _tableService.GetProductByIdAsync("Product", existingOrder.ProductId);
                        if (oldProduct != null)
                        {
                            oldProduct.StockQuantity += existingOrder.Quantity;
                            await _tableService.UpdateProductAsync(oldProduct);
                            _logger.LogInformation("Restored stock for product {ProductId}: +{Quantity}",
                                existingOrder.ProductId, existingOrder.Quantity);
                        }

                        // Reduce stock for new order
                        var newProduct = await _tableService.GetProductByIdAsync("Product", order.ProductId);
                        if (newProduct != null)
                        {
                            // Validate new stock availability
                            if (newProduct.StockQuantity < order.Quantity)
                            {
                                ModelState.AddModelError("Quantity", $"Only {newProduct.StockQuantity} items available");
                                ViewBag.Products = await _tableService.GetAllProductsAsync();
                                return View("CustomerEdit", order);
                            }

                            newProduct.StockQuantity -= order.Quantity;
                            await _tableService.UpdateProductAsync(newProduct);
                            _logger.LogInformation("Reduced stock for product {ProductId}: -{Quantity}",
                                order.ProductId, order.Quantity);

                            // Recalculate total price based on new product and quantity
                            order.TotalPrice = newProduct.Price * order.Quantity;
                            _logger.LogInformation("Recalculated total price: {TotalPrice} (Unit Price: {UnitPrice} x Quantity: {Quantity})",
                                order.TotalPrice, newProduct.Price, order.Quantity);
                        }
                    }
                    else
                    {
                        // Even if product didn't change, verify the price is correct
                        var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);
                        if (product != null)
                        {
                            var calculatedPrice = product.Price * order.Quantity;

                            // Only update if there's a significant difference (handle rounding errors)
                            if (Math.Abs(order.TotalPrice - calculatedPrice) > 0.01)
                            {
                                _logger.LogInformation("Price mismatch detected. Submitted: {SubmittedPrice}, Calculated: {CalculatedPrice}. Using calculated price.",
                                    order.TotalPrice, calculatedPrice);
                                order.TotalPrice = calculatedPrice;
                            }
                        }
                    }

                    // Update order fields in existing record
                    existingOrder.ProductId = order.ProductId;
                    existingOrder.Quantity = order.Quantity;
                    existingOrder.TotalPrice = order.TotalPrice;

                    _logger.LogInformation("Updating order {OrderId} - ProductId: {ProductId}, Quantity: {Quantity}, TotalPrice: {TotalPrice}",
                        existingOrder.RowKey, existingOrder.ProductId, existingOrder.Quantity, existingOrder.TotalPrice);

                    // Persist changes to Table Storage
                    await _tableService.UpdateOrderAsync(existingOrder);

                    // Queue update messages for monitoring
                    await SendOrderUpdateMessages(existingOrder, "CustomerUpdateOrder");

                    TempData["Success"] = "Order updated successfully!";
                    return RedirectToAction(nameof(MyOrders));
                }

                // Model validation failed - reload products and return to form
                ViewBag.Products = await _tableService.GetAllProductsAsync();
                return View("CustomerEdit", order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order: {OrderId}", order.RowKey);
                ModelState.AddModelError("", "Unable to update order. Please try again.");

                // Return appropriate view based on role
                if (User.IsInRole("Admin"))
                    return View("AdminEdit", order);
                else
                {
                    ViewBag.Products = await _tableService.GetAllProductsAsync();
                    return View("CustomerEdit", order);
                }
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays the order deletion confirmation page.
        /// Only allows customers to delete their own orders.
        /// </summary>
        /// <param name="partitionKey">Partition key of the order</param>
        /// <param name="rowKey">Row key (unique identifier) of the order</param>
        /// <returns>Delete confirmation view with order details</returns>
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<IActionResult> Delete(string partitionKey, string rowKey)
        {
            // Validate parameters
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                return NotFound();
            }

            try
            {
                // Retrieve order from Table Storage
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order == null)
                {
                    return NotFound();
                }

                // Verify customer owns this order
                var customerIdClaim = User.FindFirst("CustomerId")?.Value;
                if (order.CustomerId != customerIdClaim)
                {
                    TempData["Error"] = "You can only delete your own orders.";
                    return RedirectToAction(nameof(MyOrders));
                }

                // Load product details for display in confirmation view
                var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);
                ViewBag.Product = product;

                return View(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading order for delete");
                TempData["Error"] = "Unable to load order for deletion.";
                return RedirectToAction(nameof(MyOrders));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Order/Delete - Customer only
        [Authorize(Roles = "Customer")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                return NotFound();
            }

            try
            {
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order != null)
                {
                    // Check if customer is deleting their own order
                    var customerIdClaim = User.FindFirst("CustomerId")?.Value;
                    if (order.CustomerId != customerIdClaim)
                    {
                        TempData["Error"] = "You can only delete your own orders.";
                        return RedirectToAction(nameof(MyOrders));
                    }

                    // Restore stock when order is cancelled
                    var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);
                    if (product != null)
                    {
                        product.StockQuantity += order.Quantity;
                        await _tableService.UpdateProductAsync(product);
                        _logger.LogInformation("Stock restored for product {ProductId}: +{Quantity}", order.ProductId, order.Quantity);
                    }

                    await _tableService.DeleteOrderAsync(partitionKey, rowKey);
                    await SendOrderUpdateMessages(order, "CancelOrder");

                    TempData["Success"] = "Order deleted and stock restored successfully!";
                }
                else
                {
                    TempData["Error"] = "Order not found.";
                }

                return RedirectToAction(nameof(MyOrders));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting order");
                TempData["Error"] = "Unable to delete order. Please try again.";
                return RedirectToAction(nameof(MyOrders));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Send order update messages to queues
        private async Task SendOrderUpdateMessages(Order order, string action)
        {
            try
            {
                var queueOrderUrl = _configuration["AzureFunctions:QueueOrderMessageUrl"];
                var queueInventoryUrl = _configuration["AzureFunctions:QueueInventoryMessageUrl"];
                var functionKey = _configuration["AzureFunctions:FunctionKey"];

                // Send order message
                if (!string.IsNullOrEmpty(queueOrderUrl))
                {
                    if (!string.IsNullOrEmpty(functionKey))
                    {
                        var separator = queueOrderUrl.Contains("?") ? "&" : "?";
                        queueOrderUrl = $"{queueOrderUrl}{separator}code={functionKey}";
                    }

                    var orderMessage = new OrderMessage
                    {
                        OrderId = order.RowKey,
                        CustomerId = order.CustomerId,
                        ProductId = order.ProductId,
                        Quantity = order.Quantity,
                        TotalPrice = order.TotalPrice,
                        OrderDate = order.OrderDate,
                        Action = action
                    };

                    var jsonContent = JsonSerializer.Serialize(orderMessage, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync(queueOrderUrl, content);
                }
                else
                {
                    var orderMessage = new OrderMessage
                    {
                        OrderId = order.RowKey,
                        CustomerId = order.CustomerId,
                        ProductId = order.ProductId,
                        Quantity = order.Quantity,
                        TotalPrice = order.TotalPrice,
                        OrderDate = order.OrderDate,
                        Action = action
                    };
                    await _queueService.SendOrderMessageAsync(orderMessage);
                }

                // Send inventory message
                if (!string.IsNullOrEmpty(queueInventoryUrl))
                {
                    if (!string.IsNullOrEmpty(functionKey))
                    {
                        var separator = queueInventoryUrl.Contains("?") ? "&" : "?";
                        queueInventoryUrl = $"{queueInventoryUrl}{separator}code={functionKey}";
                    }

                    var inventoryData = new
                    {
                        orderId = order.RowKey,
                        productId = order.ProductId,
                        quantity = order.Quantity,
                        action = action == "CancelOrder" ? "Order cancelled - stock restored" : $"Order {action}"
                    };

                    var jsonContent = JsonSerializer.Serialize(inventoryData, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync(queueInventoryUrl, content);
                }
                else
                {
                    var inventoryMessage = $"{action} order {order.RowKey} - Product: {order.ProductId}, Quantity: {order.Quantity}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
                    await _queueService.SendInventoryMessageAsync(inventoryMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send queue messages for order {Action}: {OrderId}", action, order.RowKey);
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//