// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2

using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ST10439147_CLDV6212_POE.Services;
using ST10439147_CLDV6212_POE.Models;
using Azure;

namespace ST10439147_CLDV6212_POE.Functions
{
    public class CustomerFunction
    {
        private readonly ILogger<CustomerFunction> _logger;
        private readonly TableService _tableService;

        public CustomerFunction(ILogger<CustomerFunction> logger, TableService tableService)
        {
            _logger = logger;
            _tableService = tableService;
            _logger.LogInformation("CustomerFunction constructor completed");
        }

        // POST: Create a new customer
        [Function("CreateCustomer")]
        public async Task<HttpResponseData> CreateCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "customers")] HttpRequestData req)
        {
            _logger.LogInformation("=== CreateCustomer function triggered ===");

            try
            {
                _logger.LogInformation($"TableService is null: {_tableService == null}");

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation($"Request body received: {requestBody}");

                var customer = JsonConvert.DeserializeObject<Customer>(requestBody);

                if (customer == null)
                {
                    _logger.LogWarning("Failed to deserialize customer");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteStringAsync("Invalid customer data");
                    return badResponse;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(customer.FirstName) ||
                    string.IsNullOrEmpty(customer.LastName) ||
                    string.IsNullOrEmpty(customer.Email))
                {
                    _logger.LogWarning("Missing required fields");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteStringAsync("FirstName, LastName, and Email are required");
                    return badResponse;
                }

                // Set PartitionKey and RowKey if not provided
                if (string.IsNullOrEmpty(customer.PartitionKey))
                {
                    customer.PartitionKey = "Customer";
                }
                if (string.IsNullOrEmpty(customer.RowKey))
                {
                    customer.RowKey = Guid.NewGuid().ToString();
                }

                _logger.LogInformation($"Attempting to insert customer with RowKey: {customer.RowKey}");

                await _tableService.InsertCustomerAsync(customer);

                _logger.LogInformation($"Customer created successfully: {customer.RowKey}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(customer);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"=== FULL ERROR DETAILS ===");
                _logger.LogError($"Message: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");
                _logger.LogError($"Inner Exception: {ex.InnerException?.Message}");
                _logger.LogError($"Inner Stack Trace: {ex.InnerException?.StackTrace}");

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
        }

        // GET: Retrieve all customers
        [Function("GetAllCustomers")]
        public async Task<HttpResponseData> GetAllCustomers(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers")] HttpRequestData req)
        {
            _logger.LogInformation("=== GetAllCustomers function triggered ===");

            try
            {
                _logger.LogInformation($"TableService is null: {_tableService == null}");

                var customers = await _tableService.GetAllCustomersAsync();

                _logger.LogInformation($"Retrieved {customers.Count} customers");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(customers);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"=== FULL ERROR DETAILS ===");
                _logger.LogError($"Message: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");
                _logger.LogError($"Inner Exception: {ex.InnerException?.Message}");

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
        }

        // GET: Search customers by name
        [Function("SearchCustomers")]
        public async Task<HttpResponseData> SearchCustomers(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers/search")] HttpRequestData req)
        {
            _logger.LogInformation("=== SearchCustomers function triggered ===");

            try
            {
                // Get search term from query string
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var searchTerm = query["name"];

                _logger.LogInformation($"Searching for customers with name: {searchTerm}");

                var customers = await _tableService.SearchCustomersByNameAsync(searchTerm);

                _logger.LogInformation($"Found {customers.Count} customers matching search term");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(customers);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"=== FULL ERROR DETAILS ===");
                _logger.LogError($"Message: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");
                _logger.LogError($"Inner Exception: {ex.InnerException?.Message}");

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
        }

        // GET: Retrieve a customer by ID
        [Function("GetCustomerById")]
        public async Task<HttpResponseData> GetCustomerById(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"=== GetCustomerById triggered: {partitionKey}/{rowKey} ===");

            try
            {
                var customer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);

                _logger.LogInformation($"Customer retrieved successfully: {rowKey}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(customer);
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("retrieve customer"))
            {
                _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Customer not found");
                return notFoundResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving customer: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
        }

        // PUT: Update an existing customer
        [Function("UpdateCustomer")]
        public async Task<HttpResponseData> UpdateCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "customers/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"=== UpdateCustomer triggered: {partitionKey}/{rowKey} ===");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var updatedCustomer = JsonConvert.DeserializeObject<Customer>(requestBody);

                if (updatedCustomer == null)
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteStringAsync("Invalid customer data");
                    return badResponse;
                }

                // Ensure the route parameters match the customer data
                updatedCustomer.PartitionKey = partitionKey;
                updatedCustomer.RowKey = rowKey;

                // Get existing customer to maintain ETag
                var existingCustomer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);

                // Update fields
                existingCustomer.FirstName = updatedCustomer.FirstName;
                existingCustomer.LastName = updatedCustomer.LastName;
                existingCustomer.Email = updatedCustomer.Email;
                existingCustomer.PhoneNumber = updatedCustomer.PhoneNumber;

                await _tableService.UpdateCustomerAsync(existingCustomer);

                _logger.LogInformation($"Customer updated successfully: {rowKey}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(existingCustomer);
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("no longer exists"))
            {
                _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Customer not found");
                return notFoundResponse;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("modified by another user"))
            {
                _logger.LogWarning($"Concurrency conflict for customer: {partitionKey}/{rowKey}");
                var conflictResponse = req.CreateResponse(HttpStatusCode.Conflict);
                await conflictResponse.WriteStringAsync("Customer has been modified by another user");
                return conflictResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating customer: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
        }

        // DELETE: Delete a customer
        [Function("DeleteCustomer")]
        public async Task<HttpResponseData> DeleteCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "customers/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"=== DeleteCustomer triggered: {partitionKey}/{rowKey} ===");

            try
            {
                await _tableService.DeleteCustomerAsync(partitionKey, rowKey);

                _logger.LogInformation($"Customer deleted successfully: {rowKey}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { message = "Customer deleted successfully" });
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("delete customer"))
            {
                _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Customer not found");
                return notFoundResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting customer: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//