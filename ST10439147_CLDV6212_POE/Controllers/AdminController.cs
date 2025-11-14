// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - Admin Order Management

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;

namespace ST10439147_CLDV6212_POE.Controllers
{
    /// <summary>
    /// Controller responsible for administrative operations and order management.
    /// Provides administrators with dashboard access, order oversight, and status management capabilities.
    /// All actions in this controller require Admin role authentication.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        // Private fields for dependency injection
        private readonly TableService _tableService;
        private readonly ILogger<AdminController> _logger;

        /// <summary>
        /// Initializes a new instance of the AdminController with required services.
        /// </summary>
        /// <param name="tableService">Service for accessing Azure Table Storage data</param>
        /// <param name="logger">Logger for recording application events and errors</param>
        /// <exception cref="ArgumentNullException">Thrown when any required service is null</exception>
        public AdminController(TableService tableService, ILogger<AdminController> logger)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays the administrative dashboard with key system statistics.
        /// Provides an overview of orders, products, and customers for quick monitoring.
        /// Shows total counts and order status breakdowns (Pending, Processed).
        /// </summary>
        /// <returns>Dashboard view with aggregated statistics in ViewBag</returns>
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                // Retrieve all data for statistical analysis
                var orders = await _tableService.GetAllOrdersAsync();
                var products = await _tableService.GetAllProductsAsync();
                var customers = await _tableService.GetAllCustomersAsync();

                // Calculate and pass statistics to view
                ViewBag.TotalOrders = orders.Count;
                ViewBag.PendingOrders = orders.Count(o => o.Status == "Pending");       // Orders awaiting processing
                ViewBag.ProcessedOrders = orders.Count(o => o.Status == "PROCESSED");   // Completed orders
                ViewBag.TotalProducts = products.Count;
                ViewBag.TotalCustomers = customers.Count;

                return View();
            }
            catch (Exception ex)
            {
                // Handle errors gracefully - show empty dashboard with error message
                _logger.LogError(ex, "Error loading admin dashboard");
                TempData["Error"] = "Unable to load dashboard.";
                return View();
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays a comprehensive list of all orders in the system with optional status filtering.
        /// Enriches order data with related customer and product information for complete context.
        /// Administrators can filter by order status to focus on specific workflow stages.
        /// </summary>
        /// <param name="status">Optional status filter (e.g., "Pending", "PROCESSED", "Cancelled")</param>
        /// <returns>Orders view with list of orders and related entity dictionaries in ViewBag</returns>
        [HttpGet]
        public async Task<IActionResult> Orders(string status = null)
        {
            try
            {
                // Retrieve all orders from Azure Table Storage
                var orders = await _tableService.GetAllOrdersAsync();

                // Apply status filter if provided
                if (!string.IsNullOrEmpty(status))
                {
                    orders = orders.Where(o => o.Status == status).ToList();
                }

                // Load customer and product details for each order to enrich display
                foreach (var order in orders)
                {
                    try
                    {
                        // Retrieve related customer and product entities
                        var customer = await _tableService.GetCustomerByIdAsync("Customer", order.CustomerId);
                        var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

                        // Initialize ViewBag dictionaries if not already created
                        ViewBag.Customers = ViewBag.Customers ?? new Dictionary<string, Customer>();
                        ViewBag.Products = ViewBag.Products ?? new Dictionary<string, Product>();

                        // Store in dictionaries for efficient lookup in view
                        ((Dictionary<string, Customer>)ViewBag.Customers)[order.CustomerId] = customer;
                        ((Dictionary<string, Product>)ViewBag.Products)[order.ProductId] = product;
                    }
                    catch (Exception ex)
                    {
                        // Log warning but continue - some related data may be missing
                        _logger.LogWarning(ex, "Could not load related data for order {OrderId}", order.RowKey);
                    }
                }

                // Pass filter context to view for display
                ViewBag.StatusFilter = status;
                return View(orders);
            }
            catch (Exception ex)
            {
                // Handle errors gracefully - show empty list with error message
                _logger.LogError(ex, "Error loading orders");
                TempData["Error"] = "Unable to load orders.";
                return View(new List<Order>());
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Updates the status of a specific order to a new value.
        /// Used by administrators to manage order workflow progression (e.g., Pending → PROCESSED).
        /// Logs status changes for audit trail purposes.
        /// </summary>
        /// <param name="partitionKey">Partition key of the order</param>
        /// <param name="rowKey">Row key (unique identifier) of the order</param>
        /// <param name="status">New status value to apply</param>
        /// <returns>Redirect to Orders list with success or error message</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrderStatus(string partitionKey, string rowKey, string status)
        {
            // Validate all required parameters are provided
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey) || string.IsNullOrEmpty(status))
            {
                TempData["Error"] = "Invalid order information.";
                return RedirectToAction(nameof(Orders));
            }

            try
            {
                // Retrieve the order to update
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order == null)
                {
                    TempData["Error"] = "Order not found.";
                    return RedirectToAction(nameof(Orders));
                }

                // Capture old status for audit logging
                var oldStatus = order.Status;

                // Update to new status
                order.Status = status;

                // Persist changes to Azure Table Storage
                await _tableService.UpdateOrderAsync(order);

                // Log status change for audit trail
                _logger.LogInformation("Order {OrderId} status updated from {OldStatus} to {NewStatus} by admin",
                    order.RowKey, oldStatus, status);

                TempData["Success"] = $"Order status updated to '{status}' successfully.";
                return RedirectToAction(nameof(Orders));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order status for {PartitionKey}/{RowKey}",
                    partitionKey, rowKey);
                TempData["Error"] = "Unable to update order status.";
                return RedirectToAction(nameof(Orders));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Quick action method to mark an order as PROCESSED.
        /// Convenience method that wraps UpdateOrderStatus with a predefined status value.
        /// Used for one-click order processing from the orders list.
        /// </summary>
        /// <param name="partitionKey">Partition key of the order</param>
        /// <param name="rowKey">Row key (unique identifier) of the order</param>
        /// <returns>Result of UpdateOrderStatus with "PROCESSED" status</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessOrder(string partitionKey, string rowKey)
        {
            // Delegate to UpdateOrderStatus with hardcoded "PROCESSED" status
            return await UpdateOrderStatus(partitionKey, rowKey, "PROCESSED");
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays detailed information for a specific order.
        /// Shows complete order data along with associated customer and product information.
        /// Provides administrators with full context for order review and decision-making.
        /// </summary>
        /// <param name="partitionKey">Partition key of the order</param>
        /// <param name="rowKey">Row key (unique identifier) of the order</param>
        /// <returns>OrderDetails view with order data and related entities in ViewBag</returns>
        [HttpGet]
        public async Task<IActionResult> OrderDetails(string partitionKey, string rowKey)
        {
            // Validate parameters
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                return NotFound();
            }

            try
            {
                // Retrieve the order
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order == null)
                {
                    return NotFound();
                }

                // Load related customer and product data for complete context
                var customer = await _tableService.GetCustomerByIdAsync("Customer", order.CustomerId);
                var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

                // Pass related entities to view via ViewBag
                ViewBag.Customer = customer;
                ViewBag.Product = product;

                return View(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading order details");
                TempData["Error"] = "Unable to load order details.";
                return RedirectToAction(nameof(Orders));
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//