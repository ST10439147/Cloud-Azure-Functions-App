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
    /// HTTP-triggered Azure Functions for Order operations (Isolated Worker Model)
    /// Handles order creation, queuing, and status monitoring
    /// </summary>
    public class OrderHttpFunctions
    {
        private readonly TableService _tableService;
        private readonly QueueService _queueService;
        private readonly ILogger<OrderHttpFunctions> _logger;

        public OrderHttpFunctions(TableService tableService, QueueService queueService, ILogger<OrderHttpFunctions> logger)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Function 1: Store Order Information to Azure Tables
        [Function("StoreOrder")]
        public async Task<HttpResponseData> StoreOrder(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders")] HttpRequestData req)
        {
            _logger.LogInformation("StoreOrder function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                };
                var order = JsonConvert.DeserializeObject<Order>(requestBody, settings);

                if (order == null)
                {
                    _logger.LogWarning("Invalid order data received");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { message = "Invalid order data" });
                    return badResponse;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(order.CustomerId))
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "CustomerId is required" });
                    return response;
                }

                if (string.IsNullOrEmpty(order.ProductId))
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "ProductId is required" });
                    return response;
                }

                if (order.Quantity <= 0)
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "Quantity must be greater than 0" });
                    return response;
                }

                if (order.TotalPrice <= 0)
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "TotalPrice must be greater than 0" });
                    return response;
                }

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

                await _tableService.InsertOrderAsync(order);

                _logger.LogInformation("Order stored successfully with ID: {OrderId}", order.RowKey);

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
                _logger.LogError(ex, "Error storing order");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Function 2: Write Order Message to Queue
        [Function("QueueOrderMessage")]
        public async Task<HttpResponseData> QueueOrderMessage(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders/queue")] HttpRequestData req)
        {
            _logger.LogInformation("QueueOrderMessage function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var orderMessage = JsonConvert.DeserializeObject<OrderMessage>(requestBody);

                if (orderMessage == null)
                {
                    _logger.LogWarning("Invalid order message received");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { message = "Invalid order message data" });
                    return badResponse;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(orderMessage.OrderId))
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "OrderId is required" });
                    return response;
                }

                if (string.IsNullOrEmpty(orderMessage.CustomerId))
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "CustomerId is required" });
                    return response;
                }

                if (string.IsNullOrEmpty(orderMessage.ProductId))
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { message = "ProductId is required" });
                    return response;
                }

                await _queueService.SendOrderMessageAsync(orderMessage);

                _logger.LogInformation("Order message queued successfully for OrderId: {OrderId}", orderMessage.OrderId);

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
        // Function 3: Write Inventory Message to Queue
        [Function("QueueInventoryMessage")]
        public async Task<HttpResponseData> QueueInventoryMessage(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "inventory/queue")] HttpRequestData req)
        {
            _logger.LogInformation("QueueInventoryMessage function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);

                if (data == null)
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { message = "Invalid data" });
                    return badResponse;
                }

                string orderId = data.orderId;
                string productId = data.productId;
                int quantity = data.quantity;
                string action = data.action ?? "Processing";

                if (string.IsNullOrEmpty(orderId) || string.IsNullOrEmpty(productId) || quantity <= 0)
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        message = "OrderId, ProductId, and valid Quantity are required"
                    });
                    return badResponse;
                }

                var inventoryMessage = $"{action} order {orderId} - Product: {productId}, Quantity: {quantity}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";

                await _queueService.SendInventoryMessageAsync(inventoryMessage);

                _logger.LogInformation("Inventory message queued for OrderId: {OrderId}", orderId);

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
        // Function 4: Process Complete Order (Combined Operation)
        [Function("ProcessCompleteOrder")]
        public async Task<HttpResponseData> ProcessCompleteOrder(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders/process")] HttpRequestData req)
        {
            _logger.LogInformation("ProcessCompleteOrder function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                };
                var order = JsonConvert.DeserializeObject<Order>(requestBody, settings);

                if (order == null)
                {
                    _logger.LogWarning("Invalid order data received");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { message = "Invalid order data" });
                    return badResponse;
                }

                // Validate required fields
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

                // Step 1: Store order in Table Storage
                await _tableService.InsertOrderAsync(order);
                _logger.LogInformation("Order stored in table: {OrderId}", order.RowKey);

                // Step 2: Send order message to queue
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
                _logger.LogInformation("Order message queued: {OrderId}", order.RowKey);

                // Step 3: Send inventory message to queue
                var inventoryMessage = $"Processing order {order.RowKey} - Product: {order.ProductId}, Quantity: {order.Quantity}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
                await _queueService.SendInventoryMessageAsync(inventoryMessage);
                _logger.LogInformation("Inventory message queued: {OrderId}", order.RowKey);

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
                        orderMessageQueued = true,
                        inventoryMessageQueued = true
                    }
                });
                return successResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing complete order");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Function 5: Get Queue Status
        [Function("GetQueueStatus")]
        public async Task<HttpResponseData> GetQueueStatus(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "orders/queues/status")] HttpRequestData req)
        {
            _logger.LogInformation("GetQueueStatus function triggered");

            try
            {
                var orderQueueLength = await _queueService.GetQueueLengthAsync("ordermsg");
                var inventoryQueueLength = await _queueService.GetQueueLengthAsync("inventory-msg");

                _logger.LogInformation("Queue status - Orders: {OrderCount}, Inventory: {InventoryCount}",
                    orderQueueLength, inventoryQueueLength);

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
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//