// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Controllers
{
    /// <summary>
    /// Controller responsible for managing customer profile operations with role-based access control.
    /// Provides separate workflows for customers (self-service) and administrators (full management).
    /// Communicates with Azure Functions to perform CRUD operations on customer data stored in Azure Table Storage.
    /// </summary>
    public class CustomerController : Controller
    {
        // Private fields for dependency injection and configuration
        private readonly HttpClient _httpClient;
        private readonly ILogger<CustomerController> _logger;
        private readonly string _functionBaseUrl;
        private readonly string _functionKey;

        /// <summary>
        /// Initializes a new instance of the CustomerController with required services.
        /// </summary>
        /// <param name="httpClientFactory">Factory for creating HTTP clients to communicate with Azure Functions</param>
        /// <param name="logger">Logger for recording application events and errors</param>
        /// <param name="configuration">Configuration provider for accessing Azure Function settings</param>
        public CustomerController(IHttpClientFactory httpClientFactory, ILogger<CustomerController> logger, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;

            // Retrieve Azure Function configuration
            _functionBaseUrl = configuration["AzureFunctions:BaseUrl"];
            _functionKey = configuration["AzureFunctions:FunctionKey"];
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays a list of all customers for administrators with optional search functionality.
        /// Supports filtering by customer name to quickly locate specific records.
        /// </summary>
        /// <param name="searchName">Optional search term to filter customers by name</param>
        /// <returns>Admin view with list of customers, optionally filtered by search term</returns>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(string searchName)
        {
            try
            {
                _logger.LogInformation($"Loading customers from Azure Function. Search term: {searchName}");

                HttpRequestMessage request;

                // Build appropriate request URL based on search parameter
                if (!string.IsNullOrWhiteSpace(searchName))
                {
                    // Use search endpoint with URL-encoded search term
                    request = new HttpRequestMessage(HttpMethod.Get,
                        $"{_functionBaseUrl}/customers/search?name={Uri.EscapeDataString(searchName)}");
                }
                else
                {
                    // Use standard endpoint to retrieve all customers
                    request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/customers");
                }

                // Add authentication header for Azure Function
                request.Headers.Add("x-functions-key", _functionKey);
                var response = await _httpClient.SendAsync(request);

                // Handle successful response
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    // Deserialize JSON response to customer list
                    var customers = JsonSerializer.Deserialize<List<Customer>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true  // Handle case differences in JSON properties
                    });

                    // Pass search context to view for display
                    ViewBag.SearchName = searchName;
                    ViewBag.ResultCount = customers?.Count ?? 0;

                    return View(customers);
                }

                // Handle unsuccessful response
                _logger.LogError($"Error loading customers: {response.StatusCode}");
                ViewBag.Error = "Unable to load customers. Please try again.";
                return View(new List<Customer>());
            }
            catch (Exception ex)
            {
                // Handle unexpected errors
                _logger.LogError(ex, "Error loading customers");
                ViewBag.Error = "Unable to load customers. Please try again.";
                return View(new List<Customer>());
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays the customer creation form.
        /// This is currently not actively used as customers are created during registration.
        /// </summary>
        /// <returns>Create view with empty customer model</returns>
        [HttpGet]
        public IActionResult Create()
        {
            var customer = new Customer();
            return View(customer);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Processes customer profile creation submitted by authenticated customer.
        /// Generates unique identifiers and submits data to Azure Function for storage.
        /// </summary>
        /// <param name="customer">Customer data from form submission</param>
        /// <returns>Redirect to Index on success, or Create view with errors on failure</returns>
        [Authorize(Roles = "Customer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Customer customer)
        {
            // Generate unique row key if not provided
            if (string.IsNullOrEmpty(customer.RowKey))
            {
                customer.RowKey = Guid.NewGuid().ToString();
            }

            // Set partition key for Azure Table Storage organization
            if (string.IsNullOrEmpty(customer.PartitionKey))
            {
                customer.PartitionKey = "Customer";
            }

            // Remove auto-generated fields from validation
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");

            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation($"Creating customer: {customer.Email}");

                    // Create HTTP request to Azure Function
                    var request = new HttpRequestMessage(HttpMethod.Post, $"{_functionBaseUrl}/customers");
                    request.Headers.Add("x-functions-key", _functionKey);

                    // Serialize customer data to JSON
                    var jsonContent = JsonSerializer.Serialize(customer);
                    request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);

                    // Handle successful creation
                    if (response.IsSuccessStatusCode)
                    {
                        TempData["Success"] = "Customer added successfully!";
                        return RedirectToAction(nameof(Index));
                    }

                    // Handle creation failure
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Error creating customer: {response.StatusCode} - {errorContent}");
                    ModelState.AddModelError("", "Unable to save customer. Please try again.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving customer");
                    ModelState.AddModelError("", "Unable to save customer. Please try again.");
                }
            }

            return View(customer);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Internal method to create customer via Azure Function, bypassing authorization.
        /// Called by AuthenticationService during the registration process to create customer profile.
        /// This method is not an action and does not require authorization attributes.
        /// </summary>
        /// <param name="customer">Customer data to create</param>
        /// <returns>Tuple containing success status, customer ID, and message</returns>
        public async Task<(bool success, string customerId, string message)> CreateCustomerInternalAsync(Customer customer)
        {
            try
            {
                // Generate unique identifiers if not provided
                if (string.IsNullOrEmpty(customer.RowKey))
                {
                    customer.RowKey = Guid.NewGuid().ToString();
                }

                if (string.IsNullOrEmpty(customer.PartitionKey))
                {
                    customer.PartitionKey = "Customer";
                }

                _logger.LogInformation($"Creating customer via Azure Function: {customer.Email}");

                // Create HTTP request to Azure Function
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_functionBaseUrl}/customers");
                request.Headers.Add("x-functions-key", _functionKey);

                // Serialize customer data to JSON
                var jsonContent = JsonSerializer.Serialize(customer);
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);

                // Handle successful creation
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    // Deserialize response to get created customer with ID
                    var createdCustomer = JsonSerializer.Deserialize<Customer>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    _logger.LogInformation($"Customer created successfully: {createdCustomer?.RowKey}");
                    return (true, createdCustomer?.RowKey ?? customer.RowKey, "Customer created successfully");
                }

                // Handle creation failure
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Error creating customer: {response.StatusCode} - {errorContent}");
                return (false, string.Empty, $"Failed to create customer: {errorContent}");
            }
            catch (Exception ex)
            {
                // Handle unexpected errors
                _logger.LogError(ex, "Error creating customer via Azure Function");
                return (false, string.Empty, $"Error creating customer: {ex.Message}");
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays the logged-in customer's own profile information.
        /// Retrieves customer ID from authentication claims to ensure data isolation.
        /// </summary>
        /// <returns>MyProfile view with customer data, or redirect on error</returns>
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<IActionResult> MyProfile()
        {
            try
            {
                // Extract customer ID from authentication claims
                var customerIdClaim = User.FindFirst("CustomerId")?.Value;

                // Validate customer ID exists
                if (string.IsNullOrEmpty(customerIdClaim))
                {
                    TempData["Error"] = "Customer information not found.";
                    return RedirectToAction("Index", "Home");
                }

                _logger.LogInformation($"Loading customer profile: Customer/{customerIdClaim}");

                // Create HTTP request to retrieve specific customer
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{_functionBaseUrl}/customers/Customer/{customerIdClaim}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                // Handle customer not found
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = "Customer profile not found.";
                    return RedirectToAction("Index", "Home");
                }

                // Handle successful retrieval
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    // Deserialize JSON response to customer object
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
                _logger.LogError(ex, "Error loading customer profile");
                TempData["Error"] = "Unable to load your profile. Please try again.";
                return RedirectToAction("Index", "Home");
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays the profile editing form for the logged-in customer.
        /// Retrieves current profile data to pre-populate the form.
        /// </summary>
        /// <returns>EditMyProfile view with customer data, or redirect on error</returns>
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<IActionResult> EditMyProfile()
        {
            try
            {
                // Extract customer ID from authentication claims
                var customerIdClaim = User.FindFirst("CustomerId")?.Value;

                // Validate customer ID exists
                if (string.IsNullOrEmpty(customerIdClaim))
                {
                    TempData["Error"] = "Customer information not found.";
                    return RedirectToAction("Index", "Home");
                }

                _logger.LogInformation($"Loading customer for edit: Customer/{customerIdClaim}");

                // Create HTTP request to retrieve specific customer
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{_functionBaseUrl}/customers/Customer/{customerIdClaim}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                // Handle customer not found
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = "Customer profile not found.";
                    return RedirectToAction("Index", "Home");
                }

                // Handle successful retrieval
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    // Deserialize JSON response to customer object
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
                _logger.LogError(ex, "Error loading customer for edit");
                TempData["Error"] = "Unable to load your profile for editing. Please try again.";
                return RedirectToAction("MyProfile");
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Processes profile update submission from logged-in customer.
        /// Enforces that customers can only update their own profile (ownership validation).
        /// Handles concurrency conflicts when profile has been modified elsewhere.
        /// </summary>
        /// <param name="customer">Updated customer data from form</param>
        /// <returns>Redirect to MyProfile on success, or EditMyProfile view with errors on failure</returns>
        [Authorize(Roles = "Customer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMyProfile(Customer customer)
        {
            // Extract customer ID from authentication claims
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;

            // Validate customer ID exists
            if (string.IsNullOrEmpty(customerIdClaim))
            {
                TempData["Error"] = "Customer information not found.";
                return RedirectToAction("Index", "Home");
            }

            // Security check: Ensure customer can only edit their own profile
            if (customer.RowKey != customerIdClaim)
            {
                _logger.LogWarning($"Customer {customerIdClaim} attempted to edit different customer {customer.RowKey}");
                return Forbid();
            }

            // Ensure correct partition and row keys
            customer.PartitionKey = "Customer";
            customer.RowKey = customerIdClaim;

            // Remove auto-generated fields from validation
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");

            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation($"Updating customer: Customer/{customerIdClaim}");

                    // Create HTTP request to update customer
                    var request = new HttpRequestMessage(HttpMethod.Put,
                        $"{_functionBaseUrl}/customers/Customer/{customerIdClaim}");
                    request.Headers.Add("x-functions-key", _functionKey);

                    // Serialize updated customer data to JSON
                    var jsonContent = JsonSerializer.Serialize(customer);
                    request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);

                    // Handle successful update
                    if (response.IsSuccessStatusCode)
                    {
                        TempData["Success"] = "Profile updated successfully!";
                        return RedirectToAction(nameof(MyProfile));
                    }

                    // Handle profile not found (may have been deleted)
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        TempData["Error"] = "Your profile no longer exists.";
                        return RedirectToAction("Index", "Home");
                    }

                    // Handle concurrency conflict (ETag mismatch)
                    if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        ModelState.AddModelError("", "Your profile has been modified by another process. Please refresh and try again.");
                        return View(customer);
                    }

                    throw new Exception($"Error updating customer: {response.StatusCode}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating customer: Customer/{customerIdClaim}");
                    ModelState.AddModelError("", "Unable to update your profile. Please try again.");
                }
            }

            return View(customer);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays the customer editing form for administrators.
        /// Allows admins to edit any customer profile in the system.
        /// </summary>
        /// <param name="partitionKey">Partition key of the customer</param>
        /// <param name="rowKey">Row key (unique identifier) of the customer</param>
        /// <returns>Edit view with customer data, or NotFound if customer doesn't exist</returns>
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey)
        {
            // Validate parameters
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Edit called with null or empty keys");
                return NotFound();
            }

            try
            {
                _logger.LogInformation($"Loading customer for edit: {partitionKey}/{rowKey}");

                // Create HTTP request to retrieve specific customer
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/customers/{partitionKey}/{rowKey}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                // Handle customer not found
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                    return NotFound();
                }

                // Handle successful retrieval
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    // Deserialize JSON response to customer object
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
                _logger.LogError(ex, $"Error loading customer for edit: {partitionKey}/{rowKey}");
                TempData["Error"] = "Unable to load customer for editing. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Processes customer update submission from administrator.
        /// Validates route parameters match customer data and handles concurrency conflicts.
        /// </summary>
        /// <param name="partitionKey">Partition key of the customer from route</param>
        /// <param name="rowKey">Row key (unique identifier) of the customer from route</param>
        /// <param name="customer">Updated customer data from form</param>
        /// <returns>Redirect to Index on success, or Edit view with errors on failure</returns>
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey, Customer customer)
        {
            // Validate route parameters match customer data (prevent URL tampering)
            if (partitionKey != customer.PartitionKey || rowKey != customer.RowKey)
            {
                _logger.LogWarning("Route parameters don't match customer data");
                return BadRequest("Route parameters don't match the customer data.");
            }

            // Remove auto-generated fields from validation
            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");

            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation($"Updating customer: {partitionKey}/{rowKey}");

                    // Create HTTP request to update customer
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

                    // Handle customer not found (may have been deleted)
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        TempData["Error"] = "The customer no longer exists.";
                        return RedirectToAction(nameof(Index));
                    }

                    // Handle concurrency conflict (ETag mismatch)
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

            return View(customer);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Displays the customer deletion confirmation page for administrators.
        /// Shows customer details before permanent deletion.
        /// </summary>
        /// <param name="partitionKey">Partition key of the customer</param>
        /// <param name="rowKey">Row key (unique identifier) of the customer</param>
        /// <returns>Delete confirmation view with customer data, or NotFound if customer doesn't exist</returns>
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Delete(string partitionKey, string rowKey)
        {
            // Validate parameters
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("Delete called with null or empty keys");
                return NotFound();
            }

            try
            {
                _logger.LogInformation($"Loading customer for delete: {partitionKey}/{rowKey}");

                // Create HTTP request to retrieve specific customer
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/customers/{partitionKey}/{rowKey}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                // Handle customer not found
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                    return NotFound();
                }

                // Handle successful retrieval
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    // Deserialize JSON response to customer object
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

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Processes customer deletion after administrator confirmation.
        /// Permanently removes customer record from Azure Table Storage.
        /// </summary>
        /// <param name="partitionKey">Partition key of the customer to delete</param>
        /// <param name="rowKey">Row key (unique identifier) of the customer to delete</param>
        /// <returns>Redirect to Index with success or error message</returns>
        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string partitionKey, string rowKey)
        {
            // Validate parameters
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogWarning("DeleteConfirmed called with null or empty keys");
                return NotFound();
            }

            try
            {
                _logger.LogInformation($"Deleting customer: {partitionKey}/{rowKey}");

                // Create HTTP request to delete customer
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{_functionBaseUrl}/customers/{partitionKey}/{rowKey}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                // Handle successful deletion
                if (response.IsSuccessStatusCode)
                {
                    TempData["Success"] = "Customer deleted successfully!";
                    return RedirectToAction(nameof(Index));
                }

                // Handle customer not found (may have been deleted already)
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = "Customer not found.";
                    return RedirectToAction(nameof(Index));
                }

                throw new Exception($"Error deleting customer: {response.StatusCode}");
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