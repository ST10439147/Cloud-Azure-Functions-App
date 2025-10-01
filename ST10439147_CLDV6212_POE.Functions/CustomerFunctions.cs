using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Data.Tables;
using Azure;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Text;

namespace ST10439147_CLDV6212_POE.Functions
{
    // Customer model for Azure Functions
    public class Customer : ITableEntity
    {
        // Default constructor
        public Customer()
        {
            RowKey = Guid.NewGuid().ToString();// Unique identifier for each customer
            PartitionKey = "Customer";// Static partition key for all customers
        }

        public string PartitionKey { get; set; } = "Customer";// Static partition key for all customers
        public string RowKey { get; set; }// Unique identifier for each customer

        [Required(ErrorMessage = "First name is required")]
        [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters")]
        public string FirstName { get; set; }

        [Required(ErrorMessage = "Last name is required")]
        [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters")]
        public string LastName { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Phone number is required")]
        [Phone(ErrorMessage = "Invalid phone number format")]
        [StringLength(15, ErrorMessage = "Phone number cannot exceed 15 characters")]
        public string PhoneNumber { get; set; }

        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
    // Azure Functions for managing customers, including CRUD operations.
    public class CustomerFunctions
    {
        private readonly ILogger<CustomerFunctions> _logger;// Logger for logging information and errors
        private readonly TableServiceClient _tableServiceClient;// Client for interacting with Azure Table Storage

        public CustomerFunctions(ILogger<CustomerFunctions> logger, TableServiceClient tableServiceClient)
        {
            _logger = logger;
            _tableServiceClient = tableServiceClient;
        }

        [Function("GetAllCustomers")]
        public async Task<HttpResponseData> GetAllCustomers(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers")] HttpRequestData req)
        {
            _logger.LogInformation("Getting all customers via Azure Function");

            try
            {
                var tableClient = _tableServiceClient.GetTableClient("customers");
                await tableClient.CreateIfNotExistsAsync();

                var customers = new List<Customer>();
                await foreach (Customer entity in tableClient.QueryAsync<Customer>())
                {
                    customers.Add(entity);
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(customers);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving customers: {ex.Message}");
                var response = req.CreateResponse(HttpStatusCode.BadRequest);
                await response.WriteStringAsync($"Error retrieving customers: {ex.Message}");
                return response;
            }
        }

        [Function("GetCustomerById")]
        public async Task<HttpResponseData> GetCustomerById(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"Getting customer: {partitionKey}/{rowKey}");

            try
            {
                var tableClient = _tableServiceClient.GetTableClient("customers");
                var response = await tableClient.GetEntityAsync<Customer>(partitionKey, rowKey);

                var httpResponse = req.CreateResponse(HttpStatusCode.OK);
                await httpResponse.WriteAsJsonAsync(response.Value);
                return httpResponse;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                var response = req.CreateResponse(HttpStatusCode.NotFound);
                await response.WriteStringAsync("Customer not found");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving customer: {ex.Message}");
                var response = req.CreateResponse(HttpStatusCode.BadRequest);
                await response.WriteStringAsync($"Error retrieving customer: {ex.Message}");
                return response;
            }
        }

        [Function("CreateCustomer")]
        public async Task<HttpResponseData> CreateCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "customers")] HttpRequestData req)
        {
            _logger.LogInformation("Creating new customer via Azure Function");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var customer = JsonConvert.DeserializeObject<Customer>(requestBody);

                // Validate the customer object
                var validationResults = new List<ValidationResult>();
                var validationContext = new ValidationContext(customer);

                if (!Validator.TryValidateObject(customer, validationContext, validationResults, true))
                {
                    var errors = validationResults.Select(r => r.ErrorMessage).ToArray();
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { Errors = errors });
                    return response;
                }

                // Ensure RowKey and PartitionKey are set
                if (string.IsNullOrEmpty(customer.RowKey))
                {
                    customer.RowKey = Guid.NewGuid().ToString();
                }
                if (string.IsNullOrEmpty(customer.PartitionKey))
                {
                    customer.PartitionKey = "Customer";
                }

                var tableClient = _tableServiceClient.GetTableClient("customers");
                await tableClient.CreateIfNotExistsAsync();
                await tableClient.AddEntityAsync(customer);

                var httpResponse = req.CreateResponse(HttpStatusCode.Created);
                await httpResponse.WriteAsJsonAsync(customer);
                return httpResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating customer: {ex.Message}");
                var response = req.CreateResponse(HttpStatusCode.BadRequest);
                await response.WriteStringAsync($"Error creating customer: {ex.Message}");
                return response;
            }
        }

        [Function("UpdateCustomer")]
        public async Task<HttpResponseData> UpdateCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "customers/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"Updating customer: {partitionKey}/{rowKey}");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var updatedCustomer = JsonConvert.DeserializeObject<Customer>(requestBody);

                // Validate the customer object
                var validationResults = new List<ValidationResult>();
                var validationContext = new ValidationContext(updatedCustomer);

                if (!Validator.TryValidateObject(updatedCustomer, validationContext, validationResults, true))
                {
                    var errors = validationResults.Select(r => r.ErrorMessage).ToArray();
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { Errors = errors });
                    return response;
                }

                var tableClient = _tableServiceClient.GetTableClient("customers");

                // Get existing customer to preserve ETag
                var existingResponse = await tableClient.GetEntityAsync<Customer>(partitionKey, rowKey);
                var existingCustomer = existingResponse.Value;

                // Update fields
                existingCustomer.FirstName = updatedCustomer.FirstName;
                existingCustomer.LastName = updatedCustomer.LastName;
                existingCustomer.Email = updatedCustomer.Email;
                existingCustomer.PhoneNumber = updatedCustomer.PhoneNumber;

                await tableClient.UpdateEntityAsync(existingCustomer, existingCustomer.ETag, TableUpdateMode.Replace);

                var httpResponse = req.CreateResponse(HttpStatusCode.OK);
                await httpResponse.WriteAsJsonAsync(existingCustomer);
                return httpResponse;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                var response = req.CreateResponse(HttpStatusCode.NotFound);
                await response.WriteStringAsync("Customer not found");
                return response;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                var response = req.CreateResponse(HttpStatusCode.Conflict);
                await response.WriteStringAsync("Customer has been modified by another user. Please refresh and try again.");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating customer: {ex.Message}");
                var response = req.CreateResponse(HttpStatusCode.BadRequest);
                await response.WriteStringAsync($"Error updating customer: {ex.Message}");
                return response;
            }
        }

        [Function("DeleteCustomer")]
        public async Task<HttpResponseData> DeleteCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "customers/{partitionKey}/{rowKey}")] HttpRequestData req,
            string partitionKey,
            string rowKey)
        {
            _logger.LogInformation($"Deleting customer: {partitionKey}/{rowKey}");

            try
            {
                var tableClient = _tableServiceClient.GetTableClient("customers");
                await tableClient.DeleteEntityAsync(partitionKey, rowKey);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteStringAsync("Customer deleted successfully");
                return response;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                var response = req.CreateResponse(HttpStatusCode.NotFound);
                await response.WriteStringAsync("Customer not found");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting customer: {ex.Message}");
                var response = req.CreateResponse(HttpStatusCode.BadRequest);
                await response.WriteStringAsync($"Error deleting customer: {ex.Message}");
                return response;
            }
        }
    }
}