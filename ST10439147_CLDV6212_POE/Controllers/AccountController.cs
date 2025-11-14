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
    /// Controller responsible for managing user authentication, registration, and account-related operations.
    /// Handles login, logout, registration, and profile management for both customers and administrators.
    /// </summary>
    public class AccountController : Controller
    {
        // Private fields for dependency injection
        private readonly ST10439147_CLDV6212_POE.Services.AuthenticationService _authService;
        private readonly TableService _tableService;
        private readonly ILogger<AccountController> _logger;

        /// <summary>
        /// Initializes a new instance of the AccountController with required services.
        /// </summary>
        /// <param name="authService">Service for handling authentication operations</param>
        /// <param name="tableService">Service for accessing Azure Table Storage data</param>
        /// <param name="logger">Logger for recording application events and errors</param>
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
        /// <summary>
        /// Displays the login page to unauthenticated users.
        /// Redirects authenticated users to their appropriate home page based on role.
        /// </summary>
        /// <param name="returnUrl">Optional URL to redirect to after successful login</param>
        /// <returns>Login view for unauthenticated users, or redirect for authenticated users</returns>
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            // Check if user is already authenticated
            if (User.Identity?.IsAuthenticated == true)
            {
                // Redirect admins to admin dashboard
                if (User.IsInRole("Admin"))
                {
                    return RedirectToAction("Index", "Admin");
                }
                // Redirect regular users to home page
                return RedirectToAction("Index", "Home");
            }

            // Store return URL for post-login redirect
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Processes user login attempts with credentials validation.
        /// Creates authentication cookie with user claims upon successful authentication.
        /// </summary>
        /// <param name="model">Login view model containing email and password</param>
        /// <param name="returnUrl">Optional URL to redirect to after successful login</param>
        /// <returns>Redirect to appropriate page on success, or login view with errors on failure</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;

            // Validate model state
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Attempt to authenticate user
            var (success, message, user) = await _authService.LoginAsync(model);

            // Handle authentication failure
            if (!success || user == null)
            {
                ModelState.AddModelError(string.Empty, message);
                return View(model);
            }

            // Create claims for the authenticated user to store in the authentication cookie
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()), // Unique user identifier
                new Claim(ClaimTypes.Email, user.Email),                        // User's email address
                new Claim(ClaimTypes.Role, user.Role),                          // User's role (Admin/Customer)
                new Claim("FullName", user.Email)                               // Display name (using email as placeholder)
            };

            // Add customer ID claim if user is associated with a customer record
            if (!string.IsNullOrEmpty(user.CustomerId))
            {
                claims.Add(new Claim("CustomerId", user.CustomerId));
            }

            // Create claims identity using cookie authentication scheme
            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            // Configure authentication properties based on "Remember Me" option
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,                                  // Persist cookie across browser sessions
                ExpiresUtc = model.RememberMe
                    ? DateTimeOffset.UtcNow.AddDays(30)                          // 30-day expiration if remembered
                    : DateTimeOffset.UtcNow.AddHours(2),                         // 2-hour expiration for regular login
                AllowRefresh = true                                               // Allow cookie refresh
            };

            // Sign in the user by creating the authentication cookie
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                claimsPrincipal,
                authProperties);

            // Log successful login event
            _logger.LogInformation("User {Email} logged in successfully", user.Email);

            // Set success message for display
            TempData["Success"] = "Login successful!";

            // Redirect admin users to admin dashboard
            if (user.Role == "Admin")
            {
                return RedirectToAction("Index", "Admin");
            }

            // Redirect to return URL if valid and local (security check)
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            // Default redirect to home page
            return RedirectToAction("Index", "Home");
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays the registration page for new users.
        /// Redirects authenticated users to home page.
        /// </summary>
        /// <returns>Registration view for unauthenticated users, or redirect for authenticated users</returns>
        [HttpGet]
        public IActionResult Register()
        {
            // Prevent already logged-in users from accessing registration
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Processes new user registration requests.
        /// Creates user account and associated customer record in Azure Table Storage.
        /// </summary>
        /// <param name="model">Registration view model containing user details</param>
        /// <returns>Redirect to login page on success, or registration view with errors on failure</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            // Validate model state
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Attempt to register new customer
            var (success, message, user) = await _authService.RegisterCustomerAsync(model);

            // Handle registration failure
            if (!success)
            {
                ModelState.AddModelError(string.Empty, message);
                return View(model);
            }

            // Display success message and redirect to login
            TempData["Success"] = "Registration successful! Please log in with your credentials.";
            return RedirectToAction(nameof(Login));
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Logs out the current user by removing their authentication cookie.
        /// </summary>
        /// <returns>Redirect to home page after logout</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            // Remove authentication cookie
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Log logout event
            _logger.LogInformation("User logged out");

            // Display success message
            TempData["Success"] = "You have been logged out successfully.";

            return RedirectToAction("Index", "Home");
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays the access denied page when users attempt to access unauthorized resources.
        /// </summary>
        /// <returns>Access denied view</returns>
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays the user's profile page with account and customer information.
        /// Requires user to be authenticated.
        /// </summary>
        /// <returns>Profile view with user and customer data, or redirect to login if unauthenticated</returns>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            // Extract user ID from authentication claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // Validate user ID exists and can be parsed
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return RedirectToAction(nameof(Login));
            }

            // Retrieve user details from authentication service
            var user = await _authService.GetUserByIdAsync(userId);

            // Handle case where user record cannot be found
            if (user == null)
            {
                TempData["Error"] = "Unable to load profile.";
                return RedirectToAction("Index", "Home");
            }

            // Attempt to retrieve associated customer details if available
            Customer? customer = null;
            if (!string.IsNullOrEmpty(user.CustomerId))
            {
                try
                {
                    // Fetch customer record from Azure Table Storage
                    customer = await _tableService.GetCustomerByIdAsync("Customer", user.CustomerId);
                }
                catch (Exception ex)
                {
                    // Log warning but continue - customer details are optional
                    _logger.LogWarning(ex, "Unable to load customer details for user {UserId}", userId);
                }
            }

            // Pass customer data to view via ViewBag
            ViewBag.Customer = customer;
            return View(user);
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//