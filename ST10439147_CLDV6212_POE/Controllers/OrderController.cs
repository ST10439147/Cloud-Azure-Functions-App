// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - Order Controller with Authentication

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Controllers
{
    [Authorize] // Require authentication for all actions
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
        // GET: Order - Lists orders based on user role
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                _logger.LogInformation("Retrieving orders");

                List<Order> orders;

                // Admin sees all orders, customers see only their own
                if (User.IsInRole("Admin"))
                {
                    orders = await _tableService.GetAllOrdersAsync();
                }
                else
                {
                    // Get customer's orders only
                    var customerIdClaim = User.FindFirst("CustomerId")?.Value;
                    if (string.IsNullOrEmpty(customerIdClaim))
                    {
                        TempData["Error"] = "Customer information not found.";
                        return RedirectToAction("Index", "Home");
                    }

                    var allOrders = await _tableService.GetAllOrdersAsync();
                    orders = allOrders.Where(o => o.CustomerId == customerIdClaim).ToList();
                }

                return View(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving orders");
                ViewBag.Error = "Unable to load orders. Please try again.";
                return View(new List<Order>());
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Order/Create - Customers can create orders
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
                    return RedirectToAction("Index", "Home");
                }

                ViewBag.Products = products;

                var order = new Order();
                return View(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading data for order creation");
                TempData["Error"] = "Unable to load order form. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Order/Create - Process customer order
        [Authorize(Roles = "Customer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Order order)
        {
            // Get logged-in customer ID
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (string.IsNullOrEmpty(customerIdClaim))
            {
                TempData["Error"] = "Unable to identify customer. Please log in again.";
                return RedirectToAction("Login", "Account");
            }

            // Set customer ID from authenticated user
            order.CustomerId = customerIdClaim;

            // Remove validation for fields set automatically
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");
            ModelState.Remove("OrderDate");
            ModelState.Remove("Status");
            ModelState.Remove("Timestamp");
            ModelState.Remove("ETag");
            ModelState.Remove("CustomerId");

            if (string.IsNullOrEmpty(order.ProductId))
            {
                ModelState.AddModelError("ProductId", "Please select a product");
            }

            if (order.Quantity <= 0)
            {
                ModelState.AddModelError("Quantity", "Quantity must be greater than 0");
            }

            if (order.TotalPrice <= 0)
            {
                ModelState.AddModelError("TotalPrice", "Total price must be greater than 0");
            }

            // Validate stock availability
            if (!string.IsNullOrEmpty(order.ProductId))
            {
                try
                {
                    var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);
                    if (product != null && order.Quantity > product.StockQuantity)
                    {
                        ModelState.AddModelError("Quantity", $"Only {product.StockQuantity} items available in stock");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not validate stock for product: {ProductId}", order.ProductId);
                }
            }

            if (ModelState.IsValid)
            {
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
                        TempData["Success"] = "Order placed successfully!";
                    }

                    return RedirectToAction("MyOrders", "Account");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating order");
                    ModelState.AddModelError("", "Unable to create order. Please try again.");
                }
            }

            // Reload data for the form if there was an error
            try
            {
                ViewBag.Products = await _tableService.GetAllProductsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reloading data after order creation failure");
                ViewBag.Error = "Unable to load product data.";
                return RedirectToAction(nameof(Index));
            }

            return View(order);
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
        // Process order directly (fallback if Azure Function unavailable)
        private async Task ProcessOrderDirectly(Order order)
        {
            // Set default values
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

            // Update product stock IMMEDIATELY (single source of truth)
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

            // Queue messages for monitoring only
            await Task.Delay(1000);

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
        // Admin-only actions below
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//

        // GET: Order/Details - View order details
        [HttpGet]
        public async Task<IActionResult> Details(string partitionKey, string rowKey)
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

                // Check authorization: customers can only view their own orders
                if (!User.IsInRole("Admin"))
                {
                    var customerIdClaim = User.FindFirst("CustomerId")?.Value;
                    if (order.CustomerId != customerIdClaim)
                    {
                        return Forbid();
                    }
                }

                // Load related data
                var customer = await _tableService.GetCustomerByIdAsync("Customer", order.CustomerId);
                var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

                ViewBag.Customer = customer;
                ViewBag.Product = product;

                return View(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading order details");
                TempData["Error"] = "Unable to load order details.";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Admin-only: Update order status
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(string partitionKey, string rowKey, string status)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey) || string.IsNullOrEmpty(status))
            {
                return BadRequest();
            }

            try
            {
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order == null)
                {
                    return NotFound();
                }

                order.Status = status;
                await _tableService.UpdateOrderAsync(order);

                TempData["Success"] = $"Order status updated to '{status}'.";
                return RedirectToAction(nameof(Details), new { partitionKey, rowKey });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order status");
                TempData["Error"] = "Unable to update order status.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//