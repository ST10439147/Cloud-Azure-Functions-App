// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2 - Azure Functions Implementation

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Functions
{
    public class OrderFunctions
    {
        private readonly TableService _tableService;
        private readonly QueueService _queueService;
        private readonly ILogger<OrderFunctions> _logger;

        public OrderFunctions(TableService tableService, QueueService queueService, ILogger<OrderFunctions> logger)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: /api/orders - Retrieve all orders
        [Function("GetAllOrders")]
        public async Task<IActionResult> GetAllOrders(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "orders")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("Retrieving all orders via Azure Function");
                var orders = await _tableService.GetAllOrdersAsync();
                return new OkObjectResult(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving orders");
                return new ObjectResult(new { error = "Unable to load orders. Please try again." })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: /api/orders/{partitionKey}/{rowKey} - Retrieve a specific order
        [Function("GetOrderById")]
        public async Task<IActionResult> GetOrderById(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "orders/{partitionKey}/{rowKey}")] HttpRequest req,
            string partitionKey,
            string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("GetOrderById called with null or empty parameters");
                return new BadRequestObjectResult(new { error = "PartitionKey and RowKey are required" });
            }

            try
            {
                _logger.LogInformation("Loading order: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order == null)
                {
                    _logger.LogWarning("Order not found: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                    return new NotFoundObjectResult(new { error = "Order not found" });
                }

                return new OkObjectResult(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading order: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                return new ObjectResult(new { error = "Unable to load order" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: /api/orders - Create a new order
        [Function("CreateOrder")]
        public async Task<IActionResult> CreateOrder(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders")] HttpRequest req)
        {
            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var order = JsonSerializer.Deserialize<Order>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (order == null)
                {
                    return new BadRequestObjectResult(new { error = "Invalid order data" });
                }

                // Validate required fields
                if (string.IsNullOrEmpty(order.CustomerId))
                {
                    return new BadRequestObjectResult(new { error = "Customer ID is required" });
                }

                if (string.IsNullOrEmpty(order.ProductId))
                {
                    return new BadRequestObjectResult(new { error = "Product ID is required" });
                }

                if (order.Quantity <= 0)
                {
                    return new BadRequestObjectResult(new { error = "Quantity must be greater than 0" });
                }

                if (order.TotalPrice <= 0)
                {
                    return new BadRequestObjectResult(new { error = "Total price must be greater than 0" });
                }

                // Validate stock availability
                try
                {
                    var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);
                    if (product != null && order.Quantity > product.StockQuantity)
                    {
                        return new BadRequestObjectResult(new { error = $"Only {product.StockQuantity} items available in stock" });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not validate stock for product: {ProductId}", order.ProductId);
                }

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

                // Insert the order
                await _tableService.InsertOrderAsync(order);
                _logger.LogInformation("Order inserted successfully with ID: {OrderId}", order.RowKey);

                // Send messages with retry logic
                var orderMessageTask = SendOrderMessageWithRetry(order);
                var inventoryMessageTask = SendInventoryMessageWithRetry(order);

                await Task.WhenAll(orderMessageTask, inventoryMessageTask);

                _logger.LogInformation("All messages sent successfully for OrderId: {OrderId}", order.RowKey);

                return new CreatedResult($"/api/orders/{order.PartitionKey}/{order.RowKey}", new
                {
                    message = "Order created successfully and queued for processing!",
                    order
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating order");
                return new ObjectResult(new { error = "Unable to create order. Please try again." })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // PUT: /api/orders/{partitionKey}/{rowKey} - Update an existing order
        [Function("UpdateOrder")]
        public async Task<IActionResult> UpdateOrder(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "orders/{partitionKey}/{rowKey}")] HttpRequest req,
            string partitionKey,
            string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                return new BadRequestObjectResult(new { error = "PartitionKey and RowKey are required" });
            }

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var order = JsonSerializer.Deserialize<Order>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (order == null)
                {
                    return new BadRequestObjectResult(new { error = "Invalid order data" });
                }

                // Ensure route parameters match the model
                if (partitionKey != order.PartitionKey || rowKey != order.RowKey)
                {
                    _logger.LogWarning("Route parameters don't match order data");
                    return new BadRequestObjectResult(new { error = "Route parameters don't match the order data" });
                }

                _logger.LogInformation("Updating order: {OrderId}", order.RowKey);

                // Get the current entity
                var existingOrder = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);
                if (existingOrder == null)
                {
                    _logger.LogWarning("Existing order not found for update: {OrderId}", rowKey);
                    return new NotFoundObjectResult(new { error = "Order not found" });
                }

                // Update editable fields but preserve system-managed fields
                existingOrder.CustomerId = order.CustomerId;
                existingOrder.ProductId = order.ProductId;
                existingOrder.Quantity = order.Quantity;
                existingOrder.TotalPrice = order.TotalPrice;
                existingOrder.Status = order.Status;

                await _tableService.UpdateOrderAsync(existingOrder);
                _logger.LogInformation("Order updated successfully: {OrderId}", order.RowKey);

                // Send update messages
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

                    var inventoryMessage = $"Order updated {existingOrder.RowKey} - Product: {existingOrder.ProductId}, New Quantity: {existingOrder.Quantity}, Status: {existingOrder.Status}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
                    await _queueService.SendInventoryMessageAsync(inventoryMessage);
                    _logger.LogInformation("Inventory update message sent for OrderId: {OrderId}", existingOrder.RowKey);
                }
                catch (Exception queueEx)
                {
                    _logger.LogWarning(queueEx, "Failed to send queue messages for order update: {OrderId}", existingOrder.RowKey);
                }

                return new OkObjectResult(new
                {
                    message = "Order updated successfully!",
                    order = existingOrder
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order: {RowKey}", rowKey);
                return new ObjectResult(new { error = "Unable to update order. Please try again." })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // DELETE: /api/orders/{partitionKey}/{rowKey} - Delete an order
        [Function("DeleteOrder")]
        public async Task<IActionResult> DeleteOrder(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "orders/{partitionKey}/{rowKey}")] HttpRequest req,
            string partitionKey,
            string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("DeleteOrder called with null or empty parameters");
                return new BadRequestObjectResult(new { error = "PartitionKey and RowKey are required" });
            }

            try
            {
                _logger.LogInformation("Deleting order: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);

                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order != null)
                {
                    await _tableService.DeleteOrderAsync(partitionKey, rowKey);
                    _logger.LogInformation("Order deleted successfully: {OrderId}", rowKey);

                    // Send cancellation messages
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

                        var inventoryMessage = $"Order cancelled {order.RowKey} - Product: {order.ProductId}, Cancelled Quantity: {order.Quantity}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
                        await _queueService.SendInventoryMessageAsync(inventoryMessage);
                        _logger.LogInformation("Inventory cancellation message sent for OrderId: {OrderId}", order.RowKey);
                    }
                    catch (Exception queueEx)
                    {
                        _logger.LogWarning(queueEx, "Failed to send queue messages for order deletion: {OrderId}", order.RowKey);
                    }

                    return new OkObjectResult(new { message = "Order deleted successfully!" });
                }
                else
                {
                    _logger.LogWarning("Order not found for deletion: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                    return new NotFoundObjectResult(new { error = "Order not found" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting order: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                return new ObjectResult(new { error = "Unable to delete order. Please try again." })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: /api/orders/queue/status - Get queue status
        [Function("GetQueueStatus")]
        public async Task<IActionResult> GetQueueStatus(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "orders/queue/status")] HttpRequest req)
        {
            try
            {
                var orderQueueLength = await _queueService.GetQueueLengthAsync("ordermsg");
                var inventoryQueueLength = await _queueService.GetQueueLengthAsync("inventory-msg");

                _logger.LogInformation("Queue status - Orders: {OrderCount}, Inventory: {InventoryCount}",
                    orderQueueLength, inventoryQueueLength);

                return new OkObjectResult(new
                {
                    orderQueueLength,
                    inventoryQueueLength
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving queue status");
                return new ObjectResult(new { error = "Unable to retrieve queue status" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: /api/orders/queue/{queueName}/messages - View messages in a queue
        [Function("ViewQueueMessages")]
        public async Task<IActionResult> ViewQueueMessages(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "orders/queue/{queueName}/messages")] HttpRequest req,
            string queueName)
        {
            try
            {
                var messages = await _queueService.PeekQueueMessagesAsync(queueName, 32);

                _logger.LogInformation("Retrieved {Count} messages from queue: {QueueName}", messages.Count, queueName);

                return new OkObjectResult(new
                {
                    queueName,
                    messageCount = messages.Count,
                    messages
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving messages from queue: {QueueName}", queueName);
                return new ObjectResult(new { error = $"Unable to retrieve messages from queue '{queueName}'" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // DELETE: /api/orders/queue/{queueName}/messages - Process/delete a message
        [Function("ProcessOrderMessage")]
        public async Task<IActionResult> ProcessOrderMessage(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "orders/queue/{queueName}/messages")] HttpRequest req,
            string queueName)
        {
            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var messageInfo = JsonSerializer.Deserialize<MessageDeleteRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (messageInfo == null || string.IsNullOrEmpty(messageInfo.MessageId) || string.IsNullOrEmpty(messageInfo.PopReceipt))
                {
                    return new BadRequestObjectResult(new { error = "Invalid message ID or pop receipt" });
                }

                await _queueService.DeleteMessageAsync(queueName, messageInfo.MessageId, messageInfo.PopReceipt);
                _logger.LogInformation("Message processed and deleted: {MessageId} from queue: {QueueName}", messageInfo.MessageId, queueName);

                return new OkObjectResult(new { message = "Message processed successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from queue: {QueueName}", queueName);
                return new ObjectResult(new { error = "Unable to process message" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: /api/orders/queue/{queueName}/clear - Clear all messages from a queue
        [Function("ClearQueue")]
        public async Task<IActionResult> ClearQueue(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders/queue/{queueName}/clear")] HttpRequest req,
            string queueName)
        {
            if (string.IsNullOrEmpty(queueName))
            {
                _logger.LogWarning("ClearQueue called with null or empty queue name");
                return new BadRequestObjectResult(new { error = "Queue name is required" });
            }

            try
            {
                _logger.LogInformation("Attempting to clear queue: {QueueName}", queueName);

                await _queueService.ClearQueueAsync(queueName);

                _logger.LogInformation("Queue cleared successfully: {QueueName}", queueName);

                return new OkObjectResult(new { message = $"Queue '{queueName}' cleared successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing queue: {QueueName}", queueName);
                return new ObjectResult(new { error = $"Failed to clear queue: {ex.Message}" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Helper method: Send order message with retry logic
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
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send order message for OrderId: {OrderId} on attempt {Attempt}",
                        order.RowKey, attempt);

                    if (attempt == maxRetries)
                    {
                        _logger.LogError(ex, "Failed to send order message after {MaxRetries} attempts for OrderId: {OrderId}",
                            maxRetries, order.RowKey);
                        throw;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                }
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Helper method: Send inventory message with retry logic
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
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send inventory message for OrderId: {OrderId} on attempt {Attempt}",
                        order.RowKey, attempt);

                    if (attempt == maxRetries)
                    {
                        _logger.LogError(ex, "Failed to send inventory message after {MaxRetries} attempts for OrderId: {OrderId}",
                            maxRetries, order.RowKey);
                        throw;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                }
            }
        }
    }

    // Helper class for message deletion requests
    public class MessageDeleteRequest
    {
        public string MessageId { get; set; } = string.Empty;
        public string PopReceipt { get; set; } = string.Empty;
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//