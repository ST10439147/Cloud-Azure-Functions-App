// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;
using System;
using System.Threading.Tasks;

namespace ST10439147_CLDV6212_POE.Functions
{
    /// <summary>
    /// Queue-triggered Azure Functions for processing queued messages (Isolated Worker Model)
    /// PURPOSE: Monitoring, logging, and alerting only
    /// CRITICAL: These functions DO NOT update stock - stock is updated in ProcessCompleteOrder
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
        // Queue Trigger Function 1: Process Order Messages (for monitoring/logging only)
        [Function("ProcessOrderQueue")]
        public async Task ProcessOrderQueue(
            [QueueTrigger("ordermsg", Connection = "AzureWebJobsStorage")] string queueMessage)
        {
            _logger.LogInformation("ProcessOrderQueue triggered - MONITORING MODE");

            try
            {
                // Validate message
                if (string.IsNullOrWhiteSpace(queueMessage))
                {
                    _logger.LogInformation("Empty message, exiting gracefully");
                    return;
                }

                if (!queueMessage.TrimStart().StartsWith("{"))
                {
                    _logger.LogInformation("Non-JSON message: {Message}", queueMessage);
                    return;
                }

                OrderMessage orderMessage = null;
                try
                {
                    orderMessage = JsonConvert.DeserializeObject<OrderMessage>(queueMessage);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "JSON deserialization failed, exiting gracefully");
                    return;
                }

                if (orderMessage == null || string.IsNullOrEmpty(orderMessage.OrderId))
                {
                    _logger.LogInformation("Invalid order message structure, exiting gracefully");
                    return;
                }

                _logger.LogInformation("=== ORDER MONITORING ===");
                _logger.LogInformation("Action: {Action}", orderMessage.Action ?? "UNKNOWN");
                _logger.LogInformation("OrderId: {OrderId}", orderMessage.OrderId);
                _logger.LogInformation("CustomerId: {CustomerId}", orderMessage.CustomerId);
                _logger.LogInformation("ProductId: {ProductId}", orderMessage.ProductId);
                _logger.LogInformation("Quantity: {Quantity}", orderMessage.Quantity);
                _logger.LogInformation("TotalPrice: {TotalPrice:C}", orderMessage.TotalPrice);
                _logger.LogInformation("OrderDate: {OrderDate}", orderMessage.OrderDate);

                // Verify order exists in table (with retry)
                Order order = null;
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    try
                    {
                        order = await _tableService.GetOrderByIdAsync("Order", orderMessage.OrderId);
                        if (order != null)
                        {
                            _logger.LogInformation("✓ Order verification successful on attempt {Attempt}", attempt);
                            _logger.LogInformation("✓ Order Status: {Status}", order.Status);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Attempt {Attempt} failed to retrieve order", attempt);
                    }

                    if (attempt < 2)
                    {
                        await Task.Delay(2000);
                    }
                }

                if (order == null)
                {
                    _logger.LogWarning("⚠ Order {OrderId} not found after retries - may be processed by another instance",
                        orderMessage.OrderId);
                    return;
                }

                // Check product stock levels for alerts
                try
                {
                    var product = await _tableService.GetProductByIdAsync("Product", orderMessage.ProductId);
                    if (product != null)
                    {
                        _logger.LogInformation("Product: {ProductName}, Current Stock: {Stock}",
                            product.Name, product.StockQuantity);

                        // Low stock alert
                        if (product.StockQuantity < 10)
                        {
                            _logger.LogWarning("⚠ LOW STOCK ALERT: Product {ProductId} ({ProductName}) has only {Stock} items remaining",
                                product.RowKey, product.Name, product.StockQuantity);
                        }

                        // Out of stock alert
                        if (product.StockQuantity == 0)
                        {
                            _logger.LogError("❌ OUT OF STOCK: Product {ProductId} ({ProductName}) is now out of stock",
                                product.RowKey, product.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not check product stock for alerts");
                }

                _logger.LogInformation("=== END ORDER MONITORING ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessOrderQueue. Message: {Message}", queueMessage);
                // Don't throw - just log and return to prevent poison queue
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Queue Trigger Function 2: Process Inventory Messages (for logging/monitoring only)
        [Function("ProcessInventoryQueue")]
        public async Task ProcessInventoryQueue(
            [QueueTrigger("inventory-msg", Connection = "AzureWebJobsStorage")] string inventoryMessage)
        {
            try
            {
                _logger.LogInformation("=== INVENTORY MONITORING ===");
                _logger.LogInformation("Inventory log: {Message}", inventoryMessage);

                if (string.IsNullOrWhiteSpace(inventoryMessage))
                {
                    _logger.LogInformation("Empty inventory message received, exiting gracefully");
                    return;
                }

                // Parse the message to extract product information for monitoring
                string productId = null;
                int quantity = 0;
                string action = "Processing";

                // Extract Product ID
                var productMatch = System.Text.RegularExpressions.Regex.Match(inventoryMessage, @"Product:\s*([a-fA-F0-9\-]+)");
                if (productMatch.Success)
                {
                    productId = productMatch.Groups[1].Value.Trim();
                }

                // Extract Quantity
                var quantityMatch = System.Text.RegularExpressions.Regex.Match(inventoryMessage, @"Quantity:\s*(\d+)");
                if (quantityMatch.Success)
                {
                    int.TryParse(quantityMatch.Groups[1].Value, out quantity);
                }

                // Determine action type
                if (inventoryMessage.Contains("cancelled", StringComparison.OrdinalIgnoreCase) ||
                    inventoryMessage.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
                    inventoryMessage.Contains("restored", StringComparison.OrdinalIgnoreCase))
                {
                    action = "Cancelled/Restored";
                }
                else if (inventoryMessage.Contains("updated", StringComparison.OrdinalIgnoreCase))
                {
                    action = "Updated";
                }
                else if (inventoryMessage.Contains("reduced", StringComparison.OrdinalIgnoreCase))
                {
                    action = "Reduced";
                }

                _logger.LogInformation("Parsed - ProductId: {ProductId}, Quantity: {Quantity}, Action: {Action}",
                    productId ?? "N/A", quantity, action);

                // Generate stock report for monitoring
                if (!string.IsNullOrEmpty(productId))
                {
                    try
                    {
                        var product = await _tableService.GetProductByIdAsync("Product", productId);

                        if (product != null)
                        {
                            _logger.LogInformation("📊 STOCK REPORT:");
                            _logger.LogInformation("  Product: {ProductName} (ID: {ProductId})", product.Name, productId);
                            _logger.LogInformation("  Current Stock: {Stock}", product.StockQuantity);
                            _logger.LogInformation("  Price: {Price:C}", product.Price);
                            _logger.LogInformation("  Action: {Action} ({Quantity} units)", action, quantity);

                            // Generate alerts based on stock levels
                            if (product.StockQuantity == 0)
                            {
                                _logger.LogError("❌ CRITICAL: Product {ProductName} is OUT OF STOCK", product.Name);
                            }
                            else if (product.StockQuantity < 5)
                            {
                                _logger.LogWarning("⚠ WARNING: Product {ProductName} is CRITICALLY LOW ({Stock} remaining)",
                                    product.Name, product.StockQuantity);
                            }
                            else if (product.StockQuantity < 20)
                            {
                                _logger.LogWarning("⚠ NOTICE: Product {ProductName} stock is getting low ({Stock} remaining)",
                                    product.Name, product.StockQuantity);
                            }
                            else
                            {
                                _logger.LogInformation("✓ Product {ProductName} stock is adequate ({Stock} remaining)",
                                    product.Name, product.StockQuantity);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("⚠ Product {ProductId} not found for monitoring", productId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error retrieving product for monitoring: {ProductId}", productId);
                    }
                }

                _logger.LogInformation("=== END INVENTORY MONITORING ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing inventory queue message: {Message}", inventoryMessage);
                // Don't throw - just log and return to avoid poison queue
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//