using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class OrderController : Controller
    {
        private readonly TableService _tableService;
        private readonly QueueService _queueService;
        private readonly ILogger<OrderController> _logger;

        public OrderController(TableService tableService, QueueService queueService, ILogger<OrderController> logger)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                _logger.LogInformation("Retrieving all orders");
                var orders = await _tableService.GetAllOrdersAsync();
                return View(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving orders");
                ViewBag.Error = "Unable to load orders. Please try again.";
                return View(new List<Order>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            try
            {
                _logger.LogInformation("Loading data for order creation");

                var customers = await _tableService.GetAllCustomersAsync();
                var products = await _tableService.GetAllProductsAsync();

                if (!customers.Any())
                {
                    TempData["Error"] = "No customers available. Please add customers first.";
                    return RedirectToAction("Index", "Customer");
                }

                if (!products.Any())
                {
                    TempData["Error"] = "No products available. Please add products first.";
                    return RedirectToAction("Index", "Product");
                }

                ViewBag.Customers = customers;
                ViewBag.Products = products;

                // Initialize with default values
                var order = new Order();
                return View(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading data for order creation");
                TempData["Error"] = "Unable to load data for order creation. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Order order)
        {
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");
            ModelState.Remove("OrderDate");
            ModelState.Remove("Status");
            ModelState.Remove("Timestamp");
            ModelState.Remove("ETag");

            _logger.LogInformation("Received order - TotalPrice: {TotalPrice}, Type: {Type}",
                order.TotalPrice, order.TotalPrice.GetType().Name);

            if (string.IsNullOrEmpty(order.CustomerId))
            {
                ModelState.AddModelError("CustomerId", "Please select a customer");
            }

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

                    // Set order defaults
                    order.OrderDate = DateTime.UtcNow;
                    order.Status = "Pending";

                    if (string.IsNullOrEmpty(order.RowKey))
                    {
                        order.RowKey = Guid.NewGuid().ToString();
                    }

                    if (string.IsNullOrEmpty(order.PartitionKey))
                    {
                        order.PartitionKey = "Order";
                    }

                    // Insert the order first
                    await _tableService.InsertOrderAsync(order);
                    _logger.LogInformation("Order inserted successfully with ID: {OrderId}", order.RowKey);

                    // Create tasks for concurrent message sending
                    var orderMessageTask = SendOrderMessageWithRetry(order);
                    var inventoryMessageTask = SendInventoryMessageWithRetry(order);

                    // Wait for both messages to be sent
                    await Task.WhenAll(orderMessageTask, inventoryMessageTask);

                    _logger.LogInformation("All messages sent successfully for OrderId: {OrderId}", order.RowKey);

                    TempData["Success"] = "Order created successfully and queued for processing!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating order");
                    ModelState.AddModelError("", "Unable to create order. Please try again.");
                }
            }
            else
            {
                // Log validation errors for debugging
                foreach (var modelState in ModelState)
                {
                    foreach (var error in modelState.Value.Errors)
                    {
                        _logger.LogWarning("Model validation error for {Key}: {Error}",
                            modelState.Key, error.ErrorMessage);
                    }
                }
            }

            // Reload data for the form if there was an error
            try
            {
                ViewBag.Customers = await _tableService.GetAllCustomersAsync();
                ViewBag.Products = await _tableService.GetAllProductsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reloading data after order creation failure");
                ViewBag.Error = "Unable to load data for order creation.";
                return RedirectToAction(nameof(Index));
            }

            return View(order);
        }

        private async Task SendOrderMessageWithRetry(Order order, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
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
                    _logger.LogInformation("Order message sent successfully for OrderId: {OrderId} on attempt {Attempt}",
                        order.RowKey, attempt);
                    return; // Success, exit retry loop
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send order message for OrderId: {OrderId} on attempt {Attempt}",
                        order.RowKey, attempt);

                    if (attempt == maxRetries)
                    {
                        _logger.LogError(ex, "Failed to send order message after {MaxRetries} attempts for OrderId: {OrderId}",
                            maxRetries, order.RowKey);
                        throw; // Re-throw on final attempt
                    }

                    // Wait before retry (exponential backoff)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                }
            }
        }

        private async Task SendInventoryMessageWithRetry(Order order, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var inventoryMessage = $"Processing order {order.RowKey} - Product: {order.ProductId}, Quantity: {order.Quantity}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
                    await _queueService.SendInventoryMessageAsync(inventoryMessage);
                    _logger.LogInformation("Inventory message sent successfully for OrderId: {OrderId} on attempt {Attempt}",
                        order.RowKey, attempt);
                    return; // Success, exit retry loop
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send inventory message for OrderId: {OrderId} on attempt {Attempt}",
                        order.RowKey, attempt);

                    if (attempt == maxRetries)
                    {
                        _logger.LogError(ex, "Failed to send inventory message after {MaxRetries} attempts for OrderId: {OrderId}",
                            maxRetries, order.RowKey);
                        throw; // Re-throw on final attempt
                    }

                    // Wait before retry (exponential backoff)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                }
            }
        }

        // Additional action to view queue status (useful for monitoring)
        [HttpGet]
        public async Task<IActionResult> QueueStatus()
        {
            try
            {
                var orderQueueLength = await _queueService.GetQueueLengthAsync("ordermsg");
                var inventoryQueueLength = await _queueService.GetQueueLengthAsync("inventory-msg");

                ViewBag.OrderQueueLength = orderQueueLength;
                ViewBag.InventoryQueueLength = inventoryQueueLength;

                _logger.LogInformation("Queue status - Orders: {OrderCount}, Inventory: {InventoryCount}",
                    orderQueueLength, inventoryQueueLength);

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving queue status");
                ViewBag.Error = "Unable to retrieve queue status.";
                return View();
            }
        }

        // Action to view queue messages (for debugging/monitoring)
        [HttpGet]
        public async Task<IActionResult> ViewQueueMessages(string queueName = "ordermsg")
        {
            try
            {
                var messages = await _queueService.PeekQueueMessagesAsync(queueName, 50);
                ViewBag.QueueName = queueName;
                ViewBag.Messages = messages;

                _logger.LogInformation("Retrieved {Count} messages from queue: {QueueName}", messages.Count, queueName);
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving messages from queue: {QueueName}", queueName);
                ViewBag.Error = $"Unable to retrieve messages from queue '{queueName}'.";
                return View();
            }
        }

        // Optional: Action to process queue messages manually (for testing)
        [HttpPost]
        public async Task<IActionResult> ProcessOrderMessage(string messageId, string popReceipt, string queueName = "ordermsg")
        {
            try
            {
                if (!string.IsNullOrEmpty(messageId) && !string.IsNullOrEmpty(popReceipt))
                {
                    await _queueService.DeleteMessageAsync(queueName, messageId, popReceipt);
                    TempData["Success"] = "Message processed successfully!";
                    _logger.LogInformation("Message processed and deleted: {MessageId} from queue: {QueueName}", messageId, queueName);
                }
                else
                {
                    TempData["Error"] = "Invalid message ID or pop receipt.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message: {MessageId} from queue: {QueueName}", messageId, queueName);
                TempData["Error"] = "Unable to process message.";
            }

            return RedirectToAction(nameof(ViewQueueMessages), new { queueName });
        }

        // GET: Order/Details/5
        public async Task<IActionResult> Details(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Order Details called with null or empty parameters");
                return NotFound();
            }

            try
            {
                _logger.LogInformation("Retrieving order details for PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order == null)
                {
                    _logger.LogWarning("Order not found: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                    return NotFound();
                }

                // Load related data for display
                try
                {
                    var customer = await _tableService.GetCustomerByIdAsync("Customer", order.CustomerId);
                    var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

                    ViewBag.Customer = customer;
                    ViewBag.Product = product;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error loading related data for order: {OrderId}", rowKey);
                    // Continue without related data
                }

                return View(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving order details for PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                ViewBag.Error = "Unable to load order details.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Order/Edit/5
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

                // Load customers and products for dropdowns
                try
                {
                    ViewBag.Customers = await _tableService.GetAllCustomersAsync();
                    ViewBag.Products = await _tableService.GetAllProductsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading data for order edit");
                    ViewBag.Error = "Unable to load required data for editing.";
                    return View(order);
                }

                return View(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading order for edit: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                TempData["Error"] = "Unable to load order for editing.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Order/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey, Order order)
        {
            
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");
            ModelState.Remove("OrderDate");
            ModelState.Remove("Timestamp");
            ModelState.Remove("ETag");

            // Ensure the route parameters match the model
            if (partitionKey != order.PartitionKey || rowKey != order.RowKey)
            {
                _logger.LogWarning("Route parameters don't match order data. Route: {RoutePartition}/{RouteRow}, Model: {ModelPartition}/{ModelRow}",
                    partitionKey, rowKey, order.PartitionKey, order.RowKey);
                return BadRequest("Route parameters don't match the order data.");
            }

            // Remove auto-generated fields from ModelState validation
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");
            ModelState.Remove("OrderDate"); // OrderDate should not be changed after creation

            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation("Updating order: {OrderId}", order.RowKey);

                    // Get the current entity to ensure we have the latest ETag and preserve certain fields
                    var existingOrder = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);
                    if (existingOrder == null)
                    {
                        _logger.LogWarning("Existing order not found for update: {OrderId}", rowKey);
                        return NotFound();
                    }

                    // Update editable fields but preserve system-managed fields
                    existingOrder.CustomerId = order.CustomerId;
                    existingOrder.ProductId = order.ProductId;
                    existingOrder.Quantity = order.Quantity;
                    existingOrder.TotalPrice = order.TotalPrice;
                    existingOrder.Status = order.Status;
                    // Keep original OrderDate - don't allow editing
                    // Keep original ETag and Timestamp

                    await _tableService.UpdateOrderAsync(existingOrder);
                    _logger.LogInformation("Order updated successfully: {OrderId}", order.RowKey);

                    // Send updated order message to queue
                    try
                    {
                        var orderMessage = new OrderMessage
                        {
                            OrderId = existingOrder.RowKey,
                            CustomerId = existingOrder.CustomerId,
                            ProductId = existingOrder.ProductId,
                            Quantity = existingOrder.Quantity,
                            TotalPrice = existingOrder.TotalPrice,
                            OrderDate = existingOrder.OrderDate,
                            Action = "UpdateOrder"
                        };

                        await _queueService.SendOrderMessageAsync(orderMessage);
                        _logger.LogInformation("Order update message sent to queue for OrderId: {OrderId}", existingOrder.RowKey);

                        // Send inventory update message
                        var inventoryMessage = $"Order updated {existingOrder.RowKey} - Product: {existingOrder.ProductId}, New Quantity: {existingOrder.Quantity}, Status: {existingOrder.Status}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
                        await _queueService.SendInventoryMessageAsync(inventoryMessage);
                        _logger.LogInformation("Inventory update message sent for OrderId: {OrderId}", existingOrder.RowKey);
                    }
                    catch (Exception queueEx)
                    {
                        _logger.LogWarning(queueEx, "Failed to send queue messages for order update: {OrderId}", existingOrder.RowKey);
                        // Don't fail the entire operation if queue operations fail
                    }

                    TempData["Success"] = "Order updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating order: {OrderId}", order.RowKey);
                    ModelState.AddModelError("", "Unable to update order. Please try again.");
                }
            }
            else
            {
                // Log validation errors
                foreach (var modelError in ModelState.SelectMany(x => x.Value.Errors))
                {
                    _logger.LogWarning("Model validation error in Edit: {Error}", modelError.ErrorMessage);
                }
            }

            // Reload data for the form if there was an error
            try
            {
                ViewBag.Customers = await _tableService.GetAllCustomersAsync();
                ViewBag.Products = await _tableService.GetAllProductsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reloading data after order update failure");
                ViewBag.Error = "Unable to load required data for editing.";
            }

            return View(order);
        }

        // GET: Order/Delete/5
        [HttpGet]
        public async Task<IActionResult> Delete(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Order Delete called with null or empty parameters");
                return NotFound();
            }

            try
            {
                _logger.LogInformation("Loading order for delete confirmation: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order == null)
                {
                    _logger.LogWarning("Order not found for delete: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                    return NotFound();
                }

                // Load related data for display
                try
                {
                    var customer = await _tableService.GetCustomerByIdAsync("Customer", order.CustomerId);
                    var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

                    ViewBag.Customer = customer;
                    ViewBag.Product = product;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error loading related data for order deletion: {OrderId}", rowKey);
                    // Continue without related data
                }

                return View(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading order for delete confirmation: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                TempData["Error"] = "Unable to load order for deletion.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Order/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Order DeleteConfirmed called with null or empty parameters");
                return NotFound();
            }

            try
            {
                _logger.LogInformation("Deleting order: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);

                // Get the order details before deletion for queue messaging
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order != null)
                {
                    // Delete the order from table storage
                    await _tableService.DeleteOrderAsync(partitionKey, rowKey);
                    _logger.LogInformation("Order deleted successfully: {OrderId}", rowKey);

                    // Send cancellation message to queue
                    try
                    {
                        var orderMessage = new OrderMessage
                        {
                            OrderId = order.RowKey,
                            CustomerId = order.CustomerId,
                            ProductId = order.ProductId,
                            Quantity = order.Quantity,
                            TotalPrice = order.TotalPrice,
                            OrderDate = order.OrderDate,
                            Action = "CancelOrder"
                        };

                        await _queueService.SendOrderMessageAsync(orderMessage);
                        _logger.LogInformation("Order cancellation message sent to queue for OrderId: {OrderId}", order.RowKey);

                        // Send inventory cancellation message
                        var inventoryMessage = $"Order cancelled {order.RowKey} - Product: {order.ProductId}, Cancelled Quantity: {order.Quantity}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
                        await _queueService.SendInventoryMessageAsync(inventoryMessage);
                        _logger.LogInformation("Inventory cancellation message sent for OrderId: {OrderId}", order.RowKey);
                    }
                    catch (Exception queueEx)
                    {
                        _logger.LogWarning(queueEx, "Failed to send queue messages for order deletion: {OrderId}", order.RowKey);
                        // Don't fail the entire operation if queue operations fail
                    }

                    TempData["Success"] = "Order deleted successfully!";
                }
                else
                {
                    _logger.LogWarning("Order not found for deletion: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                    TempData["Error"] = "Order not found.";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting order: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                TempData["Error"] = "Unable to delete order. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}