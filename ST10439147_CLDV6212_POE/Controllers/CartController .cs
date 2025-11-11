// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - Shopping Cart Controller

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;
using System.Security.Claims;

namespace ST10439147_CLDV6212_POE.Controllers
{
    /// <summary>
    /// Controller for managing shopping cart operations
    /// Customers can add products, view cart, and place orders
    /// </summary>
    [Authorize(Roles = "Customer")]
    public class CartController : Controller
    {
        private readonly TableService _tableService;
        private readonly QueueService _queueService;
        private readonly ILogger<CartController> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private const string CartSessionKey = "ShoppingCart";

        public CartController(
            TableService tableService,
            QueueService queueService,
            ILogger<CartController> logger,
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Cart/Index - View shopping cart
        [HttpGet]
        public IActionResult Index()
        {
            var cart = GetCart();
            return View(cart);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Cart/AddToCart - Add product to cart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToCart(string productId, int quantity = 1)
        {
            try
            {
                if (string.IsNullOrEmpty(productId))
                {
                    TempData["Error"] = "Product not found.";
                    return RedirectToAction("Index", "Product");
                }

                if (quantity <= 0)
                {
                    TempData["Error"] = "Quantity must be greater than 0.";
                    return RedirectToAction("Details", "Product", new { id = productId });
                }

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
                    return RedirectToAction("Details", "Product", new { id = productId });
                }

                // Get cart from session
                var cart = GetCart();

                // Check if adding this quantity would exceed stock
                var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == productId);
                int totalQuantity = quantity + (existingItem?.Quantity ?? 0);

                if (totalQuantity > product.StockQuantity)
                {
                    TempData["Error"] = $"Cannot add {quantity} items. Only {product.StockQuantity - (existingItem?.Quantity ?? 0)} more available.";
                    return RedirectToAction("Details", "Product", new { id = productId });
                }

                // Add to cart
                cart.AddItem(product, quantity);
                SaveCart(cart);

                _logger.LogInformation("Product added to cart: {ProductId}, Quantity: {Quantity}", productId, quantity);
                TempData["Success"] = $"{product.Name} added to cart!";

                return RedirectToAction(nameof(Index));
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
            try
            {
                if (quantity < 0)
                {
                    TempData["Error"] = "Invalid quantity.";
                    return RedirectToAction(nameof(Index));
                }

                var cart = GetCart();

                if (quantity == 0)
                {
                    cart.RemoveItem(productId);
                    SaveCart(cart);
                    TempData["Success"] = "Item removed from cart.";
                    return RedirectToAction(nameof(Index));
                }

                // Check stock availability
                var product = await _tableService.GetProductByIdAsync("Product", productId);

                if (product == null)
                {
                    TempData["Error"] = "Product not found.";
                    return RedirectToAction(nameof(Index));
                }

                if (quantity > product.StockQuantity)
                {
                    TempData["Error"] = $"Only {product.StockQuantity} items available in stock.";
                    return RedirectToAction(nameof(Index));
                }

                cart.UpdateQuantity(productId, quantity);
                SaveCart(cart);

                TempData["Success"] = "Cart updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating cart quantity: {ProductId}", productId);
                TempData["Error"] = "Unable to update cart. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Cart/RemoveItem - Remove item from cart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RemoveItem(string productId)
        {
            try
            {
                var cart = GetCart();
                cart.RemoveItem(productId);
                SaveCart(cart);

                _logger.LogInformation("Item removed from cart: {ProductId}", productId);
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
                var cart = GetCart();
                cart.Clear();
                SaveCart(cart);

                _logger.LogInformation("Cart cleared");
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
        // POST: Cart/Checkout - Process checkout and create orders
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout()
        {
            try
            {
                var cart = GetCart();

                if (!cart.Items.Any())
                {
                    TempData["Error"] = "Your cart is empty.";
                    return RedirectToAction(nameof(Index));
                }

                // Get customer ID from claims
                var customerIdClaim = User.FindFirst("CustomerId")?.Value;
                if (string.IsNullOrEmpty(customerIdClaim))
                {
                    TempData["Error"] = "Unable to identify customer. Please log in again.";
                    return RedirectToAction("Login", "Account");
                }

                // Get Azure Function URL
                var functionUrl = _configuration["AzureFunctions:ProcessCompleteOrderUrl"];
                var functionKey = _configuration["AzureFunctions:FunctionKey"];

                int successCount = 0;
                int failCount = 0;
                var errors = new List<string>();

                // Process each cart item as a separate order
                foreach (var item in cart.Items)
                {
                    try
                    {
                        var order = new Order
                        {
                            PartitionKey = "Order",
                            RowKey = Guid.NewGuid().ToString(),
                            CustomerId = customerIdClaim,
                            ProductId = item.ProductId,
                            Quantity = item.Quantity,
                            TotalPrice = item.GetSubtotal(),
                            OrderDate = DateTime.UtcNow,
                            Status = "Pending"
                        };

                        if (!string.IsNullOrEmpty(functionUrl))
                        {
                            // Use Azure Function
                            await ProcessOrderViaFunction(order, functionUrl, functionKey);
                        }
                        else
                        {
                            // Direct processing fallback
                            await ProcessOrderDirectly(order);
                        }

                        successCount++;
                        _logger.LogInformation("Order placed successfully: {OrderId}, Product: {ProductId}",
                            order.RowKey, item.ProductId);
                    }
                    catch (Exception orderEx)
                    {
                        failCount++;
                        var errorMsg = $"{item.ProductName}: {orderEx.Message}";
                        errors.Add(errorMsg);
                        _logger.LogError(orderEx, "Error processing order for product: {ProductId}", item.ProductId);
                    }
                }

                // Clear cart if at least one order succeeded
                if (successCount > 0)
                {
                    cart.Clear();
                    SaveCart(cart);
                }

                // Set appropriate message
                if (failCount == 0)
                {
                    TempData["Success"] = $"{successCount} order(s) placed successfully!";
                    return RedirectToAction("MyOrders", "Account");
                }
                else if (successCount > 0)
                {
                    TempData["Warning"] = $"{successCount} order(s) placed successfully, but {failCount} failed: {string.Join(", ", errors)}";
                    return RedirectToAction("MyOrders", "Account");
                }
                else
                {
                    TempData["Error"] = $"All orders failed: {string.Join(", ", errors)}";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during checkout");
                TempData["Error"] = "Unable to process checkout. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Helper: Process order via Azure Function
        private async Task ProcessOrderViaFunction(Order order, string functionUrl, string? functionKey)
        {
            if (!string.IsNullOrEmpty(functionKey))
            {
                var separator = functionUrl.Contains("?") ? "&" : "?";
                functionUrl = $"{functionUrl}{separator}code={functionKey}";
            }

            var jsonContent = JsonConvert.SerializeObject(order);
            var content = new System.Net.Http.StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(functionUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Order processing failed: {errorContent}");
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Helper: Process order directly (fallback)
        private async Task ProcessOrderDirectly(Order order)
        {
            // Update stock
            var product = await _tableService.GetProductByIdAsync("Product", order.ProductId);

            if (product == null)
            {
                throw new InvalidOperationException("Product not found");
            }

            if (product.StockQuantity < order.Quantity)
            {
                throw new InvalidOperationException($"Insufficient stock. Available: {product.StockQuantity}");
            }

            int oldStock = product.StockQuantity;
            product.StockQuantity -= order.Quantity;
            await _tableService.UpdateProductAsync(product);

            // Store order
            await _tableService.InsertOrderAsync(order);

            // Queue monitoring messages
            await Task.Delay(500);

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

            var inventoryMessage = $"Stock reduced for order {order.RowKey} - Product: {order.ProductId}, Quantity: {order.Quantity}, Old Stock: {oldStock}, New Stock: {product.StockQuantity}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
            await _queueService.SendInventoryMessageAsync(inventoryMessage);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Helper: Get cart from session
        private Cart GetCart()
        {
            var cartJson = HttpContext.Session.GetString(CartSessionKey);

            if (string.IsNullOrEmpty(cartJson))
            {
                return new Cart();
            }

            try
            {
                return JsonConvert.DeserializeObject<Cart>(cartJson) ?? new Cart();
            }
            catch
            {
                return new Cart();
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Helper: Save cart to session
        private void SaveCart(Cart cart)
        {
            var cartJson = JsonConvert.SerializeObject(cart);
            HttpContext.Session.SetString(CartSessionKey, cartJson);
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//