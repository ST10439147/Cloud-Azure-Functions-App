// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 1

//References:
// ClaudAI - https://claude.ai/
// ChatGPT - https://chat.openai.com/
// W3schools - https://www.w3schools.com/
// IIEVC School of Computer Science Youtube channel for Azure services setup and use https://www.youtube.com/@VCSOCS
// AzureApp project done in class with lecturer

using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;
using Microsoft.Extensions.Logging;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class CustomerController : Controller
    {
        private readonly TableService _tableService;
        private readonly ILogger<CustomerController> _logger;

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        public CustomerController(TableService tableService, ILogger<CustomerController> logger)
        {
            _tableService = tableService;
            _logger = logger;
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Customer/Index
        public async Task<IActionResult> Index()
        {
            try
            {
                _logger.LogInformation("Loading all customers");
                var customers = await _tableService.GetAllCustomersAsync();
                return View(customers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading customers");
                ViewBag.Error = "Unable to load customers. Please try again.";
                return View(new List<Customer>());
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Customer/Create
        [HttpGet]
        public IActionResult Create()
        {
            var customer = new Customer();
            return View(customer);
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
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
                customer.PartitionKey = "Customer";
            }

            // Remove RowKey and PartitionKey from ModelState validation
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");

            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation($"Creating customer: {customer.Email}");
                    await _tableService.InsertCustomerAsync(customer);
                    TempData["Success"] = "Customer added successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving customer");
                    ModelState.AddModelError("", "Unable to save customer. Please try again.");
                }
            }
            else
            {
                foreach (var modelState in ModelState)
                {
                    foreach (var error in modelState.Value.Errors)
                    {
                        _logger.LogWarning($"Validation error for {modelState.Key}: {error.ErrorMessage}");
                    }
                }
            }

            return View(customer);
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Customer/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Edit called with null or empty keys");
                return NotFound();
            }

            try
            {
                _logger.LogInformation($"Loading customer for edit: {partitionKey}/{rowKey}");
                var customer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);
                if (customer == null)
                {
                    _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                    return NotFound();
                }
                return View(customer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading customer for edit: {partitionKey}/{rowKey}");
                TempData["Error"] = "Unable to load customer for editing. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Customer/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey, Customer customer)
        {
            // Ensure the route parameters match the model
            if (partitionKey != customer.PartitionKey || rowKey != customer.RowKey)
            {
                _logger.LogWarning("Route parameters don't match customer data");
                return BadRequest("Route parameters don't match the customer data.");
            }

            // Remove RowKey and PartitionKey from ModelState validation
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");

            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation($"Updating customer: {partitionKey}/{rowKey}");

                    // Get the current entity to ensure we have the latest ETag
                    var existingCustomer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);
                    if (existingCustomer == null)
                    {
                        _logger.LogWarning($"Customer not found for update: {partitionKey}/{rowKey}");
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
                catch (InvalidOperationException ex) when (ex.Message.Contains("modified by another user"))
                {
                    _logger.LogWarning(ex, "Concurrency conflict updating customer");
                    ModelState.AddModelError("", "The customer has been modified by another user. Please refresh and try again.");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("no longer exists"))
                {
                    _logger.LogWarning(ex, "Customer no longer exists");
                    TempData["Error"] = "The customer no longer exists.";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating customer: {partitionKey}/{rowKey}");
                    ModelState.AddModelError("", "Unable to update customer. Please try again.");
                }
            }
            else
            {
                foreach (var modelState in ModelState)
                {
                    foreach (var error in modelState.Value.Errors)
                    {
                        _logger.LogWarning($"Validation error for {modelState.Key}: {error.ErrorMessage}");
                    }
                }
            }

            return View(customer);
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Customer/Delete/5
        [HttpGet]
        public async Task<IActionResult> Delete(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Delete called with null or empty keys");
                return NotFound();
            }

            try
            {
                _logger.LogInformation($"Loading customer for delete: {partitionKey}/{rowKey}");
                var customer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);
                if (customer == null)
                {
                    _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                    return NotFound();
                }
                return View(customer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading customer for delete: {partitionKey}/{rowKey}");
                TempData["Error"] = "Unable to load customer for deletion. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Customer/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("DeleteConfirmed called with null or empty keys");
                return NotFound();
            }

            try
            {
                _logger.LogInformation($"Deleting customer: {partitionKey}/{rowKey}");
                await _tableService.DeleteCustomerAsync(partitionKey, rowKey);
                TempData["Success"] = "Customer deleted successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting customer: {partitionKey}/{rowKey}");
                TempData["Error"] = "Unable to delete customer. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//