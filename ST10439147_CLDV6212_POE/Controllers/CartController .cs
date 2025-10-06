using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Services;
using System.Security.Claims;

namespace ST10439147_CLDV6212_POE.Controllers
{
    [Authorize(Roles = "Customer")]
    public class CartController : Controller
    {
        private readonly CartService _cartService;
        private readonly ILogger<CartController> _logger;

        public CartController(CartService cartService, ILogger<CartController> logger)
        {
            _cartService = cartService;
            _logger = logger;
        }

        // GET: Cart
        public async Task<IActionResult> Index()
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var customerId = User.FindFirst("CustomerId")?.Value ?? "";

                var cart = await _cartService.GetOrCreateCartAsync(userId, customerId);
                cart.Items = await _cartService.GetCartItemsAsync(cart.CartId);

                return View(cart);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading cart");
                TempData["Error"] = "Unable to load cart";
                return RedirectToAction("Index", "Product");
            }
        }

        // POST: Cart/AddItem
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddItem(string productId, int quantity = 1)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var customerId = User.FindFirst("CustomerId")?.Value ?? "";

                var cart = await _cartService.GetOrCreateCartAsync(userId, customerId);
                await _cartService.AddItemToCartAsync(cart.CartId, productId, quantity);

                TempData["Success"] = "Product added to cart!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding item to cart");
                TempData["Error"] = ex.Message;
                return RedirectToAction("Index", "Product");
            }
        }

        // POST: Cart/UpdateQuantity
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateQuantity(int cartItemId, int quantity)
        {
            try
            {
                await _cartService.UpdateCartItemQuantityAsync(cartItemId, quantity);
                TempData["Success"] = "Cart updated";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating cart item");
                TempData["Error"] = "Unable to update cart";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Cart/RemoveItem
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveItem(int cartItemId)
        {
            try
            {
                await _cartService.RemoveCartItemAsync(cartItemId);
                TempData["Success"] = "Item removed from cart";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cart item");
                TempData["Error"] = "Unable to remove item";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Cart/Checkout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout()
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var customerId = User.FindFirst("CustomerId")?.Value ?? "";

                var cart = await _cartService.GetOrCreateCartAsync(userId, customerId);
                await _cartService.ProcessCartToOrderAsync(cart.CartId, customerId);

                TempData["Success"] = "Order placed successfully!";
                return RedirectToAction("Index", "Order");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing checkout");
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }
    }
}