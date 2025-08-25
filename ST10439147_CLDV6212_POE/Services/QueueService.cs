// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 1

//References:
// ClaudAI - https://claude.ai/
// ChatGPT - https://chat.openai.com/
// W3schools - https://www.w3schools.com/
// IIEVC School of Computer Science Youtube channel for Azure services setup and use https://www.youtube.com/@VCSOCS
// AzureApp project done in class with lecturer

using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ST10439147_CLDV6212_POE.Models;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Services
{
    public class QueueService
    {
        private readonly string _connectionString;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly ILogger<QueueService> _logger;

        // Azure Storage Queue limits
        private const int MAX_PEEK_MESSAGES = 32;
        private const int MAX_RECEIVE_MESSAGES = 32;

        public QueueService(IConfiguration configuration, ILogger<QueueService> logger)
        {
            _connectionString = configuration["AzureStorage:ConnectionString"];

            if (string.IsNullOrEmpty(_connectionString))
            {
                throw new InvalidOperationException("Azure Storage connection string is not configured.");
            }

            _queueServiceClient = new QueueServiceClient(_connectionString);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Sends an order message to the order processing queue
        /// </summary>
        public async Task SendOrderMessageAsync(OrderMessage orderMessage)
        {
            if (orderMessage == null)
            {
                throw new ArgumentNullException(nameof(orderMessage));
            }

            try
            {
                var queueClient = _queueServiceClient.GetQueueClient("ordermsg");
                await queueClient.CreateIfNotExistsAsync();

                var messageJson = JsonSerializer.Serialize(orderMessage, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await queueClient.SendMessageAsync(messageJson);
                _logger.LogInformation("Order message sent successfully for OrderId: {OrderId}", orderMessage.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send order message for OrderId: {OrderId}", orderMessage?.OrderId);
                throw new InvalidOperationException($"Failed to send order message: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Sends an inventory message to the inventory processing queue
        /// </summary>
        public async Task SendInventoryMessageAsync(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Message cannot be null or empty", nameof(message));
            }

            try
            {
                var queueClient = _queueServiceClient.GetQueueClient("inventory-msg");
                await queueClient.CreateIfNotExistsAsync();

                await queueClient.SendMessageAsync(message);
                _logger.LogInformation("Inventory message sent successfully: {Message}", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send inventory message: {Message}", message);
                throw new InvalidOperationException($"Failed to send inventory message: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Generic method to send a message to any queue
        /// </summary>
        public async Task SendMessageAsync(string queueName, string message)
        {
            if (string.IsNullOrEmpty(queueName))
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Message cannot be null or empty", nameof(message));
            }

            try
            {
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                await queueClient.CreateIfNotExistsAsync();

                await queueClient.SendMessageAsync(message);
                _logger.LogInformation("Message sent successfully to queue: {QueueName}", queueName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message to queue: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to send message to queue '{queueName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Retrieves messages from a queue without deleting them (peek operation)
        /// </summary>
        public async Task<List<QueueMessageInfo>> GetQueueMessagesAsync(string queueName, int maxMessages = 10)
        {
            if (string.IsNullOrEmpty(queueName))
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            // Enforce Azure Storage Queue limits
            maxMessages = Math.Min(maxMessages, MAX_RECEIVE_MESSAGES);

            try
            {
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                await queueClient.CreateIfNotExistsAsync();

                var messages = new List<QueueMessageInfo>();
                var receivedMessages = await queueClient.ReceiveMessagesAsync(maxMessages);

                foreach (var message in receivedMessages.Value)
                {
                    messages.Add(new QueueMessageInfo
                    {
                        MessageId = message.MessageId,
                        MessageText = message.MessageText,
                        PopReceipt = message.PopReceipt,
                        InsertedOn = message.InsertedOn,
                        ExpiresOn = message.ExpiresOn,
                        DequeueCount = message.DequeueCount
                    });
                }

                _logger.LogInformation("Retrieved {Count} messages from queue: {QueueName}", messages.Count, queueName);
                return messages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve messages from queue: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to retrieve messages from queue '{queueName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Peeks at messages without removing them from the queue
        /// </summary>
        public async Task<List<string>> PeekQueueMessagesAsync(string queueName, int maxMessages = 10)
        {
            if (string.IsNullOrEmpty(queueName))
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            // Enforce Azure Storage Queue limits - this is the key fix!
            maxMessages = Math.Min(maxMessages, MAX_PEEK_MESSAGES);

            try
            {
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                await queueClient.CreateIfNotExistsAsync();

                var messages = new List<string>();

                // For larger requests, we need to make multiple peek operations
                int remainingMessages = maxMessages;
                int requestedMessages = Math.Min(remainingMessages, MAX_PEEK_MESSAGES);

                while (remainingMessages > 0 && requestedMessages > 0)
                {
                    var peekedMessages = await queueClient.PeekMessagesAsync(requestedMessages);

                    if (!peekedMessages.Value.Any())
                    {
                        // No more messages available
                        break;
                    }

                    foreach (var message in peekedMessages.Value)
                    {
                        messages.Add(message.MessageText);
                    }

                    remainingMessages -= peekedMessages.Value.Length;
                    requestedMessages = Math.Min(remainingMessages, MAX_PEEK_MESSAGES);

                    // If we got fewer messages than requested, there are no more messages
                    if (peekedMessages.Value.Length < Math.Min(maxMessages, MAX_PEEK_MESSAGES))
                    {
                        break;
                    }
                }

                _logger.LogInformation("Peeked at {Count} messages from queue: {QueueName}", messages.Count, queueName);
                return messages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to peek messages from queue: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to peek messages from queue '{queueName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Deletes a specific message from the queue
        /// </summary>
        public async Task DeleteMessageAsync(string queueName, string messageId, string popReceipt)
        {
            if (string.IsNullOrEmpty(queueName))
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));
            }

            if (string.IsNullOrEmpty(popReceipt))
            {
                throw new ArgumentException("Pop receipt cannot be null or empty", nameof(popReceipt));
            }

            try
            {
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                await queueClient.DeleteMessageAsync(messageId, popReceipt);
                _logger.LogInformation("Message deleted successfully from queue: {QueueName}, MessageId: {MessageId}", queueName, messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete message from queue: {QueueName}, MessageId: {MessageId}", queueName, messageId);
                throw new InvalidOperationException($"Failed to delete message from queue '{queueName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets the approximate number of messages in the queue
        /// </summary>
        public async Task<int> GetQueueLengthAsync(string queueName)
        {
            if (string.IsNullOrEmpty(queueName))
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            try
            {
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                await queueClient.CreateIfNotExistsAsync();

                var properties = await queueClient.GetPropertiesAsync();
                var messageCount = properties.Value.ApproximateMessagesCount;

                _logger.LogInformation("Queue {QueueName} has approximately {MessageCount} messages", queueName, messageCount);
                return messageCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get queue length for: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to get queue length for '{queueName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Clears all messages from a queue
        /// </summary>
        public async Task ClearQueueAsync(string queueName)
        {
            if (string.IsNullOrEmpty(queueName))
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            try
            {
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                await queueClient.CreateIfNotExistsAsync();
                await queueClient.ClearMessagesAsync();

                _logger.LogInformation("Queue cleared successfully: {QueueName}", queueName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear queue: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to clear queue '{queueName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Updates the visibility timeout of a message (extends processing time)
        /// </summary>
        public async Task UpdateMessageVisibilityAsync(string queueName, string messageId, string popReceipt, TimeSpan visibilityTimeout)
        {
            if (string.IsNullOrEmpty(queueName))
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));
            }

            if (string.IsNullOrEmpty(popReceipt))
            {
                throw new ArgumentException("Pop receipt cannot be null or empty", nameof(popReceipt));
            }

            try
            {
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                await queueClient.UpdateMessageAsync(messageId, popReceipt, visibilityTimeout: visibilityTimeout);

                _logger.LogInformation("Message visibility updated for queue: {QueueName}, MessageId: {MessageId}", queueName, messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update message visibility for queue: {QueueName}, MessageId: {MessageId}", queueName, messageId);
                throw new InvalidOperationException($"Failed to update message visibility for queue '{queueName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Verifies if a message was sent to the queue by searching for specific text
        /// </summary>
        public async Task<bool> VerifyMessageSentAsync(string queueName, string searchText, int timeoutSeconds = 30)
        {
            var endTime = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (DateTime.UtcNow < endTime)
            {
                try
                {
                    // Use a smaller batch size for verification to avoid the limit issue
                    var messages = await PeekQueueMessagesAsync(queueName, 32);

                    if (messages.Any(m => m.Contains(searchText)))
                    {
                        _logger.LogInformation("Message verified in queue {QueueName}: {SearchText}", queueName, searchText);
                        return true;
                    }

                    // Wait before checking again
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking queue {QueueName} for message verification", queueName);
                }
            }

            _logger.LogWarning("Message verification timeout for queue {QueueName}: {SearchText}", queueName, searchText);
            return false;
        }

        /// <summary>
        /// Gets detailed queue information including properties and metadata
        /// </summary>
        public async Task<QueueProperties> GetQueuePropertiesAsync(string queueName)
        {
            if (string.IsNullOrEmpty(queueName))
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            try
            {
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                await queueClient.CreateIfNotExistsAsync();

                var properties = await queueClient.GetPropertiesAsync();
                _logger.LogInformation("Retrieved properties for queue: {QueueName}", queueName);

                return properties.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get properties for queue: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to get properties for queue '{queueName}': {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Helper class to encapsulate queue message information
    /// </summary>
    public class QueueMessageInfo
    {
        public string MessageId { get; set; } = string.Empty;
        public string MessageText { get; set; } = string.Empty;
        public string PopReceipt { get; set; } = string.Empty;
        public DateTimeOffset? InsertedOn { get; set; }
        public DateTimeOffset? ExpiresOn { get; set; }
        public long DequeueCount { get; set; }
    }
}