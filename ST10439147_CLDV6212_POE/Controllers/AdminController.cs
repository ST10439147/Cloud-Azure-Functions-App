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
    /// Admin controller for managing orders and updating order status
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly TableService _tableService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(TableService tableService, ILogger<AdminController> logger)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Admin/Index - Admin Dashboard
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                // Get statistics for dashboard
                var orders = await _tableService.GetAllOrdersAsync();
                var products = await _tableService.GetAllProductsAsync();
                var customers = await _tableService.GetAllCustomersAsync();

                ViewBag.TotalOrders = orders.Count;
                ViewBag.PendingOrders = orders.Count(o => o.Status == "Pending");
                ViewBag.ProcessedOrders = orders.Count(o => o.Status == "PROCESSED");
                ViewBag.TotalProducts = products.Count;
                ViewBag.TotalCustomers = customers.Count;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading admin dashboard");
                TempData["Error"] = "Unable to load dashboard.";
                return View();
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Admin/Orders - View all orders
        [HttpGet]
        public async Task<IActionResult> Orders(string status = null)
        {
            try
            {
                var orders = await _tableService.GetAllOrdersAsync();

                // Filter by status if provided
                if (!string.IsNullOrEmpty(status))
                {
                    orders = orders.Where(o => o.Status == status).ToList();
                }

                // Load customer and product details for each order
                foreach (var order in orders)
                {
                    try
                    {
                        var customer = await _tableService.GetCustomerByIdAsync("Customer", order.CustomerId);
                        var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

                        ViewBag.Customers = ViewBag.Customers ?? new Dictionary<string, Customer>();
                        ViewBag.Products = ViewBag.Products ?? new Dictionary<string, Product>();

                        ((Dictionary<string, Customer>)ViewBag.Customers)[order.CustomerId] = customer;
                        ((Dictionary<string, Product>)ViewBag.Products)[order.ProductId] = product;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not load related data for order {OrderId}", order.RowKey);
                    }
                }

                ViewBag.StatusFilter = status;
                return View(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading orders");
                TempData["Error"] = "Unable to load orders.";
                return View(new List<Order>());
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Admin/UpdateOrderStatus - Update order status to PROCESSED
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrderStatus(string partitionKey, string rowKey, string status)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey) || string.IsNullOrEmpty(status))
            {
                TempData["Error"] = "Invalid order information.";
                return RedirectToAction(nameof(Orders));
            }

            try
            {
                // Get the order
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order == null)
                {
                    TempData["Error"] = "Order not found.";
                    return RedirectToAction(nameof(Orders));
                }

                // Update status
                var oldStatus = order.Status;
                order.Status = status;

                await _tableService.UpdateOrderAsync(order);

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
        // POST: Admin/ProcessOrder - Quick action to mark order as PROCESSED
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessOrder(string partitionKey, string rowKey)
        {
            return await UpdateOrderStatus(partitionKey, rowKey, "PROCESSED");
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Admin/OrderDetails - View detailed order information
        [HttpGet]
        public async Task<IActionResult> OrderDetails(string partitionKey, string rowKey)
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

                // Load related data
                var customer = await _tableService.GetCustomerByIdAsync("Customer", order.CustomerId);
                var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

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