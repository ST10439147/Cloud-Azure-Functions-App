// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - Admin Controller

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;

namespace ST10439147_CLDV6212_POE.Controllers
{
    /// <summary>
    /// Admin-only controller for managing the system
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly AuthenticationService _authService;
        private readonly TableService _tableService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            AuthenticationService authService,
            TableService tableService,
            ILogger<AdminController> logger)
        {
            _authService = authService;
            _tableService = tableService;
            _logger = logger;
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Admin/Index - Dashboard
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                // Get statistics
                var users = await _authService.GetAllUsersAsync();
                var orders = await _tableService.GetAllOrdersAsync();
                var products = await _tableService.GetAllProductsAsync();
                var customers = await _tableService.GetAllCustomersAsync();

                ViewBag.TotalUsers = users.Count;
                ViewBag.TotalCustomers = users.Count(u => u.Role == "Customer");
                ViewBag.TotalOrders = orders.Count;
                ViewBag.PendingOrders = orders.Count(o => o.Status == "Pending");
                ViewBag.TotalProducts = products.Count;
                ViewBag.LowStockProducts = products.Count(p => p.StockQuantity < 10);

                // Recent orders
                ViewBag.RecentOrders = orders.OrderByDescending(o => o.OrderDate).Take(10).ToList();

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading admin dashboard");
                ViewBag.Error = "Unable to load dashboard data.";
                return View();
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Admin/Users - Manage users
        [HttpGet]
        public async Task<IActionResult> Users()
        {
            try
            {
                var users = await _authService.GetAllUsersAsync();

                // Load customer details for each user
                foreach (var user in users.Where(u => !string.IsNullOrEmpty(u.CustomerId)))
                {
                    try
                    {
                        var customer = await _tableService.GetCustomerByIdAsync("Customer", user.CustomerId!);
                        ViewBag.Customers = ViewBag.Customers ?? new Dictionary<string, Customer>();
                        ((Dictionary<string, Customer>)ViewBag.Customers)[user.CustomerId!] = customer;
                    }
                    catch { }
                }

                return View(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading users");
                ViewBag.Error = "Unable to load users.";
                return View(new List<User>());
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Admin/DeactivateUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateUser(int userId)
        {
            try
            {
                var success = await _authService.DeactivateUserAsync(userId);

                if (success)
                {
                    TempData["Success"] = "User deactivated successfully.";
                }
                else
                {
                    TempData["Error"] = "User not found.";
                }

                return RedirectToAction(nameof(Users));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating user: {UserId}", userId);
                TempData["Error"] = "Unable to deactivate user.";
                return RedirectToAction(nameof(Users));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Admin/ActivateUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActivateUser(int userId)
        {
            try
            {
                var success = await _authService.ActivateUserAsync(userId);

                if (success)
                {
                    TempData["Success"] = "User activated successfully.";
                }
                else
                {
                    TempData["Error"] = "User not found.";
                }

                return RedirectToAction(nameof(Users));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error activating user: {UserId}", userId);
                TempData["Error"] = "Unable to activate user.";
                return RedirectToAction(nameof(Users));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Admin/Orders - View all orders
        [HttpGet]
        public async Task<IActionResult> Orders(string? status = null)
        {
            try
            {
                var orders = await _tableService.GetAllOrdersAsync();

                // Filter by status if provided
                if (!string.IsNullOrEmpty(status))
                {
                    orders = orders.Where(o => o.Status == status).ToList();
                }

                // Load related data
                foreach (var order in orders)
                {
                    try
                    {
                        var customer = await _tableService.GetCustomerByIdAsync("Customer", order.CustomerId);
                        ViewBag.Customers = ViewBag.Customers ?? new Dictionary<string, Customer>();
                        ((Dictionary<string, Customer>)ViewBag.Customers)[order.CustomerId] = customer;

                        var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);
                        ViewBag.Products = ViewBag.Products ?? new Dictionary<string, Product>();
                        ((Dictionary<string, Product>)ViewBag.Products)[order.ProductId] = product;
                    }
                    catch { }
                }

                ViewBag.CurrentFilter = status;
                return View(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading orders");
                ViewBag.Error = "Unable to load orders.";
                return View(new List<Order>());
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Admin/UpdateOrderStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrderStatus(string partitionKey, string rowKey, string status)
        {
            try
            {
                var order = await _tableService.GetOrderByIdAsync(partitionKey, rowKey);

                if (order == null)
                {
                    TempData["Error"] = "Order not found.";
                    return RedirectToAction(nameof(Orders));
                }

                order.Status = status;
                await _tableService.UpdateOrderAsync(order);

                TempData["Success"] = $"Order status updated to '{status}'.";
                return RedirectToAction(nameof(Orders));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order status");
                TempData["Error"] = "Unable to update order status.";
                return RedirectToAction(nameof(Orders));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Admin/OrderDetails
        [HttpGet]
        public async Task<IActionResult> OrderDetails(string partitionKey, string rowKey)
        {
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

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Admin/Products - Product management
        [HttpGet]
        public IActionResult Products()
        {
            // Redirect to Product controller (admin can manage products there)
            return RedirectToAction("Index", "Product");
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Admin/Customers - Customer management
        [HttpGet]
        public IActionResult Customers()
        {
            // Redirect to Customer controller (admin can manage customers there)
            return RedirectToAction("Index", "Customer");
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Admin/Files - File management
        [HttpGet]
        public IActionResult Files()
        {
            // Redirect to FileUpload controller (admin can manage files there)
            return RedirectToAction("Index", "FileUpload");
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//