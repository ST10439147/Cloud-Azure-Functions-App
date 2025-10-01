// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2 - Azure Functions API Client

using ST10439147_CLDV6212_POE.Models;
using System.Text;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Services
{
    public class OrderFunctionClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OrderFunctionClient> _logger;
        private readonly string _functionBaseUrl;

        public OrderFunctionClient(HttpClient httpClient, IConfiguration configuration, ILogger<OrderFunctionClient> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Get the Azure Function base URL from configuration
            _functionBaseUrl = configuration["AzureFunctions:OrderFunctionUrl"]
                ?? throw new InvalidOperationException("Azure Function URL is not configured.");

            _httpClient.BaseAddress = new Uri(_functionBaseUrl);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET all orders
        public async Task<List<Order>> GetAllOrdersAsync()
        {
            try
            {
                _logger.LogInformation("Calling Azure Function to get all orders");
                var response = await _httpClient.GetAsync("api/orders");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var orders = JsonSerializer.Deserialize<List<Order>>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return orders ?? new List<Order>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Azure Function to get all orders");
                throw new InvalidOperationException("Failed to retrieve orders from Azure Function", ex);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET order by ID
        public async Task<Order?> GetOrderByIdAsync(string partitionKey, string rowKey)
        {
            try
            {
                _logger.LogInformation("Calling Azure Function to get order: {PartitionKey}/{RowKey}", partitionKey, rowKey);
                var response = await _httpClient.GetAsync($"api/orders/{partitionKey}/{rowKey}");

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var order = JsonSerializer.Deserialize<Order>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return order;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Azure Function to get order: {PartitionKey}/{RowKey}", partitionKey, rowKey);
                throw new InvalidOperationException("Failed to retrieve order from Azure Function", ex);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST create new order
        public async Task<Order> CreateOrderAsync(Order order)
        {
            try
            {
                _logger.LogInformation("Calling Azure Function to create order");

                var json = JsonSerializer.Serialize(order, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("api/orders", content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<CreateOrderResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return result?.Order ?? order;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Azure Function to create order");
                throw new InvalidOperationException("Failed to create order via Azure Function", ex);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // PUT update order
        public async Task UpdateOrderAsync(string partitionKey, string rowKey, Order order)
        {
            try
            {
                _logger.LogInformation("Calling Azure Function to update order: {PartitionKey}/{RowKey}", partitionKey, rowKey);

                var json = JsonSerializer.Serialize(order, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync($"api/orders/{partitionKey}/{rowKey}", content);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Azure Function to update order: {PartitionKey}/{RowKey}", partitionKey, rowKey);
                throw new InvalidOperationException("Failed to update order via Azure Function", ex);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // DELETE order
        public async Task DeleteOrderAsync(string partitionKey, string rowKey)
        {
            try
            {
                _logger.LogInformation("Calling Azure Function to delete order: {PartitionKey}/{RowKey}", partitionKey, rowKey);

                var response = await _httpClient.DeleteAsync($"api/orders/{partitionKey}/{rowKey}");
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Azure Function to delete order: {PartitionKey}/{RowKey}", partitionKey, rowKey);
                throw new InvalidOperationException("Failed to delete order via Azure Function", ex);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET queue status
        public async Task<QueueStatusResponse> GetQueueStatusAsync()
        {
            try
            {
                _logger.LogInformation("Calling Azure Function to get queue status");
                var response = await _httpClient.GetAsync("api/orders/queue/status");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var status = JsonSerializer.Deserialize<QueueStatusResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return status ?? new QueueStatusResponse();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Azure Function to get queue status");
                throw new InvalidOperationException("Failed to retrieve queue status from Azure Function", ex);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // GET queue messages
        public async Task<QueueMessagesResponse> GetQueueMessagesAsync(string queueName)
        {
            try
            {
                _logger.LogInformation("Calling Azure Function to get messages from queue: {QueueName}", queueName);
                var response = await _httpClient.GetAsync($"api/orders/queue/{queueName}/messages");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var messages = JsonSerializer.Deserialize<QueueMessagesResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return messages ?? new QueueMessagesResponse { Messages = new List<string>() };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Azure Function to get messages from queue: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to retrieve messages from queue '{queueName}' via Azure Function", ex);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // DELETE (process) queue message
        public async Task ProcessQueueMessageAsync(string queueName, string messageId, string popReceipt)
        {
            try
            {
                _logger.LogInformation("Calling Azure Function to process message from queue: {QueueName}", queueName);

                var request = new MessageDeleteRequest
                {
                    MessageId = messageId,
                    PopReceipt = popReceipt
                };

                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var httpRequest = new HttpRequestMessage(HttpMethod.Delete, $"api/orders/queue/{queueName}/messages")
                {
                    Content = content
                };

                var response = await _httpClient.SendAsync(httpRequest);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Azure Function to process message from queue: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to process message from queue '{queueName}' via Azure Function", ex);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // POST clear queue
        public async Task ClearQueueAsync(string queueName)
        {
            try
            {
                _logger.LogInformation("Calling Azure Function to clear queue: {QueueName}", queueName);

                var response = await _httpClient.PostAsync($"api/orders/queue/{queueName}/clear", null);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Azure Function to clear queue: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to clear queue '{queueName}' via Azure Function", ex);
            }
        }
    }

    //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
    // Response models for API calls
    public class CreateOrderResponse
    {
        public string Message { get; set; } = string.Empty;
        public Order? Order { get; set; }
    }

    public class QueueStatusResponse
    {
        public int OrderQueueLength { get; set; }
        public int InventoryQueueLength { get; set; }
    }

    public class QueueMessagesResponse
    {
        public string QueueName { get; set; } = string.Empty;
        public int MessageCount { get; set; }
        public List<string> Messages { get; set; } = new List<string>();
    }

    public class MessageDeleteRequest
    {
        public string MessageId { get; set; } = string.Empty;
        public string PopReceipt { get; set; } = string.Empty;
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//