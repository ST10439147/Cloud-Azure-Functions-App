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

using Azure;
using Azure.Data.Tables;
using ST10439147_CLDV6212_POE.Models;

namespace ST10439147_CLDV6212_POE.Services
{
    public class TableService
    {
        private readonly TableClient _customersTableClient;
        private readonly TableClient _productsTableClient;
        private readonly TableClient _ordersTableClient;

        public TableService(IConfiguration config)
        {
            var connectionString = config["AzureStorage:ConnectionString"];

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Azure Storage connection string is not configured.");
            }

            try
            {
                _customersTableClient = new TableClient(connectionString, "customers");
                _customersTableClient.CreateIfNotExistsAsync().Wait();

                _productsTableClient = new TableClient(connectionString, "products");
                _productsTableClient.CreateIfNotExistsAsync().Wait();

                _ordersTableClient = new TableClient(connectionString, "orders");
                _ordersTableClient.CreateIfNotExistsAsync().Wait();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize Azure Table Storage: {ex.Message}", ex);
            }
        }

        #region Customer Operations

        public async Task InsertCustomerAsync(Customer customer)
        {
            try
            {
                // Only set RowKey if it's not already set
                if (string.IsNullOrEmpty(customer.RowKey))
                {
                    customer.RowKey = Guid.NewGuid().ToString();
                }
                await _customersTableClient.AddEntityAsync(customer);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to insert customer: {ex.Message}", ex);
            }
        }

        public async Task<List<Customer>> GetAllCustomersAsync()
        {
            try
            {
                var customers = new List<Customer>();

                await foreach (Customer entity in _customersTableClient.QueryAsync<Customer>())
                {
                    customers.Add(entity);
                }
                return customers;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to retrieve customers: {ex.Message}", ex);
            }
        }

        public async Task<Customer> GetCustomerByIdAsync(string partitionKey, string rowKey)
        {
            try
            {
                var response = await _customersTableClient.GetEntityAsync<Customer>(partitionKey, rowKey);
                return response.Value;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to retrieve customer: {ex.Message}", ex);
            }
        }

        public async Task UpdateCustomerAsync(Customer customer)
        {
            try
            {
                // Use Replace mode with the ETag for optimistic concurrency
                await _customersTableClient.UpdateEntityAsync(customer, customer.ETag, TableUpdateMode.Replace);
            }
            catch (RequestFailedException ex) when (ex.Status == 412) // Precondition Failed
            {
                throw new InvalidOperationException("The customer has been modified by another user. Please refresh and try again.", ex);
            }
            catch (RequestFailedException ex) when (ex.Status == 404) // Not Found
            {
                throw new InvalidOperationException("The customer no longer exists.", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to update customer: {ex.Message}", ex);
            }
        }

        public async Task DeleteCustomerAsync(string partitionKey, string rowKey)
        {
            try
            {
                await _customersTableClient.DeleteEntityAsync(partitionKey, rowKey);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to delete customer: {ex.Message}", ex);
            }
        }

        #endregion

        #region Product Operations

        public async Task InsertProductAsync(Product product)
        {
            try
            {
                if (string.IsNullOrEmpty(product.RowKey))
                {
                    product.RowKey = Guid.NewGuid().ToString();
                }
                await _productsTableClient.AddEntityAsync(product);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to insert product: {ex.Message}", ex);
            }
        }

        public async Task<List<Product>> GetAllProductsAsync()
        {
            try
            {
                var products = new List<Product>();

                await foreach (Product entity in _productsTableClient.QueryAsync<Product>())
                {
                    products.Add(entity);
                }
                return products;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to retrieve products: {ex.Message}", ex);
            }
        }

        public async Task<Product> GetProductByIdAsync(string partitionKey, string rowKey)
        {
            try
            {
                var response = await _productsTableClient.GetEntityAsync<Product>(partitionKey, rowKey);
                return response.Value;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to retrieve product: {ex.Message}", ex);
            }
        }

        public async Task UpdateProductAsync(Product product)
        {
            try
            {
                await _productsTableClient.UpdateEntityAsync(product, product.ETag, TableUpdateMode.Replace);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to update product: {ex.Message}", ex);
            }
        }

        public async Task DeleteProductAsync(string partitionKey, string rowKey)
        {
            try
            {
                await _productsTableClient.DeleteEntityAsync(partitionKey, rowKey);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to delete product: {ex.Message}", ex);
            }
        }

        #endregion

        #region Order Operations

        public async Task InsertOrderAsync(Order order)
        {
            try
            {
                if (string.IsNullOrEmpty(order.RowKey))
                {
                    order.RowKey = Guid.NewGuid().ToString();
                }
                await _ordersTableClient.AddEntityAsync(order);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to insert order: {ex.Message}", ex);
            }
        }

        public async Task<List<Order>> GetAllOrdersAsync()
        {
            try
            {
                var orders = new List<Order>();

                await foreach (Order entity in _ordersTableClient.QueryAsync<Order>())
                {
                    orders.Add(entity);
                }
                return orders;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to retrieve orders: {ex.Message}", ex);
            }
        }

        public async Task<Order> GetOrderByIdAsync(string partitionKey, string rowKey)
        {
            try
            {
                var response = await _ordersTableClient.GetEntityAsync<Order>(partitionKey, rowKey);
                return response.Value;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to retrieve order: {ex.Message}", ex);
            }
        }

        public async Task UpdateOrderAsync(Order order)
        {
            try
            {
                await _ordersTableClient.UpdateEntityAsync(order, order.ETag, TableUpdateMode.Replace);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to update order: {ex.Message}", ex);
            }
        }

        public async Task DeleteOrderAsync(string partitionKey, string rowKey)
        {
            try
            {
                await _ordersTableClient.DeleteEntityAsync(partitionKey, rowKey);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to delete order: {ex.Message}", ex);
            }
        }

        #endregion
    }
}