using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class OrderController : Controller
    {
        private readonly AzureService _storageService;

        public OrderController(AzureService storageService)
        {
            _storageService = storageService;
        }

        public async Task<IActionResult> Index()
        {
            var orders = await _storageService.GetAllOrdersAsync();
            return View(orders);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Customers = await _storageService.GetAllCustomersAsync();
            ViewBag.Products = await _storageService.GetAllProductsAsync();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(Order order)
        {
            if (ModelState.IsValid)
            {
                order.OrderDate = DateTime.UtcNow;
                order.Status = "Pending";

                var savedOrder = await _storageService.AddOrderAsync(order);

                // Send message to queue for order processing
                var orderMessage = new OrderMessage
                {
                    OrderId = savedOrder.RowKey,
                    CustomerId = savedOrder.CustomerId,
                    ProductId = savedOrder.ProductId,
                    Quantity = savedOrder.Quantity,
                    TotalPrice = savedOrder.TotalPrice,
                    OrderDate = savedOrder.OrderDate,
                    Action = "NewOrder"
                };

                await _storageService.SendOrderMessageAsync(orderMessage);
                await _storageService.SendInventoryMessageAsync($"Processing order {savedOrder.RowKey} - Quantity: {savedOrder.Quantity}");

                return RedirectToAction(nameof(Index));
            }

            ViewBag.Customers = await _storageService.GetAllCustomersAsync();
            ViewBag.Products = await _storageService.GetAllProductsAsync();
            return View(order);
        }
    }
}
