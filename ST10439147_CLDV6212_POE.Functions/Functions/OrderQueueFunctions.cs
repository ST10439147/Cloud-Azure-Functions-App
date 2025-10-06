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
        // PURPOSE: Log order details, verify order exists, check stock levels for alerts
        // CRITICAL: This function DOES NOT update stock - stock is updated in ProcessCompleteOrder
        //  Uses Newtonsoft.Json for deserialization to handle case-insensitive property names
        //  Retries order verification up to 3 times with delays to handle eventual consistency
        //  Logs detailed information for monitoring and alerting purposes
        //  Acknowledges all messages to prevent poison queue buildup
        //  Designed for robustness and resilience in production environments
        //  Ensure the queue messages conform to the expected JSON structure
        //  Adjust logging levels as needed for production vs. development
        //  This function is part of the isolated worker model for Azure Functions
        //  Ensure the Function App has appropriate permissions to access Azure Table Storage
        //  Monitor logs regularly to catch and address any issues promptly
        [Function("ProcessOrderQueue")]
        public async Task ProcessOrderQueue(
            [QueueTrigger("ordermsg", Connection = "AzureWebJobsStorage")] string queueMessage)// Message from "ordermsg" queue
        {
            _logger.LogInformation("=== ProcessOrderQueue TRIGGERED ===");// Log function trigger
            _logger.LogInformation("Raw message: {Message}", queueMessage);// Log raw message

            try
            {
                // Validate message
                if (string.IsNullOrWhiteSpace(queueMessage))
                {
                    _logger.LogInformation("Empty message received - acknowledging and discarding");
                    return;
                }

                // Check if it looks like JSON
                var trimmedMessage = queueMessage.TrimStart();
                if (!trimmedMessage.StartsWith("{"))
                {
                    _logger.LogInformation("Non-JSON message received: {Message} - acknowledging and discarding", queueMessage);
                    return;
                }

                OrderMessage orderMessage = null;
                try
                {
                    // Use Newtonsoft.Json with settings that match the sender
                    var settings = new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        MissingMemberHandling = MissingMemberHandling.Ignore,
                        // This is crucial - it makes property matching case-insensitive
                        ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
                        {
                            NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()
                        }
                    };
                    // Deserialize the message, allowing for case-insensitive property names
                    orderMessage = JsonConvert.DeserializeObject<OrderMessage>(queueMessage, settings);

                    if (orderMessage != null)
                    {
                        _logger.LogInformation("✓ Deserialization successful - OrderId: {OrderId}", orderMessage.OrderId);
                    }
                    else
                    {
                        _logger.LogWarning("Deserialization returned null");
                        return;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "JSON deserialization failed. Message: {Message}", queueMessage);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error during deserialization. Message: {Message}", queueMessage);
                    return;
                }

                if (string.IsNullOrEmpty(orderMessage.OrderId))
                {
                    _logger.LogWarning("OrderMessage deserialized but OrderId is null/empty");
                    return;
                }

                _logger.LogInformation("=== ORDER MONITORING ===");
                _logger.LogInformation("Action: {Action}", orderMessage.Action ?? "UNKNOWN");
                _logger.LogInformation("OrderId: {OrderId}", orderMessage.OrderId);
                _logger.LogInformation("CustomerId: {CustomerId}", orderMessage.CustomerId ?? "NULL");
                _logger.LogInformation("ProductId: {ProductId}", orderMessage.ProductId ?? "NULL");
                _logger.LogInformation("Quantity: {Quantity}", orderMessage.Quantity);
                _logger.LogInformation("TotalPrice: {TotalPrice:C}", orderMessage.TotalPrice);
                _logger.LogInformation("OrderDate: {OrderDate}", orderMessage.OrderDate);

                // Verify order exists in table (with retry and longer delays)
                Order order = null;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        _logger.LogInformation("Attempting to retrieve order (attempt {Attempt}/3)...", attempt);
                        order = await _tableService.GetOrderByIdAsync("Order", orderMessage.OrderId);

                        if (order != null)
                        {
                            _logger.LogInformation("✓ Order verification successful on attempt {Attempt}", attempt);
                            _logger.LogInformation("✓ Order Status: {Status}", order.Status);
                            break;
                        }
                        else
                        {
                            _logger.LogWarning("Order returned null on attempt {Attempt}", attempt);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Attempt {Attempt} failed to retrieve order: {Error}", attempt, ex.Message);
                    }

                    if (attempt < 3)
                    {
                        _logger.LogInformation("Waiting 3 seconds before retry...");
                        await Task.Delay(3000);
                    }
                }

                if (order == null)
                {
                    _logger.LogWarning("⚠ Order {OrderId} not found after 3 retries", orderMessage.OrderId);
                }

                // Check product stock levels for alerts
                if (!string.IsNullOrEmpty(orderMessage.ProductId))
                {
                    try
                    {
                        _logger.LogInformation("Checking product stock for alerts...");
                        var product = await _tableService.GetProductByIdAsync("Product", orderMessage.ProductId);

                        if (product != null)
                        {
                            _logger.LogInformation("Product: {ProductName}, Current Stock: {Stock}",
                                product.Name, product.StockQuantity);

                            if (product.StockQuantity == 0)
                            {
                                _logger.LogError("❌ OUT OF STOCK: Product {ProductId} ({ProductName})",
                                    product.RowKey, product.Name);
                            }
                            else if (product.StockQuantity < 10)
                            {
                                _logger.LogWarning("⚠ LOW STOCK ALERT: Product {ProductId} ({ProductName}) has {Stock} items",
                                    product.RowKey, product.Name, product.StockQuantity);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Product {ProductId} not found", orderMessage.ProductId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not check product stock: {Error}", ex.Message);
                    }
                }

                _logger.LogInformation("=== END ORDER MONITORING ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CRITICAL ERROR in ProcessOrderQueue: {Error}", ex.Message);
                _logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);
                // Don't rethrow - acknowledge message to prevent poison queue
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Queue Trigger Function 2: Process Inventory Messages (for logging/monitoring only)
        // PURPOSE: Log inventory changes, verify product exists, check stock levels for alerts
        // CRITICAL: This function DOES NOT update stock - stock is updated in ProcessCompleteOrder
        //  Parses semi-structured text messages to extract product and quantity
        //  Logs detailed information for monitoring and alerting purposes
        //  Acknowledges all messages to prevent poison queue buildup
        //  Designed for robustness and resilience in production environments
        //  Ensure the queue messages conform to the expected text structure
        //  Adjust logging levels as needed for production vs. development
        //  This function is part of the isolated worker model for Azure Functions
        //  Ensure the Function App has appropriate permissions to access Azure Table Storage
        //  Monitor logs regularly to catch and address any issues promptly
        //  Note: This function expects messages in a specific text format, e.g.:
        //        "Product: {ProductId}, Quantity: {Quantity}, Action: {ActionType}"
        //        Adjust parsing logic as needed based on actual message format
        [Function("ProcessInventoryQueue")]
        public async Task ProcessInventoryQueue(
            [QueueTrigger("inventory-msg", Connection = "AzureWebJobsStorage")] string inventoryMessage)
        {
            _logger.LogInformation("=== ProcessInventoryQueue TRIGGERED ===");
            _logger.LogInformation("Raw message: {Message}", inventoryMessage);

            try
            {
                if (string.IsNullOrWhiteSpace(inventoryMessage))
                {
                    _logger.LogInformation("Empty inventory message - acknowledging and discarding");
                    return;
                }

                _logger.LogInformation("=== INVENTORY MONITORING ===");

                // Parse the message to extract product information
                string productId = null;
                int quantity = 0;
                string action = "Processing";

                var productMatch = System.Text.RegularExpressions.Regex.Match(
                    inventoryMessage, @"Product:\s*([a-fA-F0-9\-]+)");
                if (productMatch.Success)
                {
                    productId = productMatch.Groups[1].Value.Trim();
                }
                // Extract quantity
                var quantityMatch = System.Text.RegularExpressions.Regex.Match(
                    inventoryMessage, @"Quantity:\s*(\d+)");
                if (quantityMatch.Success)
                {
                    int.TryParse(quantityMatch.Groups[1].Value, out quantity);
                }
                // Determine action type from message content
                if (inventoryMessage.Contains("cancelled", StringComparison.OrdinalIgnoreCase) ||
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

                // Generate stock report
                if (!string.IsNullOrEmpty(productId))
                {
                    try
                    {
                        // Retrieve product details
                        var product = await _tableService.GetProductByIdAsync("Product", productId);

                        if (product != null)// Log stock report
                        {
                            _logger.LogInformation("📊 STOCK REPORT:");
                            _logger.LogInformation("  Product: {ProductName} (ID: {ProductId})",
                                product.Name, productId);
                            _logger.LogInformation("  Current Stock: {Stock}", product.StockQuantity);
                            _logger.LogInformation("  Price: {Price:C}", product.Price);
                            _logger.LogInformation("  Action: {Action} ({Quantity} units)", action, quantity);

                            if (product.StockQuantity == 0)
                            {
                                _logger.LogError("❌ CRITICAL: Product {ProductName} is OUT OF STOCK",
                                    product.Name);
                            }
                            else if (product.StockQuantity < 5)
                            {
                                _logger.LogWarning("⚠ WARNING: Product {ProductName} is CRITICALLY LOW ({Stock})",
                                    product.Name, product.StockQuantity);
                            }
                            else if (product.StockQuantity < 20)
                            {
                                _logger.LogWarning("⚠ NOTICE: Product {ProductName} stock is low ({Stock})",
                                    product.Name, product.StockQuantity);
                            }
                            else
                            {
                                _logger.LogInformation("✓ Product {ProductName} stock is adequate ({Stock})",
                                    product.Name, product.StockQuantity);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error retrieving product: {Error}", ex.Message);
                    }
                }

                _logger.LogInformation("=== END INVENTORY MONITORING ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CRITICAL ERROR in ProcessInventoryQueue: {Error}", ex.Message);
                _logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);
                // Don't rethrow - acknowledge message to prevent poison queue
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//