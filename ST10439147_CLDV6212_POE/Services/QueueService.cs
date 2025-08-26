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
    // Claud.Ai - Assisted with code generation and optimization
    public class QueueService
    {
        // Azure Storage connection string
        private readonly string _connectionString;
        // Client to interact with Azure Storage Queues
        private readonly QueueServiceClient _queueServiceClient;
        // Logger for logging information and errors
        private readonly ILogger<QueueService> _logger;
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Azure Storage Queue limits
        private const int MAX_PEEK_MESSAGES = 32; // Maximum number of messages to peek at once (Azure limit)
        private const int MAX_RECEIVE_MESSAGES = 32; // Maximum number of messages to receive at once (Azure limit)

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Constructor to initialize the QueueService with configuration and logger
        public QueueService(IConfiguration configuration, ILogger<QueueService> logger)
        {
            // Retrieve connection string from configuration
            _connectionString = configuration["AzureStorage:ConnectionString"];

            // Validate connection string
            if (string.IsNullOrEmpty(_connectionString))
            {
                throw new InvalidOperationException("Azure Storage connection string is not configured.");
            }

            // Initialize the QueueServiceClient for Azure Storage Queues
            _queueServiceClient = new QueueServiceClient(_connectionString);
            // Initialize the logger
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Sends an order message to the order processing queue
        // The message is serialized to JSON format
        // The queue is created if it does not exist
        // Logs success or failure of the operation
        // Throws InvalidOperationException on failure
        public async Task SendOrderMessageAsync(OrderMessage orderMessage)
        {
            if (orderMessage == null)// Validate order message
            {
                throw new ArgumentNullException(nameof(orderMessage));
            }

            try
            {
                // Get the queue client for the "ordermsg" queue
                var queueClient = _queueServiceClient.GetQueueClient("ordermsg");
                // Create the queue if it does not exist
                await queueClient.CreateIfNotExistsAsync();

                // Serialize the order message to JSON using camel case property naming
                var messageJson = JsonSerializer.Serialize(orderMessage, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                // Send the serialized message to the queue
                await queueClient.SendMessageAsync(messageJson);
                // Log success
                _logger.LogInformation("Order message sent successfully for OrderId: {OrderId}", orderMessage.OrderId);
            }
            catch (Exception ex)
            {
                // Log error and rethrow as InvalidOperationException
                _logger.LogError(ex, "Failed to send order message for OrderId: {OrderId}", orderMessage?.OrderId);
                throw new InvalidOperationException($"Failed to send order message: {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Sends an inventory message to the inventory processing queue
        // The queue is created if it does not exist
        // Logs success or failure of the operation
        // Throws InvalidOperationException on failure
        // The message is a simple string
        // This method can be expanded to handle complex message types as needed
        // Example message could be a product ID or a JSON string with inventory details
        // Ensure the message format is agreed upon by producers and consumers
        // This method is separate from SendOrderMessageAsync for clarity and separation of concerns
        public async Task SendInventoryMessageAsync(string message)
        {
            if (string.IsNullOrEmpty(message))// Validate message
            {
                throw new ArgumentException("Message cannot be null or empty", nameof(message));
            }

            try
            {
                // Get the queue client for the "inventory-msg" queue
                var queueClient = _queueServiceClient.GetQueueClient("inventory-msg");
                // Create the queue if it does not exist
                await queueClient.CreateIfNotExistsAsync();

                // Send the message to the queue
                await queueClient.SendMessageAsync(message);
                // Log success
                _logger.LogInformation("Inventory message sent successfully: {Message}", message);
            }
            catch (Exception ex)
            {
                // Log error and rethrow as InvalidOperationException
                _logger.LogError(ex, "Failed to send inventory message: {Message}", message);
                throw new InvalidOperationException($"Failed to send inventory message: {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Generic method to send a message to any queue
        // The queue is created if it does not exist
        // Logs success or failure of the operation
        // Throws InvalidOperationException on failure
        // The message is a simple string
        // This method can be used for various message types as needed
        public async Task SendMessageAsync(string queueName, string message)
        {
            if (string.IsNullOrEmpty(queueName))// Validate queue name
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            if (string.IsNullOrEmpty(message))// Validate message
            {
                throw new ArgumentException("Message cannot be null or empty", nameof(message));
            }

            try
            {
                // Get the queue client for the specified queue
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                // Create the queue if it does not exist
                await queueClient.CreateIfNotExistsAsync();

                // Send the message to the queue
                await queueClient.SendMessageAsync(message);
                // Log success
                _logger.LogInformation("Message sent successfully to queue: {QueueName}", queueName);
            }
            catch (Exception ex)
            {
                // Log error and rethrow as InvalidOperationException
                _logger.LogError(ex, "Failed to send message to queue: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to send message to queue '{queueName}': {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Retrieves messages from a queue and removes them (receive operation)
        // The queue is created if it does not exist
        // Logs success or failure of the operation
        // Throws InvalidOperationException on failure
        // Returns a list of QueueMessageInfo objects containing message details
        // The maxMessages parameter controls how many messages to retrieve (up to Azure limit)
        // This method is useful for processing messages that need to be removed from the queue
        public async Task<List<QueueMessageInfo>> GetQueueMessagesAsync(string queueName, int maxMessages = 10)
        {
            if (string.IsNullOrEmpty(queueName))// Validate queue name
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            // Enforce Azure Storage Queue limits
            maxMessages = Math.Min(maxMessages, MAX_RECEIVE_MESSAGES);

            try
            {
                // Get the queue client for the specified queue
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                // Create the queue if it does not exist
                await queueClient.CreateIfNotExistsAsync();

                var messages = new List<QueueMessageInfo>();
                // Receive messages from the queue (removes them from visibility)
                var receivedMessages = await queueClient.ReceiveMessagesAsync(maxMessages);

                // Map received messages to QueueMessageInfo objects
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

                // Log success
                _logger.LogInformation("Retrieved {Count} messages from queue: {QueueName}", messages.Count, queueName);
                return messages;
            }
            catch (Exception ex)
            {
                // Log error and rethrow as InvalidOperationException
                _logger.LogError(ex, "Failed to retrieve messages from queue: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to retrieve messages from queue '{queueName}': {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Peeks at messages without removing them from the queue
        // The queue is created if it does not exist
        // Logs success or failure of the operation
        // Throws InvalidOperationException on failure
        // Returns a list of message texts
        // The maxMessages parameter controls how many messages to peek at (up to Azure limit)
        // This method is useful for inspecting messages without affecting their visibility
        // Handles larger requests by making multiple peek operations if needed
        // Ensures compliance with Azure Storage Queue limits
        // This method is separate from GetQueueMessagesAsync to provide different functionality
        public async Task<List<string>> PeekQueueMessagesAsync(string queueName, int maxMessages = 10)
        {
            if (string.IsNullOrEmpty(queueName))// Validate queue name
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            // Enforce Azure Storage Queue limits - this is the key fix!
            maxMessages = Math.Min(maxMessages, MAX_PEEK_MESSAGES);

            try
            {
                // Get the queue client for the specified queue
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                // Create the queue if it does not exist
                await queueClient.CreateIfNotExistsAsync();

                var messages = new List<string>();

                // For larger requests, we need to make multiple peek operations
                int remainingMessages = maxMessages;
                int requestedMessages = Math.Min(remainingMessages, MAX_PEEK_MESSAGES);

                while (remainingMessages > 0 && requestedMessages > 0)
                {
                    // Peek messages from the queue (does not remove them)
                    var peekedMessages = await queueClient.PeekMessagesAsync(requestedMessages);

                    if (!peekedMessages.Value.Any())
                    {
                        // No more messages available
                        break;
                    }

                    // Add peeked message texts to the result list
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

                // Log success
                _logger.LogInformation("Peeked at {Count} messages from queue: {QueueName}", messages.Count, queueName);
                return messages;
            }
            catch (Exception ex)
            {
                // Log error and rethrow as InvalidOperationException
                _logger.LogError(ex, "Failed to peek messages from queue: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to peek messages from queue '{queueName}': {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Deletes a specific message from the queue
        // The queue is identified by its name
        // The message is identified by its ID and pop receipt
        // Logs success or failure of the operation
        // Throws InvalidOperationException on failure
        // This method is useful for removing messages that have been processed
        // Ensures that only the intended message is deleted using both ID and pop receipt
        //--------------------------------------------------------------//
        // Only going to be implemented in Part 2
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
                // Get the queue client for the specified queue
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                // Delete the message using its ID and pop receipt
                await queueClient.DeleteMessageAsync(messageId, popReceipt);
                // Log success
                _logger.LogInformation("Message deleted successfully from queue: {QueueName}, MessageId: {MessageId}", queueName, messageId);
            }
            catch (Exception ex)
            {
                // Log error and rethrow as InvalidOperationException
                _logger.LogError(ex, "Failed to delete message from queue: {QueueName}, MessageId: {MessageId}", queueName, messageId);
                throw new InvalidOperationException($"Failed to delete message from queue '{queueName}': {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Gets the approximate number of messages in the queue
        public async Task<int> GetQueueLengthAsync(string queueName)// The queue is created if it does not exist
        {
            if (string.IsNullOrEmpty(queueName))
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            try
            {
                // Get the queue client for the specified queue
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                // Create the queue if it does not exist
                await queueClient.CreateIfNotExistsAsync();

                // Get queue properties
                var properties = await queueClient.GetPropertiesAsync();
                // Get approximate message count from properties
                var messageCount = properties.Value.ApproximateMessagesCount;

                // Log success
                _logger.LogInformation("Queue {QueueName} has approximately {MessageCount} messages", queueName, messageCount);
                return messageCount;
            }
            catch (Exception ex)
            {
                // Log error and rethrow as InvalidOperationException
                _logger.LogError(ex, "Failed to get queue length for: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to get queue length for '{queueName}': {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Clears all messages from a queue
        // Only going to be implemented in Part 2
        public async Task ClearQueueAsync(string queueName)
        {
            if (string.IsNullOrEmpty(queueName))// Validate queue name
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            try
            {
                // Get the queue client for the specified queue
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                // Create the queue if it does not exist
                await queueClient.CreateIfNotExistsAsync();
                // Clear all messages from the queue
                await queueClient.ClearMessagesAsync();

                // Log success
                _logger.LogInformation("Queue cleared successfully: {QueueName}", queueName);
            }
            catch (Exception ex)
            {
                // Log error and rethrow as InvalidOperationException
                _logger.LogError(ex, "Failed to clear queue: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to clear queue '{queueName}': {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Updates the visibility timeout of a message (extends processing time)
        public async Task UpdateMessageVisibilityAsync(string queueName, string messageId, string popReceipt, TimeSpan visibilityTimeout)
        {
            if (string.IsNullOrEmpty(queueName))// Validate queue name
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            if (string.IsNullOrEmpty(messageId))// Validate message ID
            {
                throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));
            }

            if (string.IsNullOrEmpty(popReceipt))// Validate pop receipt
            {
                throw new ArgumentException("Pop receipt cannot be null or empty", nameof(popReceipt));
            }

            try
            {
                // Get the queue client for the specified queue
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                // Update the message's visibility timeout
                await queueClient.UpdateMessageAsync(messageId, popReceipt, visibilityTimeout: visibilityTimeout);

                // Log success
                _logger.LogInformation("Message visibility updated for queue: {QueueName}, MessageId: {MessageId}", queueName, messageId);
            }
            catch (Exception ex)
            {
                // Log error and rethrow as InvalidOperationException
                _logger.LogError(ex, "Failed to update message visibility for queue: {QueueName}, MessageId: {MessageId}", queueName, messageId);
                throw new InvalidOperationException($"Failed to update message visibility for queue '{queueName}': {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Verifies if a message was sent to the queue by searching for specific text
        // Retries for a specified timeout period if the message is not found
        // Logs success or failure of the verification
        // Returns true if the message is found, false if timeout occurs
        // This method is useful for testing and validation purposes
        // It peeks at messages to avoid removing them from the queue
        // Handles transient errors by logging and continuing retries
        // The searchText parameter is used to identify the message content
        // The timeoutSeconds parameter controls how long to keep retrying
        // Uses a smaller batch size for peeking to avoid Azure limits
        public async Task<bool> VerifyMessageSentAsync(string queueName, string searchText, int timeoutSeconds = 30)
        {
            // Calculate end time for timeout
            var endTime = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (DateTime.UtcNow < endTime)
            {
                try
                {
                    // Use a smaller batch size for verification to avoid the limit issue
                    var messages = await PeekQueueMessagesAsync(queueName, 32);

                    // Check if any message contains the search text
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
                    // Log warning and continue checking
                    _logger.LogWarning(ex, "Error checking queue {QueueName} for message verification", queueName);
                }
            }

            // Log timeout warning
            _logger.LogWarning("Message verification timeout for queue {QueueName}: {SearchText}", queueName, searchText);
            return false;
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Gets detailed queue information including properties and metadata
        public async Task<QueueProperties> GetQueuePropertiesAsync(string queueName)
        {
            if (string.IsNullOrEmpty(queueName))// Validate queue name
            {
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            }

            try
            {
                // Get the queue client for the specified queue
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                // Create the queue if it does not exist
                await queueClient.CreateIfNotExistsAsync();

                // Get queue properties
                var properties = await queueClient.GetPropertiesAsync();
                _logger.LogInformation("Retrieved properties for queue: {QueueName}", queueName);

                return properties.Value;
            }
            catch (Exception ex)
            {
                // Log error and rethrow as InvalidOperationException
                _logger.LogError(ex, "Failed to get properties for queue: {QueueName}", queueName);
                throw new InvalidOperationException($"Failed to get properties for queue '{queueName}': {ex.Message}", ex);
            }
        }
    }
    //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
    // Helper class to encapsulate queue message information
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
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//