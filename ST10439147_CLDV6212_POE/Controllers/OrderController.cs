// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 1

//References:
// ClaudAI - https://claude.ai/
// ChatGPT - https://chat.openai.com/
// W3schools - https://www.w3schools.com/
// IIEVC School of Computer Science Youtube channel for Azure services setup and use https://www.youtube.com/@VCSOCS
// AzureApp project done in class with lecturer

using Humanizer;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.Blazor;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;
using System;
using System.Collections.Concurrent;
using System.Reflection.Metadata;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class OrderController : Controller
    {
        // The services are injected via dependency injection
        private readonly TableService _tableService;
        private readonly QueueService _queueService;
        private readonly ILogger<OrderController> _logger;
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Constructor to initialize services
        // Throws ArgumentNullException if any service is null
        public OrderController(TableService tableService, QueueService queueService, ILogger<OrderController> logger)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        public async Task<IActionResult> Index()// Retrieving and displaying all orders
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
        // GET: Order/Create
        // This action handles the display of the create order form.
        // The method loads necessary data for dropdowns, initializes default values, and displays the form.
        // It handles errors gracefully with logging and user feedback.
        // It ensures that the user can create orders while validating stock and handling messaging reliably.
        // The method also logs key actions and errors for monitoring and debugging purposes.
        // It uses TempData to provide feedback to the user in case of errors.
        // Redirects to the Index action if required data cannot be loaded.
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            try
            {
                _logger.LogInformation("Loading data for order creation");

                var customers = await _tableService.GetAllCustomersAsync();// Load customers for dropdown
                var products = await _tableService.GetAllProductsAsync();// Load products for dropdown

                if (!customers.Any())// Check if customers exist
                {
                    TempData["Error"] = "No customers available. Please add customers first.";
                    return RedirectToAction("Index", "Customer");
                }

                if (!products.Any())// Check if products exist
                {
                    TempData["Error"] = "No products available. Please add products first.";
                    return RedirectToAction("Index", "Product");
                }

                ViewBag.Customers = customers;// Pass customers to view
                ViewBag.Products = products;// Pass products to view

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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Order/Create
        // This action handles the creation of a new order.
        // It validates the input, checks stock availability, and inserts the order into Azure Table Storage.
        // Upon successful creation, it sends messages to the order and inventory queues with retry logic.
        // The method also logs key actions and errors for monitoring and debugging purposes.
        // It uses TempData to provide feedback to the user and reloads necessary data for the form in case of errors.
        // Redirects to the Index action upon successful creation.
        // It ensures that the user can create orders while validating stock and handling messaging reliably.
        // The method also logs key actions and errors for monitoring and debugging purposes.
        // It uses TempData to provide feedback to the user in case of errors.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Order order)
        {
            // Remove fields that are not user-editable from ModelState
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");
            ModelState.Remove("OrderDate");
            ModelState.Remove("Status");
            ModelState.Remove("Timestamp");
            ModelState.Remove("ETag");

            _logger.LogInformation("Received order - TotalPrice: {TotalPrice}, Type: {Type}",
                order.TotalPrice, order.TotalPrice.GetType().Name);

            if (string.IsNullOrEmpty(order.CustomerId))// Validate customer selection
            {
                ModelState.AddModelError("CustomerId", "Please select a customer");
            }

            if (string.IsNullOrEmpty(order.ProductId))// Validate product selection
            {
                ModelState.AddModelError("ProductId", "Please select a product");
            }

            if (order.Quantity <= 0)// Validate quantity
            {
                ModelState.AddModelError("Quantity", "Quantity must be greater than 0");
            }

            if (order.TotalPrice <= 0)// Validate total price
            {
                ModelState.AddModelError("TotalPrice", "Total price must be greater than 0");
            }

            // Validate stock availability
            if (!string.IsNullOrEmpty(order.ProductId))
            {
                try
                {
                    var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);// Fetch product details
                    if (product != null && order.Quantity > product.StockQuantity)// Check stock
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // This method sends an order message to the queue with retry logic.
        // It attempts to send the message up to maxRetries times, with exponential backoff between attempts.
        // Logs success, warnings, and errors appropriately.
        // If all attempts fail, it re-throws the exception.
        // This ensures that transient issues with the queue service do not prevent order processing.
        // The method constructs an OrderMessage object from the provided Order entity and sends it to the queue.
        // It logs each attempt and any failures, providing detailed information for monitoring and debugging.
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Retry logic for sending inventory message
        // This method implements retry logic with exponential backoff for sending inventory messages to the queue.
        // It attempts to send the message up to maxRetries times, logging each attempt and any failures.
        // If all attempts fail, it logs an error and re-throws the exception.
        // This ensures that transient issues with the queue service do not prevent order processing.
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Additional action to view queue status (useful for monitoring)
        // Calls _queueService.GetQueueLengthAsync(queueName) for both "ordermsg" and "inventory-msg" queues.
        // Stores the lengths in ViewBag for display in the view.
        // Logs the queue lengths or any errors encountered during the process.
        [HttpGet]
        public async Task<IActionResult> QueueStatus()
        {
            try
            {
                // Get lengths of both queues
                var orderQueueLength = await _queueService.GetQueueLengthAsync("ordermsg");
                var inventoryQueueLength = await _queueService.GetQueueLengthAsync("inventory-msg");

                ViewBag.OrderQueueLength = orderQueueLength;// Length of order message queue
                ViewBag.InventoryQueueLength = inventoryQueueLength;// Length of inventory message queue

                _logger.LogInformation("Queue status - Orders: {OrderCount}, Inventory: {InventoryCount}",// Log the lengths
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Action to view messages in the order queue (for debugging/monitoring)
        // Calls _queueService.PeekQueueMessagesAsync(queueName, 32) to fetch up to 32 messages (Azure’s maximum limit for a single peek request).
        // Defaults to the "ordermsg" queue if no queue name is provided.
        // Stores the queue name and retrieved messages in ViewBag for display in the view.
        // Logs the number of messages retrieved or any errors encountered during the process.
        [HttpGet]
        public async Task<IActionResult> ViewQueueMessages(string queueName = "ordermsg")
        {
            try
            {
                // Use Azure's maximum limit of 32 messages instead of 50
                var messages = await _queueService.PeekQueueMessagesAsync(queueName, 32);
                ViewBag.QueueName = queueName;
                ViewBag.Messages = messages;

                _logger.LogInformation("Retrieved {Count} messages from queue: {QueueName}", messages.Count, queueName);
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving messages from queue: {QueueName}", queueName);
                ViewBag.Error = $"Unable to retrieve messages from queue '{queueName}'. {ex.Message}";
                ViewBag.QueueName = queueName;
                ViewBag.Messages = new List<string>();
                return View();
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Ensures both messageId and popReceipt are provided.
        // If missing, sets a TempData error message("Invalid message ID or pop receipt.").
        // Attempts to delete the specified message from the given queue using _queueService.DeleteMessageAsync.

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
        // GET: Order/Edit/5
        // This action handles the display of the edit form for an order stored in Azure Table Storage.
        // The method validates the provided keys, retrieves the order, loads related customer and product data for dropdowns,
        // and displays the edit view, while handling errors gracefully with logging and user feedback.
        // It ensures that the user can modify order details while preserving system-managed fields.
        // The method also logs key actions and errors for monitoring and debugging purposes.
        // It uses TempData to provide feedback to the user in case of errors.    
        // Redirects to the Index action if the order cannot be loaded.
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Order/Edit/5
        // This action processes the submission of the edit form for an order stored in Azure Table Storage.
        // It validates the input, ensures route parameters match the model, updates the order while preserving system-managed fields,
        // and handles errors nicely with logging and user feedback.
        // The method also sends update notifications via messaging services and logs key actions for monitoring and debugging.
        // It uses TempData to provide feedback to the user and reloads necessary data for the form in case of errors.
        // Redirects to the Index action upon successful update.
        // It ensures that the user can modify order details while preserving system-managed fields.
        // The method also logs key actions and errors for monitoring and debugging purposes.
        // It uses TempData to provide feedback to the user in case of errors.
        // Redirects to the Index action if the order is updated successfully.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey, Order order)
        {
            // Remove fields that are not user-editable from ModelState
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        //This action handles the display of the delete confirmation page for an order stored in Azure Table Storage.
        //The method validates keys, retrieves the order, optionally loads related customer and product info,
        //and displays the delete confirmation view,
        //while handling errors gracefully with logging and user feedback.
        [HttpGet]
        public async Task<IActionResult> Delete(string partitionKey, string rowKey)
        {
            // Validating input parameters, logging warning, returning 404 if invalid
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Order Delete called with null or empty parameters");
                return NotFound();
            }

            try// Main try-catch for loading order
            {
                // Log the attempt to load the order
                _logger.LogInformation("Loading order for delete confirmation: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);// Retrieving the order

                if (order == null)// If order not found, log and return 404
                {
                    _logger.LogWarning("Order not found for delete: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                    return NotFound();
                }

                // Load related data for display
                try
                {
                    var customer = await _tableService.GetCustomerByIdAsync("Customer", order.CustomerId);// Retrieving related customer
                    var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);// Retrieving related product

                    ViewBag.Customer = customer;// Passing customer to view
                    ViewBag.Product = product;// Same thing but for product
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error loading related data for order deletion: {OrderId}", rowKey);
                    // Continue without related data
                }

                return View(order);// Displaying the delete confirmation view
            }
            catch (Exception ex)
            {
                // Log the error and provide user feedback
                _logger.LogError(ex, "Error loading order for delete confirmation: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                TempData["Error"] = "Unable to load order for deletion.";
                return RedirectToAction(nameof(Index));
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Order/Delete/5
        // This action finalizes the deletion of an order after the user confirms the delete request.
        // It ensures the order is removed from table storage and communicates the cancellation through messaging services.
        // This method confirms order deletion by removing the order from table storage, sending cancellation notifications via queues, and providing appropriate logging and user feedback.
        // It handles errors gracefully to ensure a smooth user experience.
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string partitionKey, string rowKey)// Deleting an order
        {
            // Validate input parameters, log warning, return 404 if invalid
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Order DeleteConfirmed called with null or empty parameters");
                return NotFound();
            }

            try// Main try-catch for deletion process
            {
                // Log the deletion attempt
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

                        // Send order cancellation message
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
                    // Provide user feedback
                    TempData["Success"] = "Order deleted successfully!";
                }
                else
                {
                    // Log if the order was not found
                    _logger.LogWarning("Order not found for deletion: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                    TempData["Error"] = "Order not found.";
                }

                return RedirectToAction(nameof(Index));// Redirect to order list after deletion
            }
            catch (Exception ex)// Catch any errors during deletion process
            {
                // Log the error and provide user feedback
                _logger.LogError(ex, "Error deleting order: PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);
                TempData["Error"] = "Unable to delete order. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//