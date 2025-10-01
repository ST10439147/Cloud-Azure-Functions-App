using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Data.Tables;
using Azure;
using System.Collections.Generic;

namespace ST10439147_CLDV6212_POE.Functions.Functions
{
    public static class CustomerFunction
    {
        private const string TableName = "customers";
        private const string ConnectionStringName = "AzureWebJobsStorage";

        // POST: Create a new customer
        [FunctionName("CreateCustomer")]
        public static async Task<IActionResult> CreateCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "customers")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Creating new customer");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var customer = JsonConvert.DeserializeObject<Customer>(requestBody);

                if (customer == null)
                {
                    return new BadRequestObjectResult("Invalid customer data");
        }

                // Validate required fields
                if (string.IsNullOrEmpty(customer.FirstName) ||
                    string.IsNullOrEmpty(customer.LastName) ||
                    string.IsNullOrEmpty(customer.Email))
        {
                    return new BadRequestObjectResult("FirstName, LastName, and Email are required");
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

                var tableClient = new TableClient(
                    Environment.GetEnvironmentVariable(ConnectionStringName),
                    TableName);

                await tableClient.CreateIfNotExistsAsync();
                await tableClient.AddEntityAsync(customer);

                log.LogInformation($"Customer created successfully: {customer.RowKey}");
                return new OkObjectResult(customer);
            }
            catch (Exception ex)
            {
                log.LogError($"Error creating customer: {ex.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // GET: Retrieve all customers
        [FunctionName("GetAllCustomers")]
        public static async Task<IActionResult> GetAllCustomers(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Retrieving all customers");

            try
            {
                var tableClient = new TableClient(
                    Environment.GetEnvironmentVariable(ConnectionStringName),
                    TableName);

                var customers = new List<Customer>();
                await foreach (Customer customer in tableClient.QueryAsync<Customer>())
                {
                    customers.Add(customer);
            }

                log.LogInformation($"Retrieved {customers.Count} customers");
                return new OkObjectResult(customers);
            }
            catch (Exception ex)
            {
                log.LogError($"Error retrieving customers: {ex.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // GET: Retrieve a customer by ID
        [FunctionName("GetCustomerById")]
        public static async Task<IActionResult> GetCustomerById(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers/{partitionKey}/{rowKey}")] HttpRequest req,
            string partitionKey,
            string rowKey,
            ILogger log)
        {
            log.LogInformation($"Retrieving customer: {partitionKey}/{rowKey}");

            try
            {
                var tableClient = new TableClient(
                    Environment.GetEnvironmentVariable(ConnectionStringName),
                    TableName);

                var response = await tableClient.GetEntityAsync<Customer>(partitionKey, rowKey);

                log.LogInformation($"Customer retrieved successfully: {rowKey}");
                return new OkObjectResult(response.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                log.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                return new NotFoundObjectResult($"Customer not found");
            }
            catch (Exception ex)
            {
                log.LogError($"Error retrieving customer: {ex.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // PUT: Update an existing customer
        [FunctionName("UpdateCustomer")]
        public static async Task<IActionResult> UpdateCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "customers/{partitionKey}/{rowKey}")] HttpRequest req,
            string partitionKey,
            string rowKey,
            ILogger log)
        {
            log.LogInformation($"Updating customer: {partitionKey}/{rowKey}");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var updatedCustomer = JsonConvert.DeserializeObject<Customer>(requestBody);

                if (updatedCustomer == null)
                {
                    return new BadRequestObjectResult("Invalid customer data");
                }

                // Ensure the route parameters match the customer data
                updatedCustomer.PartitionKey = partitionKey;
                updatedCustomer.RowKey = rowKey;

                var tableClient = new TableClient(
                    Environment.GetEnvironmentVariable(ConnectionStringName),
                    TableName);

                // Get existing customer to maintain ETag
                var existingResponse = await tableClient.GetEntityAsync<Customer>(partitionKey, rowKey);
                var existingCustomer = existingResponse.Value;

                // Update fields
                existingCustomer.FirstName = updatedCustomer.FirstName;
                existingCustomer.LastName = updatedCustomer.LastName;
                existingCustomer.Email = updatedCustomer.Email;
                existingCustomer.PhoneNumber = updatedCustomer.PhoneNumber;

                await tableClient.UpdateEntityAsync(existingCustomer, existingCustomer.ETag, TableUpdateMode.Replace);

                log.LogInformation($"Customer updated successfully: {rowKey}");
                return new OkObjectResult(existingCustomer);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                log.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                return new NotFoundObjectResult("Customer not found");
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                log.LogWarning($"Concurrency conflict for customer: {partitionKey}/{rowKey}");
                return new ConflictObjectResult("Customer has been modified by another user");
            }
            catch (Exception ex)
            {
                log.LogError($"Error updating customer: {ex.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // DELETE: Delete a customer
        [FunctionName("DeleteCustomer")]
        public static async Task<IActionResult> DeleteCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "customers/{partitionKey}/{rowKey}")] HttpRequest req,
            string partitionKey,
            string rowKey,
            ILogger log)
        {
            log.LogInformation($"Deleting customer: {partitionKey}/{rowKey}");

            try
            {
                var tableClient = new TableClient(
                    Environment.GetEnvironmentVariable(ConnectionStringName),
                    TableName);

                await tableClient.DeleteEntityAsync(partitionKey, rowKey);

                log.LogInformation($"Customer deleted successfully: {rowKey}");
                return new OkObjectResult(new { message = "Customer deleted successfully" });
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                log.LogWarning($"Customer not found: {partitionKey}/{rowKey}");
                return new NotFoundObjectResult("Customer not found");
            }
            catch (Exception ex)
            {
                log.LogError($"Error deleting customer: {ex.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }

    // Customer model class for Azure Functions
    public class Customer : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//