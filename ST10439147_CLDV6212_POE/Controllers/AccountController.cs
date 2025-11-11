// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - Enhanced Account Controller

using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace ST10439147_CLDV6212_POE.Controllers
{
    /// <summary>
    /// Controller for handling user authentication and account management
    /// </summary>
    public class AccountController : Controller
    {
        private readonly ST10439147_CLDV6212_POE.Services.AuthenticationService _authService;
        private readonly TableService _tableService;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            ST10439147_CLDV6212_POE.Services.AuthenticationService authService,
            TableService tableService,
            ILogger<AccountController> logger)
        {
            _authService = authService;
            _tableService = tableService;
            _logger = logger;
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Account/Login
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            // Redirect if already logged in
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin"))
                {
                    return RedirectToAction("Index", "Admin");
                }
                return RedirectToAction("Index", "Home");
            }

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var (success, message, user) = await _authService.LoginAsync(model);

            if (!success || user == null)
            {
                ModelState.AddModelError(string.Empty, message);
                return View(model);
            }

            // Create claims for the authenticated user
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("FullName", user.Email) // Can be enhanced with actual name from customer record
            };

            if (!string.IsNullOrEmpty(user.CustomerId))
            {
                claims.Add(new Claim("CustomerId", user.CustomerId));
            }

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe
                    ? DateTimeOffset.UtcNow.AddDays(30)
                    : DateTimeOffset.UtcNow.AddHours(2),
                AllowRefresh = true
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                claimsPrincipal,
                authProperties);

            _logger.LogInformation("User {Email} logged in successfully", user.Email);

            TempData["Success"] = "Login successful!";

            // Redirect based on role
            if (user.Role == "Admin")
            {
                return RedirectToAction("Index", "Admin");
            }

            // Redirect to return URL or default customer home
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Account/Register
        [HttpGet]
        public IActionResult Register()
        {
            // Redirect if already logged in
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Account/Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var (success, message, user) = await _authService.RegisterCustomerAsync(model);

            if (!success)
            {
                ModelState.AddModelError(string.Empty, message);
                return View(model);
            }

            TempData["Success"] = "Registration successful! Please log in with your credentials.";
            return RedirectToAction(nameof(Login));
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            _logger.LogInformation("User logged out");
            TempData["Success"] = "You have been logged out successfully.";

            return RedirectToAction("Index", "Home");
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Account/AccessDenied
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Account/Profile
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return RedirectToAction(nameof(Login));
            }

            var user = await _authService.GetUserByIdAsync(userId);

            if (user == null)
            {
                TempData["Error"] = "Unable to load profile.";
                return RedirectToAction("Index", "Home");
            }

            // Get customer details if available
            Customer? customer = null;
            if (!string.IsNullOrEmpty(user.CustomerId))
            {
                try
                {
                    customer = await _tableService.GetCustomerByIdAsync("Customer", user.CustomerId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unable to load customer details for user {UserId}", userId);
                }
            }

            ViewBag.Customer = customer;
            return View(user);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Account/MyOrders - View customer's orders
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<IActionResult> MyOrders()
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;

            if (string.IsNullOrEmpty(customerIdClaim))
            {
                TempData["Error"] = "Customer information not found.";
                return RedirectToAction("Index", "Home");
            }

            try
            {
                // Get all orders and filter by customer ID
                var allOrders = await _tableService.GetAllOrdersAsync();
                var customerOrders = allOrders.Where(o => o.CustomerId == customerIdClaim).ToList();

                // Load product and customer details for each order
                foreach (var order in customerOrders)
                {
                    try
                    {
                        var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);
                        ViewBag.Products = ViewBag.Products ?? new Dictionary<string, Product>();
                        ((Dictionary<string, Product>)ViewBag.Products)[order.ProductId] = product;
                    }
                    catch { }
                }

                return View(customerOrders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading orders for customer {CustomerId}", customerIdClaim);
                TempData["Error"] = "Unable to load your orders.";
                return RedirectToAction("Index", "Home");
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//