// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;
using System;
using System.Threading.Tasks;

namespace ST10439147_CLDV6212_POE.Functions
{
    /// <summary>
    /// Queue-triggered Azure Functions for processing queued messages
    /// Handles order processing and inventory management messages
    /// </summary>
    public class OrderQueueFunctions
    {
        private readonly TableService _tableService;
        private readonly ILogger<OrderQueueFunctions> _logger;

        public OrderQueueFunctions(TableService tableService, ILogger<OrderQueueFunctions> logger)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Queue Trigger Function 1: Process Order Messages
        // Triggered automatically when messages appear in the "ordermsg" queue
        // Processes order-related actions (NewOrder, UpdateOrder, CancelOrder)
        [FunctionName("ProcessOrderQueue")]
        public async Task ProcessOrderQueue(
            [QueueTrigger("ordermsg", Connection = "AzureStorage:ConnectionString")] string queueMessage,
            ILogger log)
        {
            log.LogInformation("ProcessOrderQueue triggered with message: {Message}", queueMessage);

            try
            {
                // Deserialize the order message
                var orderMessage = JsonConvert.DeserializeObject<OrderMessage>(queueMessage);

                if (orderMessage == null)
                {
                    log.LogWarning("Failed to deserialize order message");
                    return;
                }

                log.LogInformation("Processing {Action} for OrderId: {OrderId}",
                    orderMessage.Action, orderMessage.OrderId);

                // Process based on action type
                switch (orderMessage.Action?.ToUpper())
                {
                    case "NEWORDER":
                        await ProcessNewOrder(orderMessage, log);
                        break;

                    case "UPDATEORDER":
                        await ProcessOrderUpdate(orderMessage, log);
                        break;

                    case "CANCELORDER":
                        await ProcessOrderCancellation(orderMessage, log);
                        break;

                    default:
                        log.LogWarning("Unknown action type: {Action} for OrderId: {OrderId}",
                            orderMessage.Action, orderMessage.OrderId);
                        break;
                }

                log.LogInformation("Successfully processed order message for OrderId: {OrderId}",
                    orderMessage.OrderId);
            }
            catch (JsonException jsonEx)
            {
                log.LogError(jsonEx, "Error deserializing order message: {Message}", queueMessage);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error processing order queue message: {Message}", queueMessage);
                // Consider implementing dead-letter queue or retry logic here
                throw; // Re-throw to trigger Azure Functions retry policy
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Queue Trigger Function 2: Process Inventory Messages
        // Triggered automatically when messages appear in the "inventory-msg" queue
        // Processes inventory-related updates and logs them
        [FunctionName("ProcessInventoryQueue")]
        public async Task ProcessInventoryQueue(
            [QueueTrigger("inventory-msg", Connection = "AzureStorage:ConnectionString")] string inventoryMessage,
            ILogger log)
        {
            log.LogInformation("ProcessInventoryQueue triggered with message: {Message}", inventoryMessage);

            try
            {
                // Parse the inventory message to extract details
                // Expected format: "Action order OrderId - Product: ProductId, Quantity: X, Timestamp: ..."
                var messageParts = inventoryMessage.Split(new[] { " - ", ": ", ", " }, StringSplitOptions.None);

                if (messageParts.Length >= 4)
                {
                    var action = messageParts[0];
                    var orderInfo = messageParts[1].Replace("order ", "");
                    var productId = messageParts[2].Replace("Product", "").Trim();
                    var quantityStr = messageParts[3].Replace("Quantity", "").Trim();

                    log.LogInformation("Inventory Update - Action: {Action}, Order: {OrderId}, Product: {ProductId}, Quantity: {Quantity}",
                        action, orderInfo, productId, quantityStr);

                    // Here you could implement actual inventory management logic:
                    // - Update product stock quantities in Table Storage
                    // - Send notifications for low stock
                    // - Generate inventory reports
                    // - Trigger reorder processes

                    // Example: Update product stock
                    if (int.TryParse(quantityStr, out int quantity) && !string.IsNullOrEmpty(productId))
                    {
                        await UpdateProductStock(productId, quantity, action, log);
                    }
                }
                else
                {
                    log.LogWarning("Inventory message format unexpected: {Message}", inventoryMessage);
                }

                log.LogInformation("Successfully processed inventory message");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error processing inventory queue message: {Message}", inventoryMessage);
                // Consider implementing dead-letter queue or retry logic here
                throw; // Re-throw to trigger Azure Functions retry policy
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Helper method: Process new order
        private async Task ProcessNewOrder(OrderMessage orderMessage, ILogger log)
        {
            try
            {
                // Verify the order exists in the table
                var order = await _tableService.GetOrderByIdAsync("Order", orderMessage.OrderId);

                if (order != null)
                {
                    // Update order status to "Processing"
                    order.Status = "Processing";
                    await _tableService.UpdateOrderAsync(order);

                    log.LogInformation("New order {OrderId} status updated to Processing", orderMessage.OrderId);

                    // Additional business logic could be added here:
                    // - Send confirmation email to customer
                    // - Notify warehouse system
                    // - Calculate shipping costs
                    // - Validate payment
                }
                else
                {
                    log.LogWarning("Order {OrderId} not found in table storage", orderMessage.OrderId);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error processing new order: {OrderId}", orderMessage.OrderId);
                throw;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Helper method: Process order update
        private async Task ProcessOrderUpdate(OrderMessage orderMessage, ILogger log)
        {
            try
            {
                var order = await _tableService.GetOrderByIdAsync("Order", orderMessage.OrderId);

                if (order != null)
                {
                    // Log the update
                    log.LogInformation("Order {OrderId} updated - Previous Status: {Status}",
                        orderMessage.OrderId, order.Status);

                    // Additional business logic could be added here:
                    // - Send update notification to customer
                    // - Update related systems
                    // - Log audit trail
                    // - Check for stock availability changes
                }
                else
                {
                    log.LogWarning("Order {OrderId} not found for update", orderMessage.OrderId);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error processing order update: {OrderId}", orderMessage.OrderId);
                throw;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Helper method: Process order cancellation
        private async Task ProcessOrderCancellation(OrderMessage orderMessage, ILogger log)
        {
            try
            {
                log.LogInformation("Processing cancellation for order {OrderId}", orderMessage.OrderId);

                // Additional business logic for cancellation:
                // - Restore product stock quantities
                // - Process refunds
                // - Send cancellation confirmation
                // - Update related orders or subscriptions
                // - Notify relevant departments

                // Example: Restore stock
                if (!string.IsNullOrEmpty(orderMessage.ProductId) && orderMessage.Quantity > 0)
                {
                    var product = await _tableService.GetProductByIdAsync("Product", orderMessage.ProductId);
                    if (product != null)
                    {
                        product.StockQuantity += orderMessage.Quantity;
                        await _tableService.UpdateProductAsync(product);
                        log.LogInformation("Stock restored for product {ProductId}: +{Quantity}",
                            orderMessage.ProductId, orderMessage.Quantity);
                    }
                }

                log.LogInformation("Order cancellation processed for {OrderId}", orderMessage.OrderId);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error processing order cancellation: {OrderId}", orderMessage.OrderId);
                throw;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Helper method: Update product stock based on inventory message
        private async Task UpdateProductStock(string productId, int quantity, string action, ILogger log)
        {
            try
            {
                var product = await _tableService.GetProductByIdAsync("Product", productId);

                if (product != null)
                {
                    // Determine stock adjustment based on action
                    if (action.Contains("Processing", StringComparison.OrdinalIgnoreCase) ||
                        action.Contains("NewOrder", StringComparison.OrdinalIgnoreCase))
                    {
                        // Decrease stock for new orders
                        product.StockQuantity -= quantity;
                        log.LogInformation("Stock decreased for product {ProductId}: -{Quantity} (New: {NewStock})",
                            productId, quantity, product.StockQuantity);
                    }
                    else if (action.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
                    {
                        // Restore stock for cancelled orders
                        product.StockQuantity += quantity;
                        log.LogInformation("Stock restored for product {ProductId}: +{Quantity} (New: {NewStock})",
                            productId, quantity, product.StockQuantity);
                    }

                    // Check for low stock warning
                    if (product.StockQuantity < 10)
                    {
                        log.LogWarning("Low stock alert for product {ProductId}: {StockQuantity} remaining",
                            productId, product.StockQuantity);
                    }

                    // Prevent negative stock
                    if (product.StockQuantity < 0)
                    {
                        log.LogError("Negative stock detected for product {ProductId}: {StockQuantity}",
                            productId, product.StockQuantity);
                        product.StockQuantity = 0;
                    }

                    await _tableService.UpdateProductAsync(product);
                }
                else
                {
                    log.LogWarning("Product {ProductId} not found for stock update", productId);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error updating product stock for {ProductId}", productId);
                throw;
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//