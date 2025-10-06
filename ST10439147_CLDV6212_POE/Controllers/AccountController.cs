// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - Account Controller

using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ST10439147_CLDV6212_POE.Controllers
{
    /// <summary>
    /// Controller for handling user authentication and account management
    /// </summary>
    public class AccountController : Controller
    {
        private readonly ST10439147_CLDV6212_POE.Services.AuthenticationService _authService;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            ST10439147_CLDV6212_POE.Services.AuthenticationService authService,
            ILogger<AccountController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Account/Login
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
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
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role)
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
                    : DateTimeOffset.UtcNow.AddHours(2)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                claimsPrincipal,
                authProperties);

            _logger.LogInformation("User {Username} logged in successfully", user.Username);

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

            TempData["Success"] = "Registration successful! Please log in.";
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

            return View(user);
        }
    }
}