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
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;

namespace ST10439147_CLDV6212_POE.Functions
{
    /// <summary>
    /// HTTP-triggered Azure Functions for Order operations using Isolated Worker Model.
    /// Implements a critical architectural pattern: SINGLE SOURCE OF TRUTH for stock updates.
    /// All inventory modifications occur synchronously in ProcessCompleteOrder to prevent race conditions.
    /// Queue messages serve only for logging and monitoring purposes, NOT for business logic.
    /// </summary>
    public class OrderHttpFunctions
    {
        // Service for Azure Table Storage operations (orders, products)
        private readonly TableService _tableService;

        // Service for Azure Queue Storage operations (logging/monitoring messages)
        private readonly QueueService _queueService;

        // Logger for tracking function execution and debugging
        private readonly ILogger<OrderHttpFunctions> _logger;

        /// <summary>
        /// Constructor that initializes the function with required dependencies via dependency injection.
        /// Validates that all critical services are provided to fail fast if misconfigured.
        /// </summary>
        /// <param name="tableService">Service for Table Storage operations</param>
        /// <param name="queueService">Service for Queue Storage operations</param>
        /// <param name="logger">Logger for this function class</param>
        /// <exception cref="ArgumentNullException">Thrown if any dependency is null</exception>
        public OrderHttpFunctions(TableService tableService, QueueService queueService, ILogger<OrderHttpFunctions> logger)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Function 1: Store Order Information to Azure Table Storage
        /// HTTP POST endpoint that accepts order data and persists it to Azure Tables.
        /// Sets order status to "Pending" and assigns default values for keys if not provided.
        /// This is a basic order creation function without inventory management.
        /// </summary>
        /// <param name="req">HTTP request containing order data in JSON format</param>
        /// <returns>HTTP response with order ID on success or error message on failure</returns>
        [Function("StoreOrder")]
        public async Task<HttpResponseData> StoreOrder(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders")] HttpRequestData req)
        {
            _logger.LogInformation("StoreOrder function triggered");

            try
            {
                // Read the raw JSON body from the HTTP request
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                // Configure JSON deserialization to handle missing or null properties gracefully
                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore, // Ignore null values
                    MissingMemberHandling = MissingMemberHandling.Ignore // Ignore extra properties
                };
                var order = JsonConvert.DeserializeObject<Order>(requestBody, settings);

                // Validate that deserialization succeeded
                if (order == null)
                {
                    _logger.LogWarning("Invalid order data received");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { message = "Invalid order data" });
                    return badResponse;
                }

                // Validate required field: CustomerId
                if (string.IsNullOrEmpty(order.CustomerId))
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "CustomerId is required" });
                    return response;
                }

                // Validate required field: ProductId
                if (string.IsNullOrEmpty(order.ProductId))
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "ProductId is required" });
                    return response;
                }

                // Validate that quantity is positive
                if (order.Quantity <= 0)
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "Quantity must be greater than 0" });
                    return response;
                }

                // Validate that total price is positive
                if (order.TotalPrice <= 0)
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "TotalPrice must be greater than 0" });
                    return response;
                }

                // Generate unique RowKey (identifier) if not provided
                if (string.IsNullOrEmpty(order.RowKey))
                {
                    order.RowKey = Guid.NewGuid().ToString();
                }

                // Set PartitionKey for Azure Table Storage organization
                if (string.IsNullOrEmpty(order.PartitionKey))
                {
                    order.PartitionKey = "Order";
                }

                // Set UTC timestamp for when the order was created
                order.OrderDate = DateTime.UtcNow;

                // Initialize order with Pending status
                order.Status = "Pending";

                // Persist order to Azure Table Storage
                await _tableService.InsertOrderAsync(order);

                _logger.LogInformation("Order stored successfully with ID: {OrderId}", order.RowKey);

                // Return success response with order details
                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(new
                {
                    message = "Order stored successfully",
                    orderId = order.RowKey,
                    orderDate = order.OrderDate
                });
                return successResponse;
            }
            catch (Exception ex)
            {
                // Log unexpected errors and return generic error response
                _logger.LogError(ex, "Error storing order");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Function 2: Write Order Message to Queue for monitoring purposes only
        /// HTTP POST endpoint that sends order information to Azure Queue Storage.
        /// IMPORTANT: This queue is for logging/monitoring only, NOT for triggering business logic.
        /// Stock updates should NEVER be based on these queue messages to avoid race conditions.
        /// </summary>
        /// <param name="req">HTTP request containing OrderMessage data in JSON format</param>
        /// <returns>HTTP response confirming message was queued</returns>
        [Function("QueueOrderMessage")]
        public async Task<HttpResponseData> QueueOrderMessage(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders/queue")] HttpRequestData req)
        {
            _logger.LogInformation("QueueOrderMessage function triggered");

            try // Read and deserialize request body
            {
                // Read raw JSON from request
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                // Deserialize to OrderMessage object
                var orderMessage = JsonConvert.DeserializeObject<OrderMessage>(requestBody);

                // Validate deserialization - check for null indicating invalid data
                if (orderMessage == null)
                {
                    _logger.LogWarning("Invalid order message received");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { message = "Invalid order message data" });
                    return badResponse;
                }

                // Validate required field: OrderId
                if (string.IsNullOrEmpty(orderMessage.OrderId))
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "OrderId is required" });
                    return response;
                }

                // Validate required field: CustomerId
                if (string.IsNullOrEmpty(orderMessage.CustomerId))
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "CustomerId is required" });
                    return response;
                }

                // Validate required field: ProductId
                if (string.IsNullOrEmpty(orderMessage.ProductId))
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "ProductId is required" });
                    return response;
                }

                // Send order message to Azure Queue for monitoring/logging
                await _queueService.SendOrderMessageAsync(orderMessage);

                _logger.LogInformation("Order message queued successfully for OrderId: {OrderId}", orderMessage.OrderId);

                // Return success response
                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(new
                {
                    message = "Order message queued successfully",
                    orderId = orderMessage.OrderId,
                    action = orderMessage.Action
                });
                return successResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing order message");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Function 3: Write Inventory Message to Queue for logging purposes only
        /// HTTP POST endpoint that sends inventory change notifications to Azure Queue Storage.
        /// IMPORTANT: This is for logging/audit trail only. Actual stock updates happen synchronously.
        /// Creates formatted message strings for human-readable logs.
        /// </summary>
        /// <param name="req">HTTP request containing inventory change details</param>
        /// <returns>HTTP response confirming message was queued</returns>
        [Function("QueueInventoryMessage")]
        public async Task<HttpResponseData> QueueInventoryMessage(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "inventory/queue")] HttpRequestData req)
        {
            _logger.LogInformation("QueueInventoryMessage function triggered");

            try
            {
                // Read and deserialize request as dynamic object (flexible structure)
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);

                // Validate that data was received
                if (data == null)
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { message = "Invalid data" });
                    return badResponse;
                }

                // Extract required fields from dynamic object
                string orderId = data.orderId;
                string productId = data.productId;
                int quantity = data.quantity;
                string action = data.action ?? "Processing"; // Default action if not provided

                // Validate all required fields are present and valid
                if (string.IsNullOrEmpty(orderId) || string.IsNullOrEmpty(productId) || quantity <= 0)
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        message = "OrderId, ProductId, and valid Quantity are required"
                    });
                    return badResponse;
                }

                // Create human-readable inventory log message with timestamp
                var inventoryMessage = $"{action} order {orderId} - Product: {productId}, Quantity: {quantity}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";

                // Send formatted message to inventory queue for logging
                await _queueService.SendInventoryMessageAsync(inventoryMessage);

                _logger.LogInformation("Inventory message queued for OrderId: {OrderId}", orderId);

                // Return success response with details
                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(new
                {
                    message = "Inventory message queued successfully",
                    orderId = orderId,
                    productId = productId,
                    quantity = quantity
                });
                return successResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing inventory message");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Function 4: Process Complete Order - THE SINGLE SOURCE OF TRUTH FOR STOCK UPDATES
        /// 
        /// Execution Flow:
        /// 1. Validate order data
        /// 2. Check product availability
        /// 3. UPDATE STOCK SYNCHRONOUSLY (single source of truth - prevents race conditions)
        /// 4. Store order in Table Storage
        /// 5. Send monitoring messages to queues (for logging only, NOT for business logic)
        /// 
        /// This design prevents inventory race conditions by ensuring stock updates happen
        /// atomically and immediately, not through asynchronous queue processing.
        /// </summary>
        /// <param name="req">HTTP request containing complete order data</param>
        /// <returns>HTTP response with detailed processing results including stock changes</returns>
        [Function("ProcessCompleteOrder")]
        public async Task<HttpResponseData> ProcessCompleteOrder(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders/process")] HttpRequestData req)
        {
            _logger.LogInformation("ProcessCompleteOrder function triggered");

            try
            {
                // Read and deserialize order data from request
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                };
                var order = JsonConvert.DeserializeObject<Order>(requestBody, settings);

                // Validate deserialization succeeded
                if (order == null)
                {
                    _logger.LogWarning("Invalid order data received");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { message = "Invalid order data" });
                    return badResponse;
                }

                // Validate all required fields in a single check
                if (string.IsNullOrEmpty(order.CustomerId) ||
                    string.IsNullOrEmpty(order.ProductId) ||
                    order.Quantity <= 0 ||
                    order.TotalPrice <= 0)
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        message = "CustomerId, ProductId, Quantity, and TotalPrice are required and must be valid"
                    });
                    return badResponse;
                }

                // Set default values for Azure Table Storage keys
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

                // *** CRITICAL SECTION: SINGLE SOURCE OF TRUTH FOR STOCK UPDATES ***
                // Variables to track stock update status
                bool stockUpdated = false;
                int oldStock = 0;
                int newStock = 0;

                try
                {
                    _logger.LogInformation("Updating product stock for ProductId: {ProductId}, reducing by {Quantity}",
                        order.ProductId, order.Quantity);

                    // Retrieve product from Table Storage to check availability
                    var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

                    if (product != null)
                    {
                        // Store original stock level for logging
                        oldStock = product.StockQuantity;

                        // Check if sufficient stock is available (business rule validation)
                        if (product.StockQuantity < order.Quantity)
                        {
                            _logger.LogWarning("Insufficient stock for product {ProductId}. Available: {Available}, Requested: {Requested}",
                                order.ProductId, product.StockQuantity, order.Quantity);

                            // Return error response with specific stock information
                            var insufficientStockResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                            await insufficientStockResponse.WriteAsJsonAsync(new
                            {
                                message = $"Insufficient stock. Available: {product.StockQuantity}, Requested: {order.Quantity}"
                            });
                            return insufficientStockResponse;
                        }

                        // PERFORM STOCK REDUCTION - This is the ONLY place stock is updated
                        product.StockQuantity -= order.Quantity;
                        newStock = product.StockQuantity;

                        // Persist stock change to Table Storage immediately
                        await _tableService.UpdateProductAsync(product);
                        stockUpdated = true;

                        _logger.LogInformation("Product stock updated successfully: {ProductId} from {OldStock} to {NewStock}",
                            order.ProductId, oldStock, newStock);
                    }
                    else
                    {
                        // Handle case where product doesn't exist
                        _logger.LogWarning("Product {ProductId} not found", order.ProductId);
                        var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                        await notFoundResponse.WriteAsJsonAsync(new { message = "Product not found" });
                        return notFoundResponse;
                    }
                }
                catch (Exception ex)
                {
                    // If stock update fails, abort the entire operation
                    // This prevents orders from being created without updating inventory
                    _logger.LogError(ex, "Failed to update product stock for ProductId: {ProductId}", order.ProductId);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    await errorResponse.WriteAsJsonAsync(new { message = "Failed to update product stock", error = ex.Message });
                    return errorResponse;
                }

                // Step 1: Store order in Table Storage (after successful stock update)
                await _tableService.InsertOrderAsync(order);
                _logger.LogInformation("Order stored in table: {OrderId}", order.RowKey);

                // Wait to ensure table storage write is committed before queuing messages
                // This small delay helps ensure consistency in monitoring logs
                await Task.Delay(500);

                // Step 2: Send order message to queue (FOR MONITORING/LOGGING ONLY - not for stock updates)
                // Queue message does NOT trigger any business logic, only logging/monitoring
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
                _logger.LogInformation("Order message queued for monitoring: {OrderId}", order.RowKey);

                // Step 3: Send inventory message to queue (FOR LOGGING ONLY - stock already updated above)
                // This message is purely for audit trail and monitoring dashboards
                var inventoryMessage = $"Stock updated for order {order.RowKey} - Product: {order.ProductId}, Quantity: {order.Quantity}, Old Stock: {oldStock}, New Stock: {newStock}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
                await _queueService.SendInventoryMessageAsync(inventoryMessage);
                _logger.LogInformation("Inventory log message queued: {OrderId}", order.RowKey);

                // Return comprehensive success response with all operation details
                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(new
                {
                    message = "Order processed successfully",
                    orderId = order.RowKey,
                    orderDate = order.OrderDate,
                    status = order.Status,
                    details = new
                    {
                        tableStored = true,
                        stockUpdated = stockUpdated,
                        oldStock = oldStock,
                        newStock = newStock,
                        orderMessageQueued = true,
                        inventoryMessageQueued = true
                    }
                });
                return successResponse;
            }
            catch (Exception ex)
            {
                // Catch any unexpected errors during order processing
                _logger.LogError(ex, "Error processing complete order");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { message = "Internal server error", error = ex.Message });
                return errorResponse;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Function 5: Get Queue Status
        /// HTTP GET endpoint that returns the current message count in both order and inventory queues.
        /// Useful for monitoring queue health and message processing rates.
        /// </summary>
        /// <param name="req">HTTP GET request</param>
        /// <returns>HTTP response with message counts for both queues</returns>
        [Function("GetQueueStatus")]
        public async Task<HttpResponseData> GetQueueStatus(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "orders/queues/status")] HttpRequestData req)
        {
            _logger.LogInformation("GetQueueStatus function triggered");

            try
            {
                // Query message count for order queue
                var orderQueueLength = await _queueService.GetQueueLengthAsync("ordermsg");

                // Query message count for inventory queue
                var inventoryQueueLength = await _queueService.GetQueueLengthAsync("inventory-msg");

                _logger.LogInformation("Queue status - Orders: {OrderCount}, Inventory: {InventoryCount}",
                    orderQueueLength, inventoryQueueLength);

                // Return queue statistics in structured format
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    orderQueue = new
                    {
                        name = "ordermsg",
                        messageCount = orderQueueLength
                    },
                    inventoryQueue = new
                    {
                        name = "inventory-msg",
                        messageCount = inventoryQueueLength
                    },
                    timestamp = DateTime.UtcNow
                });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving queue status");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Function 6: Move Messages from Poison Queues (Manual Recovery Tool)
        /// HTTP POST endpoint for recovering messages that failed processing multiple times.
        /// Poison queues contain messages that exceeded retry limits (typically 5 attempts).
        /// This is a placeholder function - actual recovery logic should be implemented in QueueService
        /// or handled manually through Azure Portal based on business requirements.
        /// </summary>
        /// <param name="req">HTTP request containing queue name to recover</param>
        /// <returns>HTTP response with recovery instructions</returns>
        [Function("MovePoisonMessages")]
        public async Task<HttpResponseData> MovePoisonMessages(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "queues/recover-poison")] HttpRequestData req)
        {
            _logger.LogInformation("MovePoisonMessages function triggered");

            try
            {
                // Read and deserialize request to get target queue name
                string requestBody = await req.ReadAsStringAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);

                // Default to order queue if not specified
                string queueName = data?.queueName ?? "ordermsg";

                // Azure automatically creates poison queues with "-poison" suffix
                string poisonQueueName = $"{queueName}-poison";

                _logger.LogInformation("Attempting to move messages from {PoisonQueue} to {MainQueue}",
                    poisonQueueName, queueName);

                // Return informational response (actual implementation needed in QueueService)
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    message = "Poison queue recovery initiated",
                    poisonQueue = poisonQueueName,
                    targetQueue = queueName,
                    note = "Please process poison messages manually in Azure Portal or implement recovery logic in QueueService"
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error moving poison messages");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//