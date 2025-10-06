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
    /// <summary>
    /// Azure Function class that handles HTTP-triggered CRUD operations for Customer entities.
    /// This class provides RESTful API endpoints for managing customer data in Azure Table Storage.
    /// </summary>
    public class CustomerFunction
    {
        private readonly ILogger<CustomerFunction> _logger;
        private readonly TableService _tableService;

        /// <summary>
        /// Constructor that initializes the CustomerFunction with dependency injection.
        /// </summary>
        /// <param name="logger">Logger for tracking function execution and debugging</param>
        /// <param name="tableService">Service layer for interacting with Azure Table Storage</param>
        public CustomerFunction(ILogger<CustomerFunction> logger, TableService tableService)
        {
            _logger = logger;
            _tableService = tableService;
            _logger.LogInformation("CustomerFunction constructor completed");
        }

        /// <summary>
        /// HTTP POST endpoint to create a new customer record in Azure Table Storage.
        /// Route: POST /api/customers
        /// </summary>
        /// <param name="req">HTTP request containing customer data in JSON format</param>
        /// <returns>HTTP response with the created customer object or error message</returns>
        [Function("CreateCustomer")]
        public async Task<HttpResponseData> CreateCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "customers")] HttpRequestData req)
        {
            _logger.LogInformation("=== CreateCustomer function triggered ===");

            try
            {
                // Log the state of the injected TableService for debugging purposes
                _logger.LogInformation($"TableService is null: {_tableService == null}");

                // Read and log the request body containing customer data
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation($"Request body received: {requestBody}");

                // Deserialize JSON request body into Customer object
                var customer = JsonConvert.DeserializeObject<Customer>(requestBody);

                // Validate that deserialization was successful
                if (customer == null)
                {
                    _logger.LogWarning("Failed to deserialize customer");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteStringAsync("Invalid customer data");
                    return badResponse;
                }

                // Validate that all required fields are present
                if (string.IsNullOrEmpty(customer.FirstName) ||
                    string.IsNullOrEmpty(customer.LastName) ||
                    string.IsNullOrEmpty(customer.Email))
                {
                    _logger.LogWarning("Missing required fields");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteStringAsync("FirstName, LastName, and Email are required");
                    return badResponse;
                }

                // Set PartitionKey to default value if not provided (groups related entities)
                if (string.IsNullOrEmpty(customer.PartitionKey))
                {
                    customer.PartitionKey = "Customer";
                }

                // Generate unique RowKey if not provided (unique identifier within partition)
                if (string.IsNullOrEmpty(customer.RowKey))
                {
                    customer.RowKey = Guid.NewGuid().ToString();
                }

                _logger.LogInformation($"Attempting to insert customer with RowKey: {customer.RowKey}");

                // Insert the customer into Azure Table Storage via the service layer
                await _tableService.InsertCustomerAsync(customer);

                _logger.LogInformation($"Customer created successfully: {customer.RowKey}");

                // Return successful response with created customer data
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(customer);
                return response;
            }
            catch (Exception ex)
            {
                // Log comprehensive error details for debugging
                _logger.LogError($"=== FULL ERROR DETAILS ===");
                _logger.LogError($"Message: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");
                _logger.LogError($"Inner Exception: {ex.InnerException?.Message}");
                _logger.LogError($"Inner Stack Trace: {ex.InnerException?.StackTrace}");

                // Return error response to client
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
        }

        /// <summary>
        /// HTTP GET endpoint to retrieve all customer records from Azure Table Storage.
        /// Route: GET /api/customers
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <returns>HTTP response with list of all customers or error message</returns>
        [Function("GetAllCustomers")]
        public async Task<HttpResponseData> GetAllCustomers(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers")] HttpRequestData req)
        {
            _logger.LogInformation("=== GetAllCustomers function triggered ===");

            try
            {
                // Log the state of the injected TableService for debugging
                _logger.LogInformation($"TableService is null: {_tableService == null}");

                // Retrieve all customers from Azure Table Storage
                var customers = await _tableService.GetAllCustomersAsync();

                _logger.LogInformation($"Retrieved {customers.Count} customers");

                // Return successful response with customer list
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(customers);
                return response;
            }
            catch (Exception ex)
            {
                // Log error details for troubleshooting
                _logger.LogError($"=== FULL ERROR DETAILS ===");
                _logger.LogError($"Message: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");
                _logger.LogError($"Inner Exception: {ex.InnerException?.Message}");

                // Return error response to client
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
        }

        /// <summary>
        /// HTTP GET endpoint to search for customers by name (first or last name).
        /// Route: GET /api/customers/search?name={searchTerm}
        /// </summary>
        /// <param name="req">HTTP request containing search query parameter</param>
        /// <returns>HTTP response with filtered list of customers or error message</returns>
        [Function("SearchCustomers")]
        public async Task<HttpResponseData> SearchCustomers(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers/search")] HttpRequestData req)
        {
            _logger.LogInformation("=== SearchCustomers function triggered ===");

            try
            {
                // Parse query string to extract the search term parameter
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var searchTerm = query["name"];

                _logger.LogInformation($"Searching for customers with name: {searchTerm}");

                // Search for customers matching the provided name
                var customers = await _tableService.SearchCustomersByNameAsync(searchTerm);

                _logger.LogInformation($"Found {customers.Count} customers matching search term");

                // Return successful response with filtered customer list
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(customers);
                return response;
            }
            catch (Exception ex)
            {
                // Log error details for troubleshooting
                _logger.LogError($"=== FULL ERROR DETAILS ===");
                _logger.LogError($"Message: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");
                _logger.LogError($"Inner Exception: {ex.InnerException?.Message}");

                // Return error response to client
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
        }

        /// <summary>
        /// HTTP GET endpoint to retrieve a specific customer by their unique identifiers.
        /// Route: GET /api/customers/{partitionKey}/{rowKey}
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <param name="partitionKey">The partition key of the customer (e.g., "Customer")</param>
        /// <param name="rowKey">The unique row key (ID) of the customer</param>
        /// <returns>HTTP response with customer data, 404 if not found, or error message</returns>
        [Function("GetCustomerById")]
        public async Task<HttpResponseData> GetCustomerById(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"=== GetCustomerById triggered: {partitionKey}/{rowKey} ===");

            try
            {
                // Retrieve specific customer by their partition and row keys
                var customer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);

                _logger.LogInformation($"Customer retrieved successfully: {rowKey}");

                // Return successful response with customer data
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(customer);
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("retrieve customer"))
            {
                // Handle case where customer doesn't exist - return 404 Not Found
                _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Customer not found");
                return notFoundResponse;
            }
            catch (Exception ex)
            {
                // Log and return general errors
                _logger.LogError($"Error retrieving customer: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
        }

        /// <summary>
        /// HTTP PUT endpoint to update an existing customer's information.
        /// Route: PUT /api/customers/{partitionKey}/{rowKey}
        /// </summary>
        /// <param name="req">HTTP request containing updated customer data in JSON format</param>
        /// <param name="partitionKey">The partition key of the customer to update</param>
        /// <param name="rowKey">The unique row key (ID) of the customer to update</param>
        /// <returns>HTTP response with updated customer, 404 if not found, 409 if concurrency conflict, or error message</returns>
        [Function("UpdateCustomer")]
        public async Task<HttpResponseData> UpdateCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "customers/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"=== UpdateCustomer triggered: {partitionKey}/{rowKey} ===");

            try
            {
                // Read and deserialize the updated customer data from request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var updatedCustomer = JsonConvert.DeserializeObject<Customer>(requestBody);

                // Validate deserialization was successful
                if (updatedCustomer == null)
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteStringAsync("Invalid customer data");
                    return badResponse;
                }

                // Ensure route parameters override any keys in the request body
                updatedCustomer.PartitionKey = partitionKey;
                updatedCustomer.RowKey = rowKey;

                // Retrieve existing customer to maintain ETag for optimistic concurrency control
                var existingCustomer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);

                // Update only the modifiable fields, preserving keys and ETags
                existingCustomer.FirstName = updatedCustomer.FirstName;
                existingCustomer.LastName = updatedCustomer.LastName;
                existingCustomer.Email = updatedCustomer.Email;
                existingCustomer.PhoneNumber = updatedCustomer.PhoneNumber;

                // Persist the updated customer to Azure Table Storage
                await _tableService.UpdateCustomerAsync(existingCustomer);

                _logger.LogInformation($"Customer updated successfully: {rowKey}");

                // Return successful response with updated customer data
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(existingCustomer);
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("no longer exists"))
            {
                // Handle case where customer was deleted before update - return 404
                _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Customer not found");
                return notFoundResponse;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("modified by another user"))
            {
                // Handle optimistic concurrency conflict - return 409
                _logger.LogWarning($"Concurrency conflict for customer: {partitionKey}/{rowKey}");
                var conflictResponse = req.CreateResponse(HttpStatusCode.Conflict);
                await conflictResponse.WriteStringAsync("Customer has been modified by another user");
                return conflictResponse;
            }
            catch (Exception ex)
            {
                // Log and return general errors
                _logger.LogError($"Error updating customer: {ex.Message}");
                _logger.LogError($"Stack Trace: {ex.StackTrace}");

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
        }

        /// <summary>
        /// HTTP DELETE endpoint to remove a customer from Azure Table Storage.
        /// Route: DELETE /api/customers/{partitionKey}/{rowKey}
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <param name="partitionKey">The partition key of the customer to delete</param>
        /// <param name="rowKey">The unique row key (ID) of the customer to delete</param>
        /// <returns>HTTP response with success message, 404 if not found, or error message</returns>
        [Function("DeleteCustomer")]
        public async Task<HttpResponseData> DeleteCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "customers/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"=== DeleteCustomer triggered: {partitionKey}/{rowKey} ===");

            try
            {
                // Delete the customer from Azure Table Storage
                await _tableService.DeleteCustomerAsync(partitionKey, rowKey);

                _logger.LogInformation($"Customer deleted successfully: {rowKey}");

                // Return successful response with confirmation message
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { message = "Customer deleted successfully" });
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("delete customer"))
            {
                // Handle case where customer doesn't exist - return 404
                _logger.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Customer not found");
                return notFoundResponse;
            }
            catch (Exception ex)
            {
                // Log and return general errors
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