using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class CustomerController : Controller
    {
        private readonly TableService _tableService;

        public CustomerController(TableService tableService)
        {
            _tableService = tableService;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var customers = await _tableService.GetAllCustomersAsync();
                return View(customers);
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error loading customers: {ex.Message}");
                ViewBag.Error = "Unable to load customers. Please try again.";
                return View(new List<Customer>());
            }
        }

        [HttpGet]
        public IActionResult Create()
        {
            var customer = new Customer(); // This will initialize RowKey and PartitionKey
            return View(customer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Customer customer)
        {
            // Ensure RowKey and PartitionKey are set BEFORE model validation
            if (string.IsNullOrEmpty(customer.RowKey))
            {
                customer.RowKey = Guid.NewGuid().ToString();
            }

            if (string.IsNullOrEmpty(customer.PartitionKey))
            {
                customer.PartitionKey = "Customer"; // Match the default in your model
            }

            // Remove RowKey and PartitionKey from ModelState validation
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");

            if (ModelState.IsValid)
            {
                try
                {
                    await _tableService.InsertCustomerAsync(customer);
                    TempData["Success"] = "Customer added successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving customer: {ex.Message}");
                    ModelState.AddModelError("", "Unable to save customer. Please try again.");
                }
            }
            else
            {
                foreach (var modelState in ModelState)
                {
                    foreach (var error in modelState.Value.Errors)
                    {
                        Console.WriteLine($"Validation error for {modelState.Key}: {error.ErrorMessage}");
                    }
                }
            }

            return View(customer);
        }
    }
}