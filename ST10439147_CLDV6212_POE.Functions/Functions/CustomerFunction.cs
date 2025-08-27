using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ST10439147_CLDV6212_POE.Services;
using ST10439147_CLDV6212_POE.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ST10439147_CLDV6212_POE.Functions.Functions
{
    public class CustomerFunction
    {
        private readonly TableService _tableService;
        private readonly ILogger<CustomerFunction> _logger; 

        public CustomerFunction(TableService tableService, ILogger<CustomerFunction> logger)
        {
            _tableService = tableService;
            _logger = logger;
        }

        [Function("GetAllCustomers")]
        public async Task<IActionResult> GetAllCustomers(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("GetAllCustomers function triggered");
                var customers = await _tableService.GetAllCustomersAsync();
                return new OkObjectResult(customers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving customers");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [Function("GetCustomerById")]
        public async Task<IActionResult> GetCustomerById(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers/{partitionKey}/{rowKey}")]
            HttpRequest req,
            string partitionKey,
            string rowKey)
        {
            try
            {
                var customer = await _tableService.GetCustomerByIdAsync(partitionKey, rowKey);
                if (customer == null)
                    return new NotFoundResult();

                return new OkObjectResult(customer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving customer");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [Function("CreateCustomer")]
        public async Task<IActionResult> CreateCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "customers")]
            HttpRequest req)
        {
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var customer = JsonConvert.DeserializeObject<ST10439147_CLDV6212_POE.Models.Customer>(requestBody);

                if (customer == null)
                    return new BadRequestObjectResult("Invalid customer data");

                await _tableService.InsertCustomerAsync(customer); 
                return new CreatedResult($"/api/customers/{customer.PartitionKey}/{customer.RowKey}", customer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating customer");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [Function("UpdateCustomer")]
        public async Task<IActionResult> UpdateCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "customers/{partitionKey}/{rowKey}")]
            HttpRequest req,
            string partitionKey,
            string rowKey)
        {
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var customer = JsonConvert.DeserializeObject<ST10439147_CLDV6212_POE.Models.Customer>(requestBody);

                if (customer == null || customer.PartitionKey != partitionKey || customer.RowKey != rowKey)
                    return new BadRequestObjectResult("Invalid customer data");

                await _tableService.UpdateCustomerAsync(customer);
                return new OkObjectResult(customer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating customer");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [Function("DeleteCustomer")]
        public async Task<IActionResult> DeleteCustomer(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "customers/{partitionKey}/{rowKey}")]
            HttpRequest req,
            string partitionKey,
            string rowKey)
        {
            try
            {
                await _tableService.DeleteCustomerAsync(partitionKey, rowKey);
                return new NoContentResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting customer");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}