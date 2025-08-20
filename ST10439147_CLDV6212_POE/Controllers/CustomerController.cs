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

        // GET: Customer
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

        // GET: Customer/Details/5
        public async Task<IActionResult> Details(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                return NotFound();
            }

            try
            {
                var customer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);
                if (customer == null)
                {
                    return NotFound();
                }
                return View(customer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading customer details: {ex.Message}");
                ViewBag.Error = "Unable to load customer details. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Customer/Create
        [HttpGet]
        public IActionResult Create()
        {
            var customer = new Customer(); // This will initialize RowKey and PartitionKey
            return View(customer);
        }

        // POST: Customer/Create
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

        // GET: Customer/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                return NotFound();
            }

            try
            {
                var customer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);
                if (customer == null)
                {
                    return NotFound();
                }
                return View(customer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading customer for edit: {ex.Message}");
                TempData["Error"] = "Unable to load customer for editing. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Customer/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey, Customer customer)
        {
            // Ensure the route parameters match the model
            if (partitionKey != customer.PartitionKey || rowKey != customer.RowKey)
            {
                return BadRequest("Route parameters don't match the customer data.");
            }

            // Remove RowKey and PartitionKey from ModelState validation since they shouldn't be changed
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");

            if (ModelState.IsValid)
            {
                try
                {
                    // Get the current entity to ensure we have the latest ETag
                    var existingCustomer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);
                    if (existingCustomer == null)
                    {
                        return NotFound();
                    }

                    // Update the fields but keep the original ETag and Timestamp
                    existingCustomer.FirstName = customer.FirstName;
                    existingCustomer.LastName = customer.LastName;
                    existingCustomer.Email = customer.Email;
                    existingCustomer.PhoneNumber = customer.PhoneNumber;

                    await _tableService.UpdateCustomerAsync(existingCustomer);
                    TempData["Success"] = "Customer updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating customer: {ex.Message}");
                    ModelState.AddModelError("", "Unable to update customer. Please try again.");
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

        // GET: Customer/Delete/5
        [HttpGet]
        public async Task<IActionResult> Delete(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                return NotFound();
            }

            try
            {
                var customer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);
                if (customer == null)
                {
                    return NotFound();
                }
                return View(customer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading customer for delete: {ex.Message}");
                TempData["Error"] = "Unable to load customer for deletion. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Customer/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                return NotFound();
            }

            try
            {
                await _tableService.DeleteCustomerAsync(partitionKey, rowKey);
                TempData["Success"] = "Customer deleted successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting customer: {ex.Message}");
                TempData["Error"] = "Unable to delete customer. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}