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

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class CustomerController : Controller
    {
        private readonly TableService _tableService;
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        public CustomerController(TableService tableService)// Constructor with TableService injection
        {
            _tableService = tableService; // Dependency Injection of TableService
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // this is the main page that lists all customers
        // method to display all customers
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Customer/Create
        [HttpGet]
        public IActionResult Create()
        {
            var customer = new Customer(); // This will initialize RowKey and PartitionKey
            return View(customer);
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Customer/Create
        // This method handles the creation of a new customer
        // It validates the model and saves it to Azure Table Storage
        // If successful, it redirects to the Index action
        // If there are validation errors, it redisplays the form with error messages
        // It also includes error handling to log exceptions and inform the user
        // Ensure to include anti-forgery token in the form for security
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

            if (ModelState.IsValid)// Check if the model is valid, if so, proceed to save
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
                foreach (var modelState in ModelState)// Log validation errors, if any, for debugging purposes
                {
                    foreach (var error in modelState.Value.Errors)
                    {
                        Console.WriteLine($"Validation error for {modelState.Key}: {error.ErrorMessage}");
                    }
                }
            }

            return View(customer);
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Customer/Edit/5
        // This method retrieves the customer to be edited based on PartitionKey and RowKey
        // It handles cases where the customer is not found or an error occurs
        // It returns the edit view with the customer data if successful
        // If there are issues, it redirects to the Index action with an error message
        // Ensure to include anti-forgery token in the form for security
        // The route parameters are validated to ensure they are not null or empty
        // The method uses async/await for asynchronous operations
        // It logs errors to the console for debugging purposes
        // The method returns appropriate HTTP status codes for different scenarios (NotFound, BadRequest)
        // It uses TempData to pass success or error messages between actions
        [HttpGet]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))// Validate route parameters, if invalid, return NotFound
            {
                return NotFound();
            }

            try// Try to retrieve the customer, if not found, return NotFound
            {
                var customer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);
                if (customer == null)
                {
                    return NotFound();
                }
                return View(customer);// Return the edit view with the customer data
            }
            catch (Exception ex)// Catch any exceptions, log the error, and redirect to Index with an error message
            {
                Console.WriteLine($"Error loading customer for edit: {ex.Message}");
                TempData["Error"] = "Unable to load customer for editing. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Customer/Edit/5
        // This method handles the submission of edited customer data
        // It validates the model and updates the customer in Azure Table Storage
        // If successful, it redirects to the Index action
        // If there are validation errors, it redisplays the form with error messages
        // It also includes error handling to log exceptions and inform the user
        // Ensure to include anti-forgery token in the form for security
        // The route parameters are validated to ensure they match the model data
        // The method uses async/await for asynchronous operations
        // It logs errors to the console for debugging purposes
        // The method returns appropriate HTTP status codes for different scenarios (NotFound, BadRequest)
        // It uses TempData to pass success or error messages between actions
        // It removes RowKey and PartitionKey from ModelState validation since they shouldn't be changed
        // It retrieves the existing entity to ensure the latest ETag is used for concurrency
        // It updates only the editable fields while preserving audit fields like ETag and Timestamp
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

            if (ModelState.IsValid)// If the model is valid, proceed to update
            {
                try// Try to update the customer
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
                catch (Exception ex)// Catch any exceptions, log the error, and add a model error
                {
                    Console.WriteLine($"Error updating customer: {ex.Message}");
                    ModelState.AddModelError("", "Unable to update customer. Please try again.");
                }
            }
            else
            {
                foreach (var modelState in ModelState)// Log validation errors, if any, for debugging purposes
                {
                    foreach (var error in modelState.Value.Errors)// Log each validation error
                    {
                        Console.WriteLine($"Validation error for {modelState.Key}: {error.ErrorMessage}");
                    }
                }
            }

            return View(customer);
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET: Customer/Delete/5
        // This method retrieves the customer to be deleted based on PartitionKey and RowKey
        // It handles cases where the customer is not found or an error occurs
        // It returns the delete confirmation view with the customer data if successful
        // If there are issues, it redirects to the Index action with an error message
        // Ensure to include anti-forgery token in the form for security
        // The route parameters are validated to ensure they are not null or empty
        // The method uses async/await for asynchronous operations
        // It logs errors to the console for debugging purposes
        // The method returns appropriate HTTP status codes for different scenarios (NotFound, BadRequest)
        // It uses TempData to pass success or error messages between actions
        // It displays a confirmation view before deletion to prevent accidental deletions
        [HttpGet]
        public async Task<IActionResult> Delete(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))// Validate route parameters, if invalid, return NotFound
            {
                return NotFound();
            }

            try// Try to retrieve the customer, if not found, return NotFound
            {
                var customer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);// Retrieve the customer to confirm deletion
                if (customer == null)// If customer not found, return NotFound
                {
                    return NotFound();
                }
                return View(customer);
            }
            catch (Exception ex)// Catch any exceptions, log the error, and redirect to Index with an error message
            {
                Console.WriteLine($"Error loading customer for delete: {ex.Message}");
                TempData["Error"] = "Unable to load customer for deletion. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST: Customer/Delete/5
        // This method handles the deletion of a customer
        // It validates the route parameters and deletes the customer from Azure Table Storage
        // If successful, it redirects to the Index action
        // If there are issues, it redirects to the Index action with an error message
        // Ensure to include anti-forgery token in the form for security
        // The route parameters are validated to ensure they are not null or empty
        // The method uses async/await for asynchronous operations
        // It logs errors to the console for debugging purposes
        // The method returns appropriate HTTP status codes for different scenarios (NotFound, BadRequest)
        // It uses TempData to pass success or error messages between actions
        // It confirms deletion to prevent accidental deletions
        // The method is decorated with ActionName to differentiate between GET and POST requests for the same action
        // It handles exceptions gracefully and informs the user of any issues
        // It ensures that only valid requests can trigger the deletion process
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))// Validate route parameters, if invalid, return NotFound
            {
                return NotFound();
            }

            try// Try to delete the customer
            {
                await _tableService.DeleteCustomerAsync(partitionKey, rowKey);
                TempData["Success"] = "Customer deleted successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)// Catch any exceptions, log the error, and redirect to Index with an error message
            {
                Console.WriteLine($"Error deleting customer: {ex.Message}");
                TempData["Error"] = "Unable to delete customer. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//