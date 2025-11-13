// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - Shopping Cart Controller

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Controllers
{
    /// <summary>
    /// Controller for managing shopping cart operations
    /// Customers can add products to cart, update quantities, and checkout
    /// </summary>
    [Authorize(Roles = "Customer")]
    public class CartController : Controller
    {
        private readonly TableService _tableService;
        private readonly ILogger<CartController> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private const string CartSessionKey = "ShoppingCart";

        public CartController(
            TableService tableService,
            ILogger<CartController> logger,
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Helper method to get cart from session
        private Cart GetCart()
        {
            var cartJson = HttpContext.Session.GetString(CartSessionKey);

            if (string.IsNullOrEmpty(cartJson))
            {
                return new Cart();
            }

            try
            {
                return JsonSerializer.Deserialize<Cart>(cartJson) ?? new Cart();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deserializing cart from session");
                return new Cart();
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Helper method to save cart to session
        private void SaveCart(Cart cart)
        {
            try
            {
                var cartJson = JsonSerializer.Serialize(cart);
                HttpContext.Session.SetString(CartSessionKey, cartJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving cart to session");
                throw;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Cart/Index - Display cart contents
        [HttpGet]
        public IActionResult Index()
        {
            try
            {
                var cart = GetCart();
                return View(cart);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading cart");
                TempData["Error"] = "Unable to load cart. Please try again.";
                return RedirectToAction("Index", "Product");
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Cart/AddToCart - Add product to cart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToCart(string productId, int quantity = 1)
        {
            if (string.IsNullOrEmpty(productId))
            {
                TempData["Error"] = "Invalid product.";
                return RedirectToAction("Index", "Product");
            }

            if (quantity <= 0)
            {
                TempData["Error"] = "Quantity must be at least 1.";
                return RedirectToAction("Index", "Product");
            }

            try
            {
                // Get product details
                var product = await _tableService.GetProductByIdAsync("Product", productId);

                if (product == null)
                {
                    TempData["Error"] = "Product not found.";
                    return RedirectToAction("Index", "Product");
                }

                // Check stock availability
                if (product.StockQuantity < quantity)
                {
                    TempData["Error"] = $"Only {product.StockQuantity} items available in stock.";
                    return RedirectToAction("Details", "Product", new { partitionKey = "Product", rowKey = productId });
                }

                // Get cart and add item
                var cart = GetCart();

                // Check if adding this quantity would exceed stock
                var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == productId);
                var totalQuantity = quantity + (existingItem?.Quantity ?? 0);

                if (totalQuantity > product.StockQuantity)
                {
                    TempData["Error"] = $"Cannot add {quantity} items. Only {product.StockQuantity - (existingItem?.Quantity ?? 0)} more available.";
                    return RedirectToAction("Details", "Product", new { partitionKey = "Product", rowKey = productId });
                }

                cart.AddItem(product, quantity);
                SaveCart(cart);

                TempData["Success"] = $"{product.Name} added to cart!";
                return RedirectToAction("Index", "Product");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding product to cart: {ProductId}", productId);
                TempData["Error"] = "Unable to add product to cart. Please try again.";
                return RedirectToAction("Index", "Product");
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Cart/UpdateQuantity - Update item quantity in cart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateQuantity(string productId, int quantity)
        {
            if (string.IsNullOrEmpty(productId))
            {
                return Json(new { success = false, message = "Invalid product." });
            }

            try
            {
                var cart = GetCart();
                var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);

                if (item == null)
                {
                    return Json(new { success = false, message = "Item not found in cart." });
                }

                // If quantity is 0 or less, remove item
                if (quantity <= 0)
                {
                    cart.RemoveItem(productId);
                    SaveCart(cart);
                    return Json(new
                    {
                        success = true,
                        message = "Item removed from cart.",
                        cartTotal = cart.GetTotal(),
                        cartCount = cart.GetItemCount()
                    });
                }

                // Check stock availability
                var product = await _tableService.GetProductByIdAsync("Product", productId);

                if (product == null)
                {
                    return Json(new { success = false, message = "Product not found." });
                }

                if (quantity > product.StockQuantity)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Only {product.StockQuantity} items available in stock."
                    });
                }

                // Update quantity
                cart.UpdateQuantity(productId, quantity);
                SaveCart(cart);

                return Json(new
                {
                    success = true,
                    message = "Cart updated successfully.",
                    itemSubtotal = item.GetSubtotal(),
                    cartTotal = cart.GetTotal(),
                    cartCount = cart.GetItemCount()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating cart quantity: {ProductId}", productId);
                return Json(new { success = false, message = "Unable to update cart. Please try again." });
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Cart/RemoveItem - Remove item from cart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RemoveItem(string productId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                TempData["Error"] = "Invalid product.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var cart = GetCart();
                cart.RemoveItem(productId);
                SaveCart(cart);

                TempData["Success"] = "Item removed from cart.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing item from cart: {ProductId}", productId);
                TempData["Error"] = "Unable to remove item. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Cart/Clear - Clear all items from cart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Clear()
        {
            try
            {
                var cart = new Cart();
                SaveCart(cart);

                TempData["Success"] = "Cart cleared successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing cart");
                TempData["Error"] = "Unable to clear cart. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Cart/Checkout - Display checkout page
        [HttpGet]
        public IActionResult Checkout()
        {
            try
            {
                var cart = GetCart();

                if (!cart.Items.Any())
                {
                    TempData["Error"] = "Your cart is empty.";
                    return RedirectToAction("Index", "Product");
                }

                return View(cart);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading checkout");
                TempData["Error"] = "Unable to load checkout. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Cart/ProcessCheckout - Process checkout and create orders
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessCheckout()
        {
            try
            {
                var cart = GetCart();

                if (!cart.Items.Any())
                {
                    TempData["Error"] = "Your cart is empty.";
                    return RedirectToAction("Index", "Product");
                }

                // Get customer ID
                var customerIdClaim = User.FindFirst("CustomerId")?.Value;
                if (string.IsNullOrEmpty(customerIdClaim))
                {
                    TempData["Error"] = "Unable to identify customer. Please log in again.";
                    return RedirectToAction("Login", "Account");
                }

                var orderIds = new List<string>();
                var failedItems = new List<string>();

                // Process each cart item as a separate order
                foreach (var item in cart.Items)
                {
                    try
                    {
                        // Verify stock availability one more time
                        var product = await _tableService.GetProductByIdAsync("Product", item.ProductId);

                        if (product == null)
                        {
                            failedItems.Add($"{item.ProductName} (product not found)");
                            continue;
                        }

                        if (product.StockQuantity < item.Quantity)
                        {
                            failedItems.Add($"{item.ProductName} (insufficient stock)");
                            continue;
                        }

                        // Create order
                        var order = new Order
                        {
                            RowKey = Guid.NewGuid().ToString(),
                            PartitionKey = "Order",
                            CustomerId = customerIdClaim,
                            ProductId = item.ProductId,
                            Quantity = item.Quantity,
                            TotalPrice = item.GetSubtotal(),
                            OrderDate = DateTime.UtcNow,
                            Status = "Pending"
                        };

                        // Process order via Azure Function or direct service
                        var functionUrl = _configuration["AzureFunctions:ProcessCompleteOrderUrl"];

                        if (!string.IsNullOrEmpty(functionUrl))
                        {
                            await ProcessOrderViaFunction(order, functionUrl);
                        }
                        else
                        {
                            await ProcessOrderDirectly(order);
                        }

                        orderIds.Add(order.RowKey);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing order for product: {ProductId}", item.ProductId);
                        failedItems.Add($"{item.ProductName} (processing error)");
                    }
                }

                // Clear cart after successful orders
                if (orderIds.Any())
                {
                    var cart2 = new Cart();
                    SaveCart(cart2);
                }

                // Build result message
                if (orderIds.Any() && !failedItems.Any())
                {
                    TempData["Success"] = $"Checkout successful! {orderIds.Count} order(s) placed.";
                    return RedirectToAction("MyOrders", "Account");
                }
                else if (orderIds.Any() && failedItems.Any())
                {
                    TempData["Warning"] = $"{orderIds.Count} order(s) placed successfully. Failed items: {string.Join(", ", failedItems)}";
                    return RedirectToAction(nameof(Index));
                }
                else
                {
                    TempData["Error"] = $"Checkout failed. Failed items: {string.Join(", ", failedItems)}";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing checkout");
                TempData["Error"] = "Unable to process checkout. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Process order via Azure Function
        private async Task ProcessOrderViaFunction(Order order, string functionUrl)
        {
            var functionKey = _configuration["AzureFunctions:FunctionKey"];

            if (!string.IsNullOrEmpty(functionKey))
            {
                var separator = functionUrl.Contains("?") ? "&" : "?";
                functionUrl = $"{functionUrl}{separator}code={functionKey}";
            }

            var jsonContent = JsonSerializer.Serialize(order, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new System.Net.Http.StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            _logger.LogInformation("Calling Azure Function for order: {OrderId}", order.RowKey);
            var response = await _httpClient.PostAsync(functionUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Azure Function call failed: {StatusCode}, {Error}", response.StatusCode, errorContent);
                throw new InvalidOperationException($"Failed to process order via Azure Function: {response.StatusCode}");
            }

            _logger.LogInformation("Order processed via Azure Function: {OrderId}", order.RowKey);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Process order directly (fallback)
        private async Task ProcessOrderDirectly(Order order)
        {
            // Update product stock
            var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

            if (product == null)
            {
                throw new InvalidOperationException($"Product {order.ProductId} not found");
            }

            if (product.StockQuantity < order.Quantity)
            {
                throw new InvalidOperationException($"Insufficient stock. Available: {product.StockQuantity}, Requested: {order.Quantity}");
            }

            product.StockQuantity -= order.Quantity;
            await _tableService.UpdateProductAsync(product);

            // Store order
            await _tableService.InsertOrderAsync(order);

            _logger.LogInformation("Order processed directly: {OrderId}", order.RowKey);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Cart/GetCartCount - AJAX endpoint for cart badge
        [HttpGet]
        public IActionResult GetCartCount()
        {
            try
            {
                var cart = GetCart();
                return Json(new { count = cart.GetItemCount() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cart count");
                return Json(new { count = 0 });
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//