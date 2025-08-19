using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class OrderController : Controller
    {
        private readonly TableService _tableService;
        private readonly QueueService _queueService;

        public OrderController(TableService tableService, QueueService queueService)
        {
            _tableService = tableService;
            _queueService = queueService;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var orders = await _tableService.GetAllOrdersAsync();
                return View(orders);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Unable to load orders. Please try again.";
                return View(new List<Order>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            try
            {
                ViewBag.Customers = await _tableService.GetAllCustomersAsync();
                ViewBag.Products = await _tableService.GetAllProductsAsync();
                return View();
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Unable to load data for order creation.";
                return View();
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create(Order order)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    order.OrderDate = DateTime.UtcNow;
                    order.Status = "Pending";

                    // Insert the order first
                    await _tableService.InsertOrderAsync(order);

                    // Send message to queue for order processing
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
                    await _queueService.SendInventoryMessageAsync($"Processing order {order.RowKey} - Quantity: {order.Quantity}");

                    TempData["Success"] = "Order created successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "Unable to create order. Please try again.");
                }
            }

            try
            {
                ViewBag.Customers = await _tableService.GetAllCustomersAsync();
                ViewBag.Products = await _tableService.GetAllProductsAsync();
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Unable to load data for order creation.";
            }

            return View(order);
        }
    }
}