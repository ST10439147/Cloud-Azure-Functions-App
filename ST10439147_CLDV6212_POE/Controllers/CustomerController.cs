// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2

//References:
// ClaudAI - https://claude.ai/
// ChatGPT - https://chat.openai.com/
// W3schools - https://www.w3schools.com/
// IIEVC School of Computer Science Youtube channel for Azure services setup and use https://www.youtube.com/@VCSOCS
// AzureApp project done in class with lecturer

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Controllers
{
    /// <summary>
    /// Controller responsible for managing customer-related operations.
    /// Communicates with Azure Functions to perform CRUD operations on customer data.
    /// </summary>
    public class CustomerController : Controller
    {
        // HTTP client for making requests to Azure Functions
        private readonly HttpClient _httpClient;

        // Logger for tracking operations and errors
        private readonly ILogger<CustomerController> _logger;

        // Base URL for the Azure Function endpoints
        private readonly string _functionBaseUrl;

        // Authentication key for Azure Functions
        private readonly string _functionKey;

        /// <summary>
        /// Constructor that initializes the controller with required dependencies.
        /// Retrieves Azure Function configuration from app settings.
        /// </summary>
        /// <param name="httpClientFactory">Factory for creating HTTP clients</param>
        /// <param name="logger">Logger instance for this controller</param>
        /// <param name="configuration">Configuration to access app settings</param>
        public CustomerController(IHttpClientFactory httpClientFactory, ILogger<CustomerController> logger, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
            _functionBaseUrl = configuration["AzureFunctions:BaseUrl"];
            _functionKey = configuration["AzureFunctions:FunctionKey"];
        }

        /// <summary>
        /// GET: Customer/Index
        /// Displays a list of all customers or filtered customers based on search criteria.
        /// Supports searching customers by name.
        /// </summary>
        /// <param name="searchName">Optional search parameter to filter customers by name</param>
        /// <returns>View with list of customers</returns>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(string searchName)
        {
            try
            {
                _logger.LogInformation($"Loading customers from Azure Function. Search term: {searchName}");

                HttpRequestMessage request;

                // Determine which endpoint to call based on whether a search term was provided
                if (!string.IsNullOrWhiteSpace(searchName))
                {
                    // Use search endpoint if search term is provided
                    // URI.EscapeDataString ensures special characters are properly encoded
                    request = new HttpRequestMessage(HttpMethod.Get,
                        $"{_functionBaseUrl}/customers/search?name={Uri.EscapeDataString(searchName)}");
                }
                else
                {
                    // Use regular endpoint to retrieve all customers
                    request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/customers");
                }

                // Add authentication header for Azure Function authorization
                request.Headers.Add("x-functions-key", _functionKey);

                // Send the HTTP request to Azure Function
                var response = await _httpClient.SendAsync(request);

                // Check if the request was successful (2xx status code)
                if (response.IsSuccessStatusCode)
                {
                    // Read the JSON response content
                    var content = await response.Content.ReadAsStringAsync();

                    // Deserialize JSON into a list of Customer objects
                    // PropertyNameCaseInsensitive allows matching regardless of casing
                    var customers = JsonSerializer.Deserialize<List<Customer>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    // Store search term and result count in ViewBag for display in the view
                    ViewBag.SearchName = searchName;
                    ViewBag.ResultCount = customers?.Count ?? 0;

                    return View(customers);
                }

                // Log error details if the request failed
                _logger.LogError($"Error loading customers: {response.StatusCode}");
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Error details: {errorContent}");

                // Display error message to user and return empty list
                ViewBag.Error = "Unable to load customers. Please try again.";
                return View(new List<Customer>());
            }
            catch (Exception ex)
            {
                // Catch any unexpected errors and log them
                _logger.LogError(ex, "Error loading customers");
                ViewBag.Error = "Unable to load customers. Please try again.";
                return View(new List<Customer>());
            }
        }

        /// <summary>
        /// GET: Customer/Create
        /// Displays the form for creating a new customer.
        /// </summary>
        /// <returns>View with empty customer model</returns>
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public IActionResult Create()
        {
            // Create a new empty customer object for the form
            var customer = new Customer();
            return View(customer);
        }

        /// <summary>
        /// POST: Customer/Create
        /// Processes the form submission to create a new customer.
        /// Sends customer data to Azure Function for storage in Azure Table Storage.
        /// </summary>
        /// <param name="customer">Customer object populated from the form</param>
        /// <returns>Redirects to Index on success, returns to form on failure</returns>
        [ Authorize(Roles = "Customer")]
        [HttpPost]
        [ValidateAntiForgeryToken] // Protects against CSRF attacks
        public async Task<IActionResult> Create(Customer customer)
        {
            // Generate a unique RowKey (identifier) if not provided
            if (string.IsNullOrEmpty(customer.RowKey))
            {
                customer.RowKey = Guid.NewGuid().ToString();
            }

            // Set the PartitionKey for Azure Table Storage organization
            if (string.IsNullOrEmpty(customer.PartitionKey))
            {
                customer.PartitionKey = "Customer";
            }

            // Remove RowKey and PartitionKey from validation since they're auto-generated
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");

            // Validate all other model properties
            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation($"Creating customer: {customer.Email}");

                    // Create POST request to Azure Function
                    var request = new HttpRequestMessage(HttpMethod.Post, $"{_functionBaseUrl}/customers");
                    request.Headers.Add("x-functions-key", _functionKey);

                    // Serialize customer object to JSON
                    var jsonContent = JsonSerializer.Serialize(customer);
                    request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    // Send the request to create the customer
                    var response = await _httpClient.SendAsync(request);

                    // Check if creation was successful
                    if (response.IsSuccessStatusCode)
                    {
                        // Store success message in TempData (persists across redirect)
                        TempData["Success"] = "Customer added successfully!";
                        return RedirectToAction(nameof(Index));
                    }

                    // Log error details if creation failed
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Error creating customer: {response.StatusCode} - {errorContent}");
                    ModelState.AddModelError("", "Unable to save customer. Please try again.");
                }
                catch (Exception ex)
                {
                    // Handle any unexpected errors during the creation process
                    _logger.LogError(ex, "Error saving customer");
                    ModelState.AddModelError("", "Unable to save customer. Please try again.");
                }
            }

            // Return to the form with validation errors if anything failed
            return View(customer);
        }

        /// <summary>
        /// GET: Customer/Edit/5
        /// Retrieves and displays a customer's current data for editing.
        /// </summary>
        /// <param name="partitionKey">Azure Table Storage partition key</param>
        /// <param name="rowKey">Azure Table Storage row key (unique identifier)</param>
        /// <returns>View with customer data for editing</returns>
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey)
        {
            // Validate that both keys are provided
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Edit called with null or empty keys");
                return NotFound();
            }

            try
            {
                _logger.LogInformation($"Loading customer for edit: {partitionKey}/{rowKey}");

                // Create GET request to retrieve specific customer
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/customers/{partitionKey}/{rowKey}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                // Handle case where customer doesn't exist
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                    return NotFound();
                }

                // Deserialize and return customer data if found
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var customer = JsonSerializer.Deserialize<Customer>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return View(customer);
                }

                throw new Exception($"Error loading customer: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                // Log error and redirect back to index with error message
                _logger.LogError(ex, $"Error loading customer for edit: {partitionKey}/{rowKey}");
                TempData["Error"] = "Unable to load customer for editing. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// POST: Customer/Edit/5
        /// Processes the form submission to update an existing customer.
        /// Validates that route parameters match the customer data for security.
        /// </summary>
        /// <param name="partitionKey">Partition key from route</param>
        /// <param name="rowKey">Row key from route</param>
        /// <param name="customer">Updated customer data from form</param>
        /// <returns>Redirects to Index on success, returns to form on failure</returns>
        [Authorize(Roles = "Customer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey, Customer customer)
        {
            // Security check: ensure route parameters match the customer object
            // This prevents users from modifying the wrong customer
            if (partitionKey != customer.PartitionKey || rowKey != customer.RowKey)
            {
                _logger.LogWarning("Route parameters don't match customer data");
                return BadRequest("Route parameters don't match the customer data.");
            }

            // Remove keys from validation (they're identifiers, not editable fields)
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");

            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation($"Updating customer: {partitionKey}/{rowKey}");

                    // Create PUT request to update customer
                    var request = new HttpRequestMessage(HttpMethod.Put, $"{_functionBaseUrl}/customers/{partitionKey}/{rowKey}");
                    request.Headers.Add("x-functions-key", _functionKey);

                    // Serialize updated customer data to JSON
                    var jsonContent = JsonSerializer.Serialize(customer);
                    request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);

                    // Handle successful update
                    if (response.IsSuccessStatusCode)
                    {
                        TempData["Success"] = "Customer updated successfully!";
                        return RedirectToAction(nameof(Index));
                    }

                    // Handle case where customer was deleted by another user
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        TempData["Error"] = "The customer no longer exists.";
                        return RedirectToAction(nameof(Index));
                    }

                    // Handle concurrent modification conflict (ETag mismatch)
                    if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        ModelState.AddModelError("", "The customer has been modified by another user. Please refresh and try again.");
                        return View(customer);
                    }

                    throw new Exception($"Error updating customer: {response.StatusCode}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating customer: {partitionKey}/{rowKey}");
                    ModelState.AddModelError("", "Unable to update customer. Please try again.");
                }
            }

            // Return to edit form with validation errors
            return View(customer);
        }

        /// <summary>
        /// GET: Customer/Delete/5
        /// Displays customer details and confirmation prompt before deletion.
        /// </summary>
        /// <param name="partitionKey">Partition key of customer to delete</param>
        /// <param name="rowKey">Row key of customer to delete</param>
        /// <returns>View with customer details for confirmation</returns>
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<IActionResult> Delete(string partitionKey, string rowKey)
        {
            // Validate that both keys are provided
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Delete called with null or empty keys");
                return NotFound();
            }

            try
            {
                _logger.LogInformation($"Loading customer for delete: {partitionKey}/{rowKey}");

                // Retrieve customer data to display for confirmation
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/customers/{partitionKey}/{rowKey}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                // Handle case where customer doesn't exist
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                    return NotFound();
                }

                // Deserialize and display customer data
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var customer = JsonSerializer.Deserialize<Customer>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return View(customer);
                }

                throw new Exception($"Error loading customer: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading customer for delete: {partitionKey}/{rowKey}");
                TempData["Error"] = "Unable to load customer for deletion. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// POST: Customer/Delete/5
        /// Processes the confirmed deletion of a customer.
        /// Uses ActionName attribute to map to the Delete action while maintaining RESTful naming.
        /// </summary>
        /// <param name="partitionKey">Partition key of customer to delete</param>
        /// <param name="rowKey">Row key of customer to delete</param>
        /// <returns>Redirects to Index with success or error message</returns>
        [Authorize(Roles = "Customer")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string partitionKey, string rowKey)
        {
            // Validate that both keys are provided
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("DeleteConfirmed called with null or empty keys");
                return NotFound();
            }

            try
            {
                _logger.LogInformation($"Deleting customer: {partitionKey}/{rowKey}");

                // Create DELETE request to remove customer
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{_functionBaseUrl}/customers/{partitionKey}/{rowKey}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                // Handle successful deletion
                if (response.IsSuccessStatusCode)
                {
                    TempData["Success"] = "Customer deleted successfully!";
                    return RedirectToAction(nameof(Index));
                }

                // Handle case where customer was already deleted
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = "Customer not found.";
                    return RedirectToAction(nameof(Index));
                }

                throw new Exception($"Error deleting customer: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                // Log error and redirect with error message
                _logger.LogError(ex, $"Error deleting customer: {partitionKey}/{rowKey}");
                TempData["Error"] = "Unable to delete customer. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//