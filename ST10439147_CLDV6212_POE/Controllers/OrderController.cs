// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2 - Updated with Customer/Admin Role Separation

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class OrderController : Controller
    {
        private readonly TableService _tableService;
        private readonly QueueService _queueService;
        private readonly ILogger<OrderController> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

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
        // GET: Order/Index - Admin View Only
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(string status = null)
        {
            try
            {
                _logger.LogInformation("Admin retrieving all orders");

                // Get all orders
                var orders = await _tableService.GetAllOrdersAsync();

                // Filter by status if provided
                if (!string.IsNullOrEmpty(status))
                {
                    orders = orders.Where(o => o.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                // Load all customers and products for display
                var customers = await _tableService.GetAllCustomersAsync();
                var products = await _tableService.GetAllProductsAsync();

                // Create dictionaries for easy lookup
                var customerDict = customers.ToDictionary(c => c.RowKey, c => c);
                var productDict = products.ToDictionary(p => p.RowKey, p => p);

                // Pass to view
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
        // GET: Order/MyOrders - Customer View Only
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<IActionResult> MyOrders()
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;

            if (string.IsNullOrEmpty(customerIdClaim))
            {
                TempData["Error"] = "Customer information not found.";
                return RedirectToAction("Index", "Home");
            }

            try
            {
                _logger.LogInformation("Customer {CustomerId} retrieving their orders", customerIdClaim);

                // Get all orders and filter by customer ID
                var allOrders = await _tableService.GetAllOrdersAsync();
                var customerOrders = allOrders.Where(o => o.CustomerId == customerIdClaim).ToList();

                // Load product details for each order
                var productsDict = new Dictionary<string, Product>();
                foreach (var order in customerOrders)
                {
                    try
                    {
                        var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);
                        if (product != null)
                        {
                            productsDict[order.ProductId] = product;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not load product {ProductId}", order.ProductId);
                    }
                }

                ViewBag.Products = productsDict;
                return View("MyOrders", customerOrders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading orders for customer {CustomerId}", customerIdClaim);
                TempData["Error"] = "Unable to load your orders.";
                return View("MyOrders", new List<Order>());
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Order/Create - Customer Only
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            try
            {
                _logger.LogInformation("Loading data for order creation");

                var products = await _tableService.GetAllProductsAsync();

                if (!products.Any())
                {
                    TempData["Error"] = "No products available. Please check back later.";
                    return RedirectToAction(nameof(MyOrders));
                }

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
        // POST: Order/Create - Customer Only
        [Authorize(Roles = "Customer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string ProductId, int Quantity, string TotalPrice)  // Changed TotalPrice to string
        {
            // Get customer ID from claims
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;

            if (string.IsNullOrEmpty(customerIdClaim))
            {
                TempData["Error"] = "Customer information not found. Please log in again.";
                return RedirectToAction("Login", "Account");
            }

            // Parse TotalPrice manually with invariant culture
            double totalPriceValue = 0;
            try
            {
                // Try to parse with invariant culture (uses period as decimal separator)
                totalPriceValue = double.Parse(TotalPrice.Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse TotalPrice: {TotalPrice}", TotalPrice);
                TempData["Error"] = "Invalid price format. Please try again.";
                return RedirectToAction(nameof(Create));
            }

            _logger.LogInformation("=== ORDER SUBMISSION DEBUG ===");
            _logger.LogInformation("ProductId received: {ProductId}", ProductId ?? "NULL");
            _logger.LogInformation("Quantity received: {Quantity}", Quantity);
            _logger.LogInformation("TotalPrice string received: {TotalPrice}", TotalPrice);
            _logger.LogInformation("TotalPrice parsed value: {TotalPriceValue}", totalPriceValue);

            // Create order object
            var order = new Order
            {
                CustomerId = customerIdClaim,
                ProductId = ProductId,
                Quantity = Quantity,
                TotalPrice = totalPriceValue  // Use parsed value
            };

            _logger.LogInformation("Received order - ProductId: {ProductId}, Quantity: {Quantity}, TotalPrice: {TotalPrice}",
                ProductId, Quantity, totalPriceValue);

            // Validate inputs
            if (string.IsNullOrEmpty(ProductId))
            {
                TempData["Error"] = "Please select a product.";
                return RedirectToAction(nameof(Create));
            }

            if (Quantity <= 0)
            {
                TempData["Error"] = "Quantity must be greater than 0.";
                return RedirectToAction(nameof(Create));
            }

            if (totalPriceValue <= 0)
            {
                TempData["Error"] = "Total price must be greater than 0.";
                return RedirectToAction(nameof(Create));
            }

            // Validate stock availability
            try
            {
                var product = await _tableService.GetProductByIdAsync("Product", ProductId);
                if (product == null)
                {
                    TempData["Error"] = "Product not found.";
                    return RedirectToAction(nameof(Create));
                }

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

            // Process order
            try
            {
                _logger.LogInformation("Creating new order for customer: {CustomerId}, product: {ProductId}",
                    order.CustomerId, order.ProductId);

                // Get Azure Function URL from configuration
                var functionUrl = _configuration["AzureFunctions:ProcessCompleteOrderUrl"];

                if (string.IsNullOrEmpty(functionUrl))
                {
                    // No function URL configured, use direct processing
                    _logger.LogWarning("Azure Function URL not configured, using direct service");
                    await ProcessOrderDirectly(order);
                    TempData["Success"] = "Order created successfully!";
                }
                else
                {
                    // Use Azure Function to process order
                    await ProcessOrderViaFunction(order, functionUrl);
                    TempData["Success"] = "Order created successfully!";
                }

                return RedirectToAction(nameof(MyOrders));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Insufficient stock"))
            {
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
        // Process order directly
        private async Task ProcessOrderDirectly(Order order)
        {
            if (string.IsNullOrEmpty(order.RowKey))
            {
                order.RowKey = Guid.NewGuid().ToString();
            }

            if (string.IsNullOrEmpty(order.PartitionKey))
            {
                order.PartitionKey = "Order";
            }

            order.OrderDate = DateTime.UtcNow;
            order.Status = "Pending";

            // Update product stock IMMEDIATELY
            try
            {
                _logger.LogInformation("Updating product stock for ProductId: {ProductId}, reducing by {Quantity}",
                    order.ProductId, order.Quantity);

                var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

                if (product != null)
                {
                    int oldStock = product.StockQuantity;

                    if (product.StockQuantity < order.Quantity)
                    {
                        throw new InvalidOperationException($"Insufficient stock. Available: {product.StockQuantity}, Requested: {order.Quantity}");
                    }

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

            // Store order
            await _tableService.InsertOrderAsync(order);
            await Task.Delay(1000);

            // Queue messages for MONITORING/LOGGING only
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

            var inventoryMessage = $"Stock reduced for order {order.RowKey} - Product: {order.ProductId}, Quantity: {order.Quantity}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
            await _queueService.SendInventoryMessageAsync(inventoryMessage);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Process order via Azure Function
        private async Task ProcessOrderViaFunction(Order order, string functionUrl)
        {
            var functionKey = _configuration["AzureFunctions:FunctionKey"];

            if (!string.IsNullOrEmpty(functionKey))
            {
                var separator = functionUrl.Contains("?") ? "&" : "?";
                functionUrl = $"{functionUrl}{separator}code={functionKey}";
            }

            var jsonContent = JsonSerializer.Serialize(order, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger.LogInformation("Calling Azure Function at: {Url}", functionUrl.Replace(functionKey ?? "", "***"));
            var response = await _httpClient.PostAsync(functionUrl, content);

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
        // GET: Order/Edit - Customer can edit their own orders, Admin can only edit status
        [Authorize(Roles = "Customer,Admin")]
        [HttpGet]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Order Edit called with null or empty parameters");
                return NotFound();
            }

            try
            {
                _logger.LogInformation("Loading order for edit: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order == null)
                {
                    _logger.LogWarning("Order not found for edit: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                    return NotFound();
                }

                // Check if customer is editing their own order
                if (User.IsInRole("Customer"))
                {
                    var customerIdClaim = User.FindFirst("CustomerId")?.Value;
                    if (order.CustomerId != customerIdClaim)
                    {
                        TempData["Error"] = "You can only edit your own orders.";
                        return RedirectToAction(nameof(MyOrders));
                    }

                    ViewBag.Products = await _tableService.GetAllProductsAsync();
                    return View("CustomerEdit", order);
                }

                // Admin view - only status editing
                return View("AdminEdit", order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading order for edit: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                TempData["Error"] = "Unable to load order for editing.";

                if (User.IsInRole("Customer"))
                    return RedirectToAction(nameof(MyOrders));
                else
                    return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Order/Edit - Customer full edit, Admin status only
        [Authorize(Roles = "Customer,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey, Order order, string? newStatus = null)
        {
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");
            ModelState.Remove("OrderDate");
            ModelState.Remove("Timestamp");
            ModelState.Remove("ETag");

            if (partitionKey != order.PartitionKey || rowKey != order.RowKey)
            {
                _logger.LogWarning("Route parameters don't match order data");
                return BadRequest("Route parameters don't match the order data.");
            }

            try
            {
                var existingOrder = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);
                if (existingOrder == null)
                {
                    return NotFound();
                }

                // Admin: Only update status
                if (User.IsInRole("Admin"))
                {
                    if (!string.IsNullOrEmpty(newStatus))
                    {
                        existingOrder.Status = newStatus;
                        await _tableService.UpdateOrderAsync(existingOrder);

                        // Send update messages for logging
                        await SendOrderUpdateMessages(existingOrder, "AdminStatusUpdate");

                        TempData["Success"] = "Order status updated successfully!";
                        return RedirectToAction(nameof(Index));
                    }

                    return View("AdminEdit", existingOrder);
                }

                // Customer: Full edit (but only their own orders)
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
                        // Restore old stock
                        var oldProduct = await _tableService.GetProductByIdAsync("Product", existingOrder.ProductId);
                        if (oldProduct != null)
                        {
                            oldProduct.StockQuantity += existingOrder.Quantity;
                            await _tableService.UpdateProductAsync(oldProduct);
                        }

                        // Reduce new stock
                        var newProduct = await _tableService.GetProductByIdAsync("Product", order.ProductId);
                        if (newProduct != null)
                        {
                            if (newProduct.StockQuantity < order.Quantity)
                            {
                                ModelState.AddModelError("Quantity", $"Only {newProduct.StockQuantity} items available");
                                ViewBag.Products = await _tableService.GetAllProductsAsync();
                                return View("CustomerEdit", order);
                            }
                            newProduct.StockQuantity -= order.Quantity;
                            await _tableService.UpdateProductAsync(newProduct);
                        }
                    }

                    existingOrder.ProductId = order.ProductId;
                    existingOrder.Quantity = order.Quantity;
                    existingOrder.TotalPrice = order.TotalPrice;

                    await _tableService.UpdateOrderAsync(existingOrder);
                    await SendOrderUpdateMessages(existingOrder, "CustomerUpdateOrder");

                    TempData["Success"] = "Order updated successfully!";
                    return RedirectToAction(nameof(MyOrders));
                }

                ViewBag.Products = await _tableService.GetAllProductsAsync();
                return View("CustomerEdit", order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order: {OrderId}", order.RowKey);
                ModelState.AddModelError("", "Unable to update order. Please try again.");

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
        // GET: Order/Delete - Customer only
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<IActionResult> Delete(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                return NotFound();
            }

            try
            {
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order == null)
                {
                    return NotFound();
                }

                // Check if customer is deleting their own order
                var customerIdClaim = User.FindFirst("CustomerId")?.Value;
                if (order.CustomerId != customerIdClaim)
                {
                    TempData["Error"] = "You can only delete your own orders.";
                    return RedirectToAction(nameof(MyOrders));
                }

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