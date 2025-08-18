using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Files.Shares;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using ST10439147_CLDV6212_POE.Models;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Services
{
    public class AzureService
    {
        private readonly string _connectionString;
        private readonly TableServiceClient _tableServiceClient;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly ShareServiceClient _shareServiceClient;

        public AzureService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("AzureStorage");
            _tableServiceClient = new TableServiceClient(_connectionString);
            _blobServiceClient = new BlobServiceClient(_connectionString);
            _queueServiceClient = new QueueServiceClient(_connectionString);
            _shareServiceClient = new ShareServiceClient(_connectionString);
        }

        #region Table Storage Operations

        public async Task<Customer> AddCustomerAsync(Customer customer)
        {
            var tableClient = _tableServiceClient.GetTableClient("customers");
            await tableClient.CreateIfNotExistsAsync();

            customer.RowKey = Guid.NewGuid().ToString();
            await tableClient.AddEntityAsync(customer);
            return customer;
        }

        public async Task<List<Customer>> GetAllCustomersAsync()
        {
            var tableClient = _tableServiceClient.GetTableClient("customers");
            await tableClient.CreateIfNotExistsAsync();

            var customers = new List<Customer>();
            await foreach (var customer in tableClient.QueryAsync<Customer>())
            {
                customers.Add(customer);
            }
            return customers;
        }

        public async Task<Product> AddProductAsync(Product product)
        {
            var tableClient = _tableServiceClient.GetTableClient("products");
            await tableClient.CreateIfNotExistsAsync();

            product.RowKey = Guid.NewGuid().ToString();
            await tableClient.AddEntityAsync(product);
            return product;
        }

        public async Task<List<Product>> GetAllProductsAsync()
        {
            var tableClient = _tableServiceClient.GetTableClient("products");
            await tableClient.CreateIfNotExistsAsync();

            var products = new List<Product>();
            await foreach (var product in tableClient.QueryAsync<Product>())
            {
                products.Add(product);
            }
            return products;
        }

        public async Task<Order> AddOrderAsync(Order order)
        {
            var tableClient = _tableServiceClient.GetTableClient("orders");
            await tableClient.CreateIfNotExistsAsync();

            order.RowKey = Guid.NewGuid().ToString();
            await tableClient.AddEntityAsync(order);
            return order;
        }

        public async Task<List<Order>> GetAllOrdersAsync()
        {
            var tableClient = _tableServiceClient.GetTableClient("orders");
            await tableClient.CreateIfNotExistsAsync();

            var orders = new List<Order>();
            await foreach (var order in tableClient.QueryAsync<Order>())
            {
                orders.Add(order);
            }
            return orders;
        }

        #endregion

        #region Blob Storage Operations

        public async Task<string> UploadImageAsync(IFormFile file, string containerName = "product-images")
        {
            var blobContainerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            await blobContainerClient.CreateIfNotExistsAsync();
            await blobContainerClient.SetAccessPolicyAsync(Azure.Storage.Blobs.Models.PublicAccessType.Blob);

            var fileName = $"{Guid.NewGuid()}_{file.FileName}";
            var blobClient = blobContainerClient.GetBlobClient(fileName);

            using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, overwrite: true);

            return blobClient.Uri.ToString();
        }

        public async Task<List<string>> GetAllImageUrlsAsync(string containerName = "product-images")
        {
            var blobContainerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var imageUrls = new List<string>();

            await foreach (var blobItem in blobContainerClient.GetBlobsAsync())
            {
                var blobClient = blobContainerClient.GetBlobClient(blobItem.Name);
                imageUrls.Add(blobClient.Uri.ToString());
            }

            return imageUrls;
        }

        #endregion

        #region Queue Operations

        public async Task SendOrderMessageAsync(OrderMessage orderMessage)
        {
            var queueClient = _queueServiceClient.GetQueueClient("order-processing");
            await queueClient.CreateIfNotExistsAsync();

            var messageJson = JsonSerializer.Serialize(orderMessage);
            await queueClient.SendMessageAsync(messageJson);
        }

        public async Task SendInventoryMessageAsync(string message)
        {
            var queueClient = _queueServiceClient.GetQueueClient("inventory-management");
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

        #endregion

        #region File Storage Operations

        public async Task<string> UploadFileAsync(IFormFile file, string shareName = "contracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            await shareClient.CreateIfNotExistsAsync();

            var directoryClient = shareClient.GetRootDirectoryClient();
            var fileName = $"{Guid.NewGuid()}_{file.FileName}";
            var fileClient = directoryClient.GetFileClient(fileName);

            using var stream = file.OpenReadStream();
            await fileClient.CreateAsync(stream.Length);
            await fileClient.UploadAsync(stream);

            return fileName;
        }

        public async Task<List<string>> GetAllFilesAsync(string shareName = "contracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            var directoryClient = shareClient.GetRootDirectoryClient();
            var files = new List<string>();

            await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync())
            {
                if (!item.IsDirectory)
                {
                    files.Add(item.Name);
                }
            }

            return files;
        }

        public async Task<Stream> DownloadFileAsync(string fileName, string shareName = "contracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            var directoryClient = shareClient.GetRootDirectoryClient();
            var fileClient = directoryClient.GetFileClient(fileName);

            var download = await fileClient.DownloadAsync();
            return download.Value.Content;
        }

        #endregion
    }

}
