// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2

using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;
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

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
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

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
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

                    // Get Azure Function URL from configuration
                    var functionUrl = _configuration["AzureFunctions:ProcessCompleteOrderUrl"];

                    if (string.IsNullOrEmpty(functionUrl))
                    {
                        _logger.LogWarning("Azure Function URL not configured, using direct service");
                        await ProcessOrderDirectly(order);
                        TempData["Success"] = "Order created successfully!";
                    }
                    else
                    {
                        await ProcessOrderViaFunction(order, functionUrl);
                        TempData["Success"] = "Order created successfully via Azure Function!";
                    }

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

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
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

            // *** FIXED: Update product stock IMMEDIATELY (single source of truth) ***
            try
            {
                _logger.LogInformation("Updating product stock for ProductId: {ProductId}, reducing by {Quantity}",
                    order.ProductId, order.Quantity);

                var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

                if (product != null)
                {
                    int oldStock = product.StockQuantity;

                    // Check stock availability
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
                throw; // Rethrow to prevent order creation if stock update fails
            }

            // Store order
            await _tableService.InsertOrderAsync(order);

            // Add delay to ensure table storage write is committed
            await Task.Delay(1000);

            // Queue messages for MONITORING/LOGGING only (not for stock updates)
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

            // Inventory message is for logging/monitoring purposes only
            var inventoryMessage = $"Stock reduced for order {order.RowKey} - Product: {order.ProductId}, Quantity: {order.Quantity}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
            await _queueService.SendInventoryMessageAsync(inventoryMessage);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        [HttpGet]
        public async Task<IActionResult> QueueStatus()
        {
            try
            {
                var functionUrl = _configuration["AzureFunctions:GetQueueStatusUrl"];

                if (!string.IsNullOrEmpty(functionUrl))
                {
                    try
                    {
                        var functionKey = _configuration["AzureFunctions:FunctionKey"];
                        if (!string.IsNullOrEmpty(functionKey))
                        {
                            var separator = functionUrl.Contains("?") ? "&" : "?";
                            functionUrl = $"{functionUrl}{separator}code={functionKey}";
                        }

                        _logger.LogInformation("Calling Azure Function for queue status at: {Url}", functionUrl.Replace(functionKey ?? "", "***"));
                        var response = await _httpClient.GetAsync(functionUrl);

                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            var statusData = JsonSerializer.Deserialize<JsonElement>(content);

                            ViewBag.OrderQueueLength = statusData.GetProperty("orderQueue").GetProperty("messageCount").GetInt32();
                            ViewBag.InventoryQueueLength = statusData.GetProperty("inventoryQueue").GetProperty("messageCount").GetInt32();
                            ViewBag.UsingFunction = true;

                            _logger.LogInformation("Queue status retrieved via Azure Function successfully");
                            return View();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to call Azure Function for queue status, falling back to direct service");
                    }
                }

                var orderQueueLength = await _queueService.GetQueueLengthAsync("ordermsg");
                var inventoryQueueLength = await _queueService.GetQueueLengthAsync("inventory-msg");

                ViewBag.OrderQueueLength = orderQueueLength;
                ViewBag.InventoryQueueLength = inventoryQueueLength;
                ViewBag.UsingFunction = false;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving queue status");
                ViewBag.Error = "Unable to retrieve queue status.";
                return View();
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        [HttpGet]
        public async Task<IActionResult> ViewQueueMessages(string queueName = "ordermsg", bool peek = false)
        {
            try
            {
                _logger.LogInformation("Attempting to retrieve messages from queue: {QueueName}, Peek mode: {Peek}", queueName, peek);

                if (peek)
                {
                    var peekMessages = await _queueService.PeekQueueMessagesAsync(queueName, 32);

                    var messageInfoList = new List<QueueMessageInfo>();
                    for (int i = 0; i < peekMessages.Count; i++)
                    {
                        messageInfoList.Add(new QueueMessageInfo
                        {
                            MessageId = $"peek-{i}",
                            MessageText = peekMessages[i],
                            PopReceipt = string.Empty,
                            InsertedOn = null,
                            ExpiresOn = null,
                            DequeueCount = 0
                        });
                    }

                    ViewBag.QueueName = queueName;
                    ViewBag.Messages = messageInfoList;
                    ViewBag.IsReadOnly = true;
                    ViewBag.MessageCount = peekMessages.Count;

                    _logger.LogInformation("Successfully peeked {Count} messages from queue: {QueueName}", peekMessages.Count, queueName);
                }
                else
                {
                    var messages = await _queueService.GetQueueMessagesAsync(queueName, 32);

                    _logger.LogInformation("Successfully retrieved {Count} messages from queue: {QueueName}",
                        messages.Count, queueName);

                    ViewBag.QueueName = queueName;
                    ViewBag.Messages = messages;
                    ViewBag.IsReadOnly = false;
                    ViewBag.MessageCount = messages.Count;
                }

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving messages from queue: {QueueName}. Details: {Message}",
                    queueName, ex.Message);

                if (ex.InnerException != null)
                {
                    _logger.LogError("Inner exception: {InnerMessage}", ex.InnerException.Message);
                }

                ViewBag.Error = $"Unable to retrieve messages from queue '{queueName}'. Error: {ex.Message}";
                ViewBag.QueueName = queueName;
                ViewBag.Messages = new List<QueueMessageInfo>();
                ViewBag.IsReadOnly = false;
                ViewBag.MessageCount = 0;

                return View();
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
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

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
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

                ViewBag.Customers = await _tableService.GetAllCustomersAsync();
                ViewBag.Products = await _tableService.GetAllProductsAsync();

                return View(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading order for edit: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                TempData["Error"] = "Unable to load order for editing.";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey, Order order)
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

            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation("Updating order: {OrderId}", order.RowKey);

                    var existingOrder = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);
                    if (existingOrder == null)
                    {
                        return NotFound();
                    }

                    // Handle stock adjustments if quantity changed
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
                                ViewBag.Customers = await _tableService.GetAllCustomersAsync();
                                ViewBag.Products = await _tableService.GetAllProductsAsync();
                                return View(order);
                            }
                            newProduct.StockQuantity -= order.Quantity;
                            await _tableService.UpdateProductAsync(newProduct);
                        }
                    }

                    existingOrder.CustomerId = order.CustomerId;
                    existingOrder.ProductId = order.ProductId;
                    existingOrder.Quantity = order.Quantity;
                    existingOrder.TotalPrice = order.TotalPrice;
                    existingOrder.Status = order.Status;

                    await _tableService.UpdateOrderAsync(existingOrder);

                    // Send update messages for logging
                    await SendOrderUpdateMessages(existingOrder, "UpdateOrder");

                    TempData["Success"] = "Order updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating order: {OrderId}", order.RowKey);
                    ModelState.AddModelError("", "Unable to update order. Please try again.");
                }
            }

            ViewBag.Customers = await _tableService.GetAllCustomersAsync();
            ViewBag.Products = await _tableService.GetAllProductsAsync();

            return View(order);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
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

                var customer = await _tableService.GetCustomerByIdAsync("Customer", order.CustomerId);
                var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

                ViewBag.Customer = customer;
                ViewBag.Product = product;

                return View(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading order for delete");
                TempData["Error"] = "Unable to load order for deletion.";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
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
                    // Restore stock when order is cancelled
                    var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);
                    if (product != null)
                    {
                        product.StockQuantity += order.Quantity;
                        await _tableService.UpdateProductAsync(product);
                        _logger.LogInformation("Stock restored for product {ProductId}: +{Quantity}", order.ProductId, order.Quantity);
                    }

                    await _tableService.DeleteOrderAsync(partitionKey, rowKey);

                    // Send cancellation messages for logging
                    await SendOrderUpdateMessages(order, "CancelOrder");

                    TempData["Success"] = "Order deleted and stock restored successfully!";
                }
                else
                {
                    TempData["Error"] = "Order not found.";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting order");
                TempData["Error"] = "Unable to delete order. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
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

                // Send inventory message for logging only
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
                        action = action == "CancelOrder" ? "Order cancelled - stock restored" : "Order updated"
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

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearQueue(string queueName)
        {
            if (string.IsNullOrEmpty(queueName))
            {
                return Json(new { success = false, message = "Queue name is required." });
            }

            try
            {
                await _queueService.ClearQueueAsync(queueName);
                return Json(new { success = true, message = $"Queue '{queueName}' cleared successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing queue: {QueueName}", queueName);
                return Json(new { success = false, message = $"Failed to clear queue: {ex.Message}" });
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        [HttpGet]
        public async Task<IActionResult> PeekQueueMessages(string queueName = "ordermsg")
        {
            try
            {
                _logger.LogInformation("Peeking messages from queue: {QueueName}", queueName);

                var messages = await _queueService.PeekQueueMessagesAsync(queueName, 32);

                _logger.LogInformation("Successfully peeked {Count} messages from queue: {QueueName}",
                    messages.Count, queueName);

                var messageInfoList = messages.Select((msg, index) => new
                {
                    Index = index,
                    MessageText = msg,
                    IsJson = msg.TrimStart().StartsWith("{")
                }).ToList();

                ViewBag.QueueName = queueName;
                ViewBag.Messages = messageInfoList;
                ViewBag.MessageCount = messages.Count;
                ViewBag.IsReadOnly = true;

                return View("PeekQueueMessages");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error peeking messages from queue: {QueueName}. Details: {Message}",
                    queueName, ex.Message);

                if (ex.InnerException != null)
                {
                    _logger.LogError("Inner exception: {InnerMessage}", ex.InnerException.Message);
                }

                ViewBag.Error = $"Unable to peek messages from queue '{queueName}'. Error: {ex.Message}";
                ViewBag.QueueName = queueName;
                ViewBag.Messages = new List<object>();
                ViewBag.MessageCount = 0;
                ViewBag.IsReadOnly = true;

                return View("PeekQueueMessages");
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//