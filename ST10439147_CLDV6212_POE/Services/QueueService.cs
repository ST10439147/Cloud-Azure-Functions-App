using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using ST10439147_CLDV6212_POE.Models;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Services
{
    public class QueueService
    {
        private readonly string _connectionString;
        private readonly QueueServiceClient _queueServiceClient;

        public QueueService(IConfiguration configuration)
        {
            _connectionString = configuration["AzureStorage:ConnectionString"];
            _queueServiceClient = new QueueServiceClient(_connectionString);
        }

        public async Task SendOrderMessageAsync(OrderMessage orderMessage)
        {
            var queueClient = _queueServiceClient.GetQueueClient("ordermsg");
            await queueClient.CreateIfNotExistsAsync();

            var messageJson = JsonSerializer.Serialize(orderMessage);
            await queueClient.SendMessageAsync(messageJson);
        }

        public async Task SendInventoryMessageAsync(string message)
        {
            var queueClient = _queueServiceClient.GetQueueClient("inventory-msg");
            await queueClient.CreateIfNotExistsAsync();

            await queueClient.SendMessageAsync(message);
        }

        public async Task SendMessageAsync(string queueName, string message)
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync();

            await queueClient.SendMessageAsync(message);
        }

        public async Task<List<string>> GetQueueMessagesAsync(string queueName, int maxMessages = 10)
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync();

            var messages = new List<string>();
            var receivedMessages = await queueClient.ReceiveMessagesAsync(maxMessages);

            foreach (var message in receivedMessages.Value)
            {
                messages.Add(message.MessageText);
            }

            return messages;
        }

        public async Task DeleteMessageAsync(string queueName, string messageId, string popReceipt)
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.DeleteMessageAsync(messageId, popReceipt);
        }

        public async Task<int> GetQueueLengthAsync(string queueName)
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            var properties = await queueClient.GetPropertiesAsync();
            return properties.Value.ApproximateMessagesCount;
        }
    }
}