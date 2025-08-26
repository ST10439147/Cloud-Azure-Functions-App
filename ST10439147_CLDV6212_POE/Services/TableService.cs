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
        private readonly TableClient _customersTableClient;// Table client for customers
        private readonly TableClient _productsTableClient;// Table client for products
        private readonly TableClient _ordersTableClient;// Table client for orders
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        public TableService(IConfiguration config)// Constructor to initialize TableService with configuration
        {
            var connectionString = config["AzureStorage:ConnectionString"];// Retrieve connection string from configuration

            if (string.IsNullOrEmpty(connectionString))// Check if connection string is null or empty
            {
                // Throw exception if connection string is not configured
                throw new InvalidOperationException("Azure Storage connection string is not configured.");
            }

            try
            {
                _customersTableClient = new TableClient(connectionString, "customers");// Initialize TableClient for customers table
                _customersTableClient.CreateIfNotExistsAsync().Wait();// Ensure the table exists

                _productsTableClient = new TableClient(connectionString, "products");// Initialize TableClient for products table
                _productsTableClient.CreateIfNotExistsAsync().Wait();// Ensure the table exists

                _ordersTableClient = new TableClient(connectionString, "orders");// Initialize TableClient for orders table
                _ordersTableClient.CreateIfNotExistsAsync().Wait();// Ensure the table exists
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize Azure Table Storage: {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        //  Using region to organize customer-related methods, product-related methods, and order-related methods
        #region Customer Operations
        // Method to insert a new customer into the customers table
        // If RowKey is not set, generate a new GUID for it
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Use async-await for asynchronous operations
        // Ensure optimistic concurrency with ETag during updates
        // Validate input data using data annotations in the Customer model
        // Ensure table existence during service initialization
        public async Task InsertCustomerAsync(Customer customer)
        {
            try
            {
                // Only set RowKey if it's not already set
                if (string.IsNullOrEmpty(customer.RowKey))
                {
                    customer.RowKey = Guid.NewGuid().ToString();// Generate a new GUID for RowKey
                }
                await _customersTableClient.AddEntityAsync(customer);// Insert the customer entity into the table
            }
            catch (Exception ex)
            {
                // Handle exceptions and provide meaningful error messages
                throw new InvalidOperationException($"Failed to insert customer: {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to retrieve all customers from the customers table
        // Use async-await for asynchronous operations
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Return a list of Customer objects
        // Use pagination if necessary for large datasets (not implemented here for simplicity)
        // Ensure table existence during service initialization
        // Validate input data using data annotations in the Customer model
        public async Task<List<Customer>> GetAllCustomersAsync()
        {
            try
            {
                var customers = new List<Customer>();// List to hold retrieved customers

                await foreach (Customer entity in _customersTableClient.QueryAsync<Customer>())// Query all customer entities asynchronously
                {
                    customers.Add(entity);// Add each customer entity to the list
                }
                return customers;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to retrieve customers: {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to retrieve a customer by their PartitionKey and RowKey
        // Use async-await for asynchronous operations
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Return the Customer object if found, otherwise handle not found scenario
        // Ensure table existence during service initialization
        // Validate input data using data annotations in the Customer model
        // Use appropriate exception handling for not found cases
        // Consider using caching for frequently accessed customers (not implemented here for simplicity)
        public async Task<Customer> GetCustomerByIdAsync(string partitionKey, string rowKey)
        {
            try
            {
                // Retrieve the customer entity by PartitionKey and RowKey
                var response = await _customersTableClient.GetEntityAsync<Customer>(partitionKey, rowKey);
                return response.Value;// Return the retrieved customer entity
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to retrieve customer: {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to update an existing customer in the customers table
        // Use async-await for asynchronous operations
        // Wrap operations in try-catch to handle exceptions and provide meaningful error message
        // Validate input data using data annotations in the Customer model
        // Ensure table existence during service initialization
        // Handle not found scenarios appropriately
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to delete a customer from the customers table by their PartitionKey and RowKey
        // Use async-await for asynchronous operations
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Ensure table existence during service initialization
        // Validate input data using data annotations in the Customer model
        // Handle not found scenarios appropriately
        // Consider cascading deletes if there are related entities (not implemented here for simplicity)
        public async Task DeleteCustomerAsync(string partitionKey, string rowKey)
        {
            try
            {
                await _customersTableClient.DeleteEntityAsync(partitionKey, rowKey);// Delete the customer entity by PartitionKey and RowKey
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to delete customer: {ex.Message}", ex);// Handle exceptions and provide meaningful error messages
            }
        }

        #endregion
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        #region Product Operations
        // Method to insert a new product into the products table
        // If RowKey is not set, generate a new GUID for it
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Use async-await for asynchronous operations
        // Ensure optimistic concurrency with ETag during updates
        // Validate input data using data annotations in the Product model
        // Ensure table existence during service initialization
        // Method to insert a new product into the products table
        // If RowKey is not set, generate a new GUID for it
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to retrieve all products from the products table
        // Use async-await for asynchronous operations
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Return a list of Product objects
        // Use pagination if necessary for large datasets (not implemented here for simplicity)
        // Ensure table existence during service initialization
        // Validate input data using data annotations in the Product model
        // Consider using caching for frequently accessed products (not implemented here for simplicity)
        public async Task<List<Product>> GetAllProductsAsync()
        {
            try
            {
                var products = new List<Product>();// List to hold retrieved products

                // Query all product entities asynchronously, adding each to the list
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
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to retrieve a product by its PartitionKey and RowKey
        // Use async-await for asynchronous operations
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Return the Product object if found, otherwise handle not found scenario
        // Ensure table existence during service initialization
        // Validate input data using data annotations in the Product model
        // Use appropriate exception handling for not found cases
        // Consider using caching for frequently accessed products (not implemented here for simplicity)
        public async Task<Product> GetProductByIdAsync(string partitionKey, string rowKey)
        {
            try
            {
                // Retrieve the product entity by PartitionKey and RowKey, returning the value
                var response = await _productsTableClient.GetEntityAsync<Product>(partitionKey, rowKey);
                return response.Value;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to retrieve product: {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to update an existing product in the products table
        // Use async-await for asynchronous operations
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Validate input data using data annotations in the Product model
        // Ensure table existence during service initialization
        // Handle not found scenarios appropriately
        public async Task UpdateProductAsync(Product product)
        {
            try
            {
                // Use Replace mode with the ETag for optimistic concurrency, updating the product entity
                await _productsTableClient.UpdateEntityAsync(product, product.ETag, TableUpdateMode.Replace);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to update product: {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to delete a product from the products table by its PartitionKey and RowKey
        // Use async-await for asynchronous operations
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Ensure table existence during service initialization
        // Validate input data using data annotations in the Product model
        // Handle not found scenarios appropriately
        // Consider cascading deletes if there are related entities (not implemented here for simplicity)
        public async Task DeleteProductAsync(string partitionKey, string rowKey)
        {
            try
            {
                // Delete the product entity by PartitionKey and RowKey
                await _productsTableClient.DeleteEntityAsync(partitionKey, rowKey);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to delete product: {ex.Message}", ex);
            }
        }

        #endregion
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        #region Order Operations
        // Method to insert a new order into the orders table
        // If RowKey is not set, generate a new GUID for it
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Use async-await for asynchronous operations
        // Ensure optimistic concurrency with ETag during updates
        // Validate input data using data annotations in the Order model
        // Ensure table existence during service initialization
        public async Task InsertOrderAsync(Order order)
        {
            try
            {
                if (string.IsNullOrEmpty(order.RowKey))// Only set RowKey if it's not already set
                {
                    order.RowKey = Guid.NewGuid().ToString();// Generate a new GUID for RowKey
                }
                await _ordersTableClient.AddEntityAsync(order);// Insert the order entity into the table
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to insert order: {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to retrieve all orders from the orders table
        // Use async-await for asynchronous operations
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Return a list of Order objects
        public async Task<List<Order>> GetAllOrdersAsync()
        {
            try
            {
                var orders = new List<Order>();// List to hold retrieved orders

                // Query all order entities asynchronously, adding each to the list
                await foreach (Order entity in _ordersTableClient.QueryAsync<Order>())
                {
                    orders.Add(entity);// Add each order entity to the list
                }
                return orders;// Return the list of orders
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to retrieve orders: {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to retrieve an order by its PartitionKey and RowKey
        // Use async-await for asynchronous operations
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Return the Order object if found, otherwise handle not found scenario
        // Ensure table existence during service initialization
        // Validate input data using data annotations in the Order model
        // Uses exception handling for not found cases
        public async Task<Order> GetOrderByIdAsync(string partitionKey, string rowKey)
        {
            try
            {
                // Retrieve the order entity by PartitionKey and RowKey, returning the value
                var response = await _ordersTableClient.GetEntityAsync<Order>(partitionKey, rowKey);
                return response.Value;// Return the retrieved order entity
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to retrieve order: {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to update an existing order in the orders table
        // Use async-await for asynchronous operations
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Validate input data using data annotations in the Order model
        // Ensure table existence during service initialization
        // Handle not found scenarios appropriately
        public async Task UpdateOrderAsync(Order order)
        {
            try
            {
                // Use Replace mode with the ETag for optimistic concurrency, updating the order entity
                await _ordersTableClient.UpdateEntityAsync(order, order.ETag, TableUpdateMode.Replace);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to update order: {ex.Message}", ex);
            }
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to delete an order from the orders table by its PartitionKey and RowKey
        // Use async-await for asynchronous operations
        // Wrap operations in try-catch to handle exceptions and provide meaningful error messages
        // Ensure table existence during service initialization
        // Validate input data using data annotations in the Order model
        // Handle not found scenarios appropriately
        public async Task DeleteOrderAsync(string partitionKey, string rowKey)
        {
            try
            {
                // Delete the order entity by PartitionKey and RowKey
                await _ordersTableClient.DeleteEntityAsync(partitionKey, rowKey);
            }
            catch (Exception ex)
            {
                // Handle exceptions and provide meaningful error messages
                throw new InvalidOperationException($"Failed to delete order: {ex.Message}", ex);
            }
        }

        #endregion
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//