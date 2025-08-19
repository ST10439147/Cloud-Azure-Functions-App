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

        #endregion
    }
}