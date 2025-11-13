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
    /// Controller responsible for managing customer-related operations.
    /// Communicates with Azure Functions to perform CRUD operations on customer data.
    /// </summary>
    public class CustomerController : Controller
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CustomerController> _logger;
        private readonly string _functionBaseUrl;
        private readonly string _functionKey;

        public CustomerController(IHttpClientFactory httpClientFactory, ILogger<CustomerController> logger, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
            _functionBaseUrl = configuration["AzureFunctions:BaseUrl"];
            _functionKey = configuration["AzureFunctions:FunctionKey"];
        }

        /// <summary>
        /// GET: Customer/Index - View all customers (Admin only)
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(string searchName)
        {
            try
            {
                _logger.LogInformation($"Loading customers from Azure Function. Search term: {searchName}");

                HttpRequestMessage request;

                if (!string.IsNullOrWhiteSpace(searchName))
                {
                    request = new HttpRequestMessage(HttpMethod.Get,
                        $"{_functionBaseUrl}/customers/search?name={Uri.EscapeDataString(searchName)}");
                }
                else
                {
                    request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/customers");
                }

                request.Headers.Add("x-functions-key", _functionKey);
                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var customers = JsonSerializer.Deserialize<List<Customer>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    ViewBag.SearchName = searchName;
                    ViewBag.ResultCount = customers?.Count ?? 0;

                    return View(customers);
                }

                _logger.LogError($"Error loading customers: {response.StatusCode}");
                ViewBag.Error = "Unable to load customers. Please try again.";
                return View(new List<Customer>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading customers");
                ViewBag.Error = "Unable to load customers. Please try again.";
                return View(new List<Customer>());
            }
        }

        /// <summary>
        /// GET: Customer/Create - Customers can create their own profile
        /// </summary>

        [HttpGet]
        public IActionResult Create()
        {
            var customer = new Customer();
            return View(customer);
        }

        /// <summary>
        /// POST: Customer/Create - Customers can create their own profile
        /// </summary>
        [Authorize(Roles = "Customer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Customer customer)
        {
            if (string.IsNullOrEmpty(customer.RowKey))
            {
                customer.RowKey = Guid.NewGuid().ToString();
            }

            if (string.IsNullOrEmpty(customer.PartitionKey))
            {
                customer.PartitionKey = "Customer";
            }

            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");

            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation($"Creating customer: {customer.Email}");

                    var request = new HttpRequestMessage(HttpMethod.Post, $"{_functionBaseUrl}/customers");
                    request.Headers.Add("x-functions-key", _functionKey);

                    var jsonContent = JsonSerializer.Serialize(customer);
                    request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        TempData["Success"] = "Customer added successfully!";
                        return RedirectToAction(nameof(Index));
                    }

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

        /// <summary>
        /// Internal method to create customer via Azure Function (called by AuthenticationService)
        /// This bypasses authorization for the registration process
        /// </summary>
        public async Task<(bool success, string customerId, string message)> CreateCustomerInternalAsync(Customer customer)
        {
            try
            {
                if (string.IsNullOrEmpty(customer.RowKey))
                {
                    customer.RowKey = Guid.NewGuid().ToString();
                }

                if (string.IsNullOrEmpty(customer.PartitionKey))
                {
                    customer.PartitionKey = "Customer";
                }

                _logger.LogInformation($"Creating customer via Azure Function: {customer.Email}");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_functionBaseUrl}/customers");
                request.Headers.Add("x-functions-key", _functionKey);

                var jsonContent = JsonSerializer.Serialize(customer);
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var createdCustomer = JsonSerializer.Deserialize<Customer>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    _logger.LogInformation($"Customer created successfully: {createdCustomer?.RowKey}");
                    return (true, createdCustomer?.RowKey ?? customer.RowKey, "Customer created successfully");
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Error creating customer: {response.StatusCode} - {errorContent}");
                return (false, string.Empty, $"Failed to create customer: {errorContent}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating customer via Azure Function");
                return (false, string.Empty, $"Error creating customer: {ex.Message}");
            }
        }

        /// <summary>
        /// GET: Customer/MyProfile - View logged-in customer's own profile
        /// </summary>
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<IActionResult> MyProfile()
        {
            try
            {
                var customerIdClaim = User.FindFirst("CustomerId")?.Value;

                if (string.IsNullOrEmpty(customerIdClaim))
                {
                    TempData["Error"] = "Customer information not found.";
                    return RedirectToAction("Index", "Home");
                }

                _logger.LogInformation($"Loading customer profile: Customer/{customerIdClaim}");

                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{_functionBaseUrl}/customers/Customer/{customerIdClaim}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = "Customer profile not found.";
                    return RedirectToAction("Index", "Home");
                }

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
                _logger.LogError(ex, "Error loading customer profile");
                TempData["Error"] = "Unable to load your profile. Please try again.";
                return RedirectToAction("Index", "Home");
            }
        }

        /// <summary>
        /// GET: Customer/EditMyProfile - Edit logged-in customer's own profile
        /// </summary>
        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<IActionResult> EditMyProfile()
        {
            try
            {
                var customerIdClaim = User.FindFirst("CustomerId")?.Value;

                if (string.IsNullOrEmpty(customerIdClaim))
                {
                    TempData["Error"] = "Customer information not found.";
                    return RedirectToAction("Index", "Home");
                }

                _logger.LogInformation($"Loading customer for edit: Customer/{customerIdClaim}");

                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{_functionBaseUrl}/customers/Customer/{customerIdClaim}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Error"] = "Customer profile not found.";
                    return RedirectToAction("Index", "Home");
                }

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
                _logger.LogError(ex, "Error loading customer for edit");
                TempData["Error"] = "Unable to load your profile for editing. Please try again.";
                return RedirectToAction("MyProfile");
            }
        }

        /// <summary>
        /// POST: Customer/EditMyProfile - Update logged-in customer's own profile
        /// </summary>
        [Authorize(Roles = "Customer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMyProfile(Customer customer)
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;

            if (string.IsNullOrEmpty(customerIdClaim))
            {
                TempData["Error"] = "Customer information not found.";
                return RedirectToAction("Index", "Home");
            }

            // Ensure customer can only edit their own profile
            if (customer.RowKey != customerIdClaim)
            {
                _logger.LogWarning($"Customer {customerIdClaim} attempted to edit different customer {customer.RowKey}");
                return Forbid();
            }

            customer.PartitionKey = "Customer";
            customer.RowKey = customerIdClaim;

            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");

            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation($"Updating customer: Customer/{customerIdClaim}");

                    var request = new HttpRequestMessage(HttpMethod.Put,
                        $"{_functionBaseUrl}/customers/Customer/{customerIdClaim}");
                    request.Headers.Add("x-functions-key", _functionKey);

                    var jsonContent = JsonSerializer.Serialize(customer);
                    request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        TempData["Success"] = "Profile updated successfully!";
                        return RedirectToAction(nameof(MyProfile));
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        TempData["Error"] = "Your profile no longer exists.";
                        return RedirectToAction("Index", "Home");
                    }

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

        /// <summary>
        /// GET: Customer/Edit - Admin edits any customer
        /// </summary>
        [Authorize(Roles = "Admin")]
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

                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/customers/{partitionKey}/{rowKey}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                    return NotFound();
                }

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
                _logger.LogError(ex, $"Error loading customer for edit: {partitionKey}/{rowKey}");
                TempData["Error"] = "Unable to load customer for editing. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// POST: Customer/Edit - Admin updates any customer
        /// </summary>
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey, Customer customer)
        {
            if (partitionKey != customer.PartitionKey || rowKey != customer.RowKey)
            {
                _logger.LogWarning("Route parameters don't match customer data");
                return BadRequest("Route parameters don't match the customer data.");
            }

            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");

            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation($"Updating customer: {partitionKey}/{rowKey}");

                    var request = new HttpRequestMessage(HttpMethod.Put, $"{_functionBaseUrl}/customers/{partitionKey}/{rowKey}");
                    request.Headers.Add("x-functions-key", _functionKey);

                    var jsonContent = JsonSerializer.Serialize(customer);
                    request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        TempData["Success"] = "Customer updated successfully!";
                        return RedirectToAction(nameof(Index));
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        TempData["Error"] = "The customer no longer exists.";
                        return RedirectToAction(nameof(Index));
                    }

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

        /// <summary>
        /// GET: Customer/Delete - Admin deletes customer
        /// </summary>
        [Authorize(Roles = "Admin")]
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

                var request = new HttpRequestMessage(HttpMethod.Get, $"{_functionBaseUrl}/customers/{partitionKey}/{rowKey}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                    return NotFound();
                }

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
        /// POST: Customer/Delete - Admin confirms deletion
        /// </summary>
        [Authorize(Roles = "Admin")]
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

                var request = new HttpRequestMessage(HttpMethod.Delete, $"{_functionBaseUrl}/customers/{partitionKey}/{rowKey}");
                request.Headers.Add("x-functions-key", _functionKey);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    TempData["Success"] = "Customer deleted successfully!";
                    return RedirectToAction(nameof(Index));
                }

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