    // StudentNumber: ST10439147
    // StudentName: Dillon Rinkwest
    // CourseCode: CLDV6212
    // POE Part: 2 - Azure Functions Integration

    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Azure.Functions.Worker;
    using Microsoft.Extensions.Logging;
    using ST10439147_CLDV6212_POE.Models;
    using ST10439147_CLDV6212_POE.Services;
    using System.Text.Json;

    namespace ST10439147_CLDV6212_POE.Functions
    {
        public class TableServiceFunctions
        {
            private readonly ILogger<TableServiceFunctions> _logger;
            private readonly TableService _tableService;

            public TableServiceFunctions(ILogger<TableServiceFunctions> logger, TableService tableService)
            {
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
                _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            }

            #region Customer Functions

            /// <summary>
            /// HTTP Function to retrieve all customers
            /// GET /api/customers
            /// </summary>
            [Function("GetAllCustomers")]
            public async Task<IActionResult> GetAllCustomers(
                [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers")] HttpRequest req)
            {
                try
                {
                    _logger.LogInformation("GetAllCustomers function triggered");

                    var customers = await _tableService.GetAllCustomersAsync();

                    _logger.LogInformation("Retrieved {Count} customers", customers.Count);
                    return new OkObjectResult(customers);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving customers");
                    return new BadRequestObjectResult(new { error = "Failed to retrieve customers", message = ex.Message });
                }
            }

            /// <summary>
            /// HTTP Function to get a specific customer by ID
            /// GET /api/customers/{id}
            /// </summary>
            [Function("GetCustomerById")]
            public async Task<IActionResult> GetCustomerById(
                [HttpTrigger(AuthorizationLevel.Function, "get", Route = "customers/{id}")] HttpRequest req,
                string id)
            {
                try
                {
                    _logger.LogInformation("GetCustomerById function triggered for ID: {CustomerId}", id);

                    if (string.IsNullOrEmpty(id))
                    {
                        return new BadRequestObjectResult(new { error = "Customer ID is required" });
                    }

                    var customer = await _tableService.GetCustomerByIdAsync("Customer", id);

                    if (customer == null)
                    {
                        return new NotFoundObjectResult(new { error = "Customer not found" });
                    }

                    return new OkObjectResult(customer);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving customer {CustomerId}", id);
                    return new BadRequestObjectResult(new { error = "Failed to retrieve customer", message = ex.Message });
                }
            }

            /// <summary>
            /// HTTP Function to create a new customer
            /// POST /api/customers
            /// </summary>
            [Function("CreateCustomer")]
            public async Task<IActionResult> CreateCustomer(
                [HttpTrigger(AuthorizationLevel.Function, "post", Route = "customers")] HttpRequest req)
            {
                try
                {
                    _logger.LogInformation("CreateCustomer function triggered");

                    // Read the request body
                    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                    if (string.IsNullOrEmpty(requestBody))
                    {
                        return new BadRequestObjectResult(new { error = "Request body is required" });
                    }

                    // Deserialize the customer object
                    var customer = JsonSerializer.Deserialize<ST10439147_CLDV6212_POE.Models.Customer>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (customer == null)
                    {
                        return new BadRequestObjectResult(new { error = "Invalid customer data" });
                    }

                    // Ensure RowKey and PartitionKey are set
                    if (string.IsNullOrEmpty(customer.RowKey))
                    {
                        customer.RowKey = Guid.NewGuid().ToString();
                    }
                    customer.PartitionKey = "Customer";

                    await _tableService.InsertCustomerAsync(customer);

                    _logger.LogInformation("Customer created successfully: {CustomerId}", customer.RowKey);
                    return new CreatedResult($"/api/customers/{customer.RowKey}", customer);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Invalid JSON in request body");
                    return new BadRequestObjectResult(new { error = "Invalid JSON format", message = ex.Message });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating customer");
                    return new BadRequestObjectResult(new { error = "Failed to create customer", message = ex.Message });
                }
            }

            /// <summary>
            /// HTTP Function to update an existing customer
            /// PUT /api/customers/{id}
            /// </summary>
            [Function("UpdateCustomer")]
            public async Task<IActionResult> UpdateCustomer(
                [HttpTrigger(AuthorizationLevel.Function, "put", Route = "customers/{id}")] HttpRequest req,
                string id)
            {
                try
                {
                    _logger.LogInformation("UpdateCustomer function triggered for ID: {CustomerId}", id);

                    if (string.IsNullOrEmpty(id))
                    {
                        return new BadRequestObjectResult(new { error = "Customer ID is required" });
                    }

                    // Read the request body
                    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                    if (string.IsNullOrEmpty(requestBody))
                    {
                        return new BadRequestObjectResult(new { error = "Request body is required" });
                    }

                    // Deserialize the customer object
                    var customer = JsonSerializer.Deserialize<Customer>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (customer == null)
                    {
                        return new BadRequestObjectResult(new { error = "Invalid customer data" });
                    }

                    // Ensure the ID matches
                    if (customer.RowKey != id)
                    {
                        return new BadRequestObjectResult(new { error = "Customer ID mismatch" });
                    }

                    // Get existing customer to preserve ETag
                    var existingCustomer = await _tableService.GetCustomerByIdAsync("Customer", id);
                    if (existingCustomer == null)
                    {
                        return new NotFoundObjectResult(new { error = "Customer not found" });
                    }

                    // Update fields while preserving ETag
                    existingCustomer.FirstName = customer.FirstName;
                    existingCustomer.LastName = customer.LastName;
                    existingCustomer.Email = customer.Email;
                    existingCustomer.PhoneNumber = customer.PhoneNumber;

                    await _tableService.UpdateCustomerAsync(existingCustomer);

                    _logger.LogInformation("Customer updated successfully: {CustomerId}", id);
                    return new OkObjectResult(existingCustomer);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Invalid JSON in request body");
                    return new BadRequestObjectResult(new { error = "Invalid JSON format", message = ex.Message });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating customer {CustomerId}", id);
                    return new BadRequestObjectResult(new { error = "Failed to update customer", message = ex.Message });
                }
            }

            /// <summary>
            /// HTTP Function to delete a customer
            /// DELETE /api/customers/{id}
            /// </summary>
            [Function("DeleteCustomer")]
            public async Task<IActionResult> DeleteCustomer(
                [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "customers/{id}")] HttpRequest req,
                string id)
            {
                try
                {
                    _logger.LogInformation("DeleteCustomer function triggered for ID: {CustomerId}", id);

                    if (string.IsNullOrEmpty(id))
                    {
                        return new BadRequestObjectResult(new { error = "Customer ID is required" });
                    }

                    // Check if customer exists
                    var existingCustomer = await _tableService.GetCustomerByIdAsync("Customer", id);
                    if (existingCustomer == null)
                    {
                        return new NotFoundObjectResult(new { error = "Customer not found" });
                    }

                    await _tableService.DeleteCustomerAsync("Customer", id);

                    _logger.LogInformation("Customer deleted successfully: {CustomerId}", id);
                    return new NoContentResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting customer {CustomerId}", id);
                    return new BadRequestObjectResult(new { error = "Failed to delete customer", message = ex.Message });
                }
            }

            #endregion

            #region Product Functions

            /// <summary>
            /// HTTP Function to retrieve all products
            /// GET /api/products
            /// </summary>
            [Function("GetAllProducts")]
            public async Task<IActionResult> GetAllProducts(
                [HttpTrigger(AuthorizationLevel.Function, "get", Route = "products")] HttpRequest req)
            {
                try
                {
                    _logger.LogInformation("GetAllProducts function triggered");

                    var products = await _tableService.GetAllProductsAsync();

                    _logger.LogInformation("Retrieved {Count} products", products.Count);
                    return new OkObjectResult(products);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving products");
                    return new BadRequestObjectResult(new { error = "Failed to retrieve products", message = ex.Message });
                }
            }

            /// <summary>
            /// HTTP Function to get a specific product by ID
            /// GET /api/products/{id}
            /// </summary>
            [Function("GetProductById")]
            public async Task<IActionResult> GetProductById(
                [HttpTrigger(AuthorizationLevel.Function, "get", Route = "products/{id}")] HttpRequest req,
                string id)
            {
                try
                {
                    _logger.LogInformation("GetProductById function triggered for ID: {ProductId}", id);

                    if (string.IsNullOrEmpty(id))
                    {
                        return new BadRequestObjectResult(new { error = "Product ID is required" });
                    }

                    var product = await _tableService.GetProductByIdAsync("Product", id);

                    if (product == null)
                    {
                        return new NotFoundObjectResult(new { error = "Product not found" });
                    }

                    return new OkObjectResult(product);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving product {ProductId}", id);
                    return new BadRequestObjectResult(new { error = "Failed to retrieve product", message = ex.Message });
                }
            }

            /// <summary>
            /// HTTP Function to create a new product
            /// POST /api/products
            /// </summary>
            [Function("CreateProduct")]
            public async Task<IActionResult> CreateProduct(
                [HttpTrigger(AuthorizationLevel.Function, "post", Route = "products")] HttpRequest req)
            {
                try
                {
                    _logger.LogInformation("CreateProduct function triggered");

                    // Read the request body
                    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                    if (string.IsNullOrEmpty(requestBody))
                    {
                        return new BadRequestObjectResult(new { error = "Request body is required" });
                    }

                    // Deserialize the product object
                    var product = JsonSerializer.Deserialize<Product>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (product == null)
                    {
                        return new BadRequestObjectResult(new { error = "Invalid product data" });
                    }

                    // Ensure RowKey and PartitionKey are set
                    if (string.IsNullOrEmpty(product.RowKey))
                    {
                        product.RowKey = Guid.NewGuid().ToString();
                    }
                    product.PartitionKey = "Product";

                    await _tableService.InsertProductAsync(product);

                    _logger.LogInformation("Product created successfully: {ProductId}", product.RowKey);
                    return new CreatedResult($"/api/products/{product.RowKey}", product);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Invalid JSON in request body");
                    return new BadRequestObjectResult(new { error = "Invalid JSON format", message = ex.Message });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating product");
                    return new BadRequestObjectResult(new { error = "Failed to create product", message = ex.Message });
                }
            }

            /// <summary>
            /// HTTP Function to update an existing product
            /// PUT /api/products/{id}
            /// </summary>
            [Function("UpdateProduct")]
            public async Task<IActionResult> UpdateProduct(
                [HttpTrigger(AuthorizationLevel.Function, "put", Route = "products/{id}")] HttpRequest req,
                string id)
            {
                try
                {
                    _logger.LogInformation("UpdateProduct function triggered for ID: {ProductId}", id);

                    if (string.IsNullOrEmpty(id))
                    {
                        return new BadRequestObjectResult(new { error = "Product ID is required" });
                    }

                    // Read the request body
                    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                    if (string.IsNullOrEmpty(requestBody))
                    {
                        return new BadRequestObjectResult(new { error = "Request body is required" });
                    }

                    // Deserialize the product object
                    var product = JsonSerializer.Deserialize<Product>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (product == null)
                    {
                        return new BadRequestObjectResult(new { error = "Invalid product data" });
                    }

                    // Ensure the ID matches
                    if (product.RowKey != id)
                    {
                        return new BadRequestObjectResult(new { error = "Product ID mismatch" });
                    }

                    // Get existing product to preserve ETag
                    var existingProduct = await _tableService.GetProductByIdAsync("Product", id);
                    if (existingProduct == null)
                    {
                        return new NotFoundObjectResult(new { error = "Product not found" });
                    }

                    // Update fields while preserving ETag and ImageUrl if not provided
                    existingProduct.Name = product.Name;
                    existingProduct.Description = product.Description;
                    existingProduct.Price = product.Price;
                    existingProduct.StockQuantity = product.StockQuantity;

                    // Only update ImageUrl if provided
                    if (!string.IsNullOrEmpty(product.ImageUrl))
                    {
                        existingProduct.ImageUrl = product.ImageUrl;
                    }

                    await _tableService.UpdateProductAsync(existingProduct);

                    _logger.LogInformation("Product updated successfully: {ProductId}", id);
                    return new OkObjectResult(existingProduct);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Invalid JSON in request body");
                    return new BadRequestObjectResult(new { error = "Invalid JSON format", message = ex.Message });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating product {ProductId}", id);
                    return new BadRequestObjectResult(new { error = "Failed to update product", message = ex.Message });
                }
            }

            /// <summary>
            /// HTTP Function to delete a product
            /// DELETE /api/products/{id}
            /// </summary>
            [Function("DeleteProduct")]
            public async Task<IActionResult> DeleteProduct(
                [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "products/{id}")] HttpRequest req,
                string id)
            {
                try
                {
                    _logger.LogInformation("DeleteProduct function triggered for ID: {ProductId}", id);

                    if (string.IsNullOrEmpty(id))
                    {
                        return new BadRequestObjectResult(new { error = "Product ID is required" });
                    }

                    // Check if product exists
                    var existingProduct = await _tableService.GetProductByIdAsync("Product", id);
                    if (existingProduct == null)
                    {
                        return new NotFoundObjectResult(new { error = "Product not found" });
                    }

                    await _tableService.DeleteProductAsync("Product", id);

                    _logger.LogInformation("Product deleted successfully: {ProductId}", id);
                    return new NoContentResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting product {ProductId}", id);
                    return new BadRequestObjectResult(new { error = "Failed to delete product", message = ex.Message });
                }
            }

            #endregion

            #region Order Functions

            /// <summary>
            /// HTTP Function to retrieve all orders
            /// GET /api/orders
            /// </summary>
            [Function("GetAllOrders")]
            public async Task<IActionResult> GetAllOrders(
                [HttpTrigger(AuthorizationLevel.Function, "get", Route = "orders")] HttpRequest req)
            {
                try
                {
                    _logger.LogInformation("GetAllOrders function triggered");

                    var orders = await _tableService.GetAllOrdersAsync();

                    _logger.LogInformation("Retrieved {Count} orders", orders.Count);
                    return new OkObjectResult(orders);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving orders");
                    return new BadRequestObjectResult(new { error = "Failed to retrieve orders", message = ex.Message });
                }
            }

            /// <summary>
            /// HTTP Function to get a specific order by ID
            /// GET /api/orders/{id}
            /// </summary>
            [Function("GetOrderById")]
            public async Task<IActionResult> GetOrderById(
                [HttpTrigger(AuthorizationLevel.Function, "get", Route = "orders/{id}")] HttpRequest req,
                string id)
            {
                try
                {
                    _logger.LogInformation("GetOrderById function triggered for ID: {OrderId}", id);

                    if (string.IsNullOrEmpty(id))
                    {
                        return new BadRequestObjectResult(new { error = "Order ID is required" });
                    }

                    var order = await _tableService.GetOrderByIdAsync("Order", id);

                    if (order == null)
                    {
                        return new NotFoundObjectResult(new { error = "Order not found" });
                    }

                    return new OkObjectResult(order);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving order {OrderId}", id);
                    return new BadRequestObjectResult(new { error = "Failed to retrieve order", message = ex.Message });
                }
            }

            /// <summary>
            /// HTTP Function to create a new order
            /// POST /api/orders
            /// </summary>
            [Function("CreateOrder")]
            public async Task<IActionResult> CreateOrder(
                [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders")] HttpRequest req)
            {
                try
                {
                    _logger.LogInformation("CreateOrder function triggered");

                    // Read the request body
                    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                    if (string.IsNullOrEmpty(requestBody))
                    {
                        return new BadRequestObjectResult(new { error = "Request body is required" });
                    }

                    // Deserialize the order object
                    var order = JsonSerializer.Deserialize<Order>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (order == null)
                    {
                        return new BadRequestObjectResult(new { error = "Invalid order data" });
                    }

                    // Ensure RowKey and PartitionKey are set
                    if (string.IsNullOrEmpty(order.RowKey))
                    {
                        order.RowKey = Guid.NewGuid().ToString();
                    }
                    order.PartitionKey = "Order";

                    // Set order date if not provided
                    if (order.OrderDate == default)
                    {
                        order.OrderDate = DateTime.UtcNow;
                    }

                    await _tableService.InsertOrderAsync(order);

                    _logger.LogInformation("Order created successfully: {OrderId}", order.RowKey);
                    return new CreatedResult($"/api/orders/{order.RowKey}", order);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Invalid JSON in request body");
                    return new BadRequestObjectResult(new { error = "Invalid JSON format", message = ex.Message });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating order");
                    return new BadRequestObjectResult(new { error = "Failed to create order", message = ex.Message });
                }
            }

            /// <summary>
            /// HTTP Function to update an existing order
            /// PUT /api/orders/{id}
            /// </summary>
            [Function("UpdateOrder")]
            public async Task<IActionResult> UpdateOrder(
                [HttpTrigger(AuthorizationLevel.Function, "put", Route = "orders/{id}")] HttpRequest req,
                string id)
            {
                try
                {
                    _logger.LogInformation("UpdateOrder function triggered for ID: {OrderId}", id);

                    if (string.IsNullOrEmpty(id))
                    {
                        return new BadRequestObjectResult(new { error = "Order ID is required" });
                    }

                    // Read the request body
                    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                    if (string.IsNullOrEmpty(requestBody))
                    {
                        return new BadRequestObjectResult(new { error = "Request body is required" });
                    }

                    // Deserialize the order object
                    var order = JsonSerializer.Deserialize<Order>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (order == null)
                    {
                        return new BadRequestObjectResult(new { error = "Invalid order data" });
                    }

                    // Ensure the ID matches
                    if (order.RowKey != id)
                    {
                        return new BadRequestObjectResult(new { error = "Order ID mismatch" });
                    }

                    // Get existing order to preserve ETag
                    var existingOrder = await _tableService.GetOrderByIdAsync("Order", id);
                    if (existingOrder == null)
                    {
                        return new NotFoundObjectResult(new { error = "Order not found" });
                    }

                    // Update fields while preserving ETag
                    existingOrder.CustomerId = order.CustomerId;
                    existingOrder.ProductId = order.ProductId;
                    existingOrder.Quantity = order.Quantity;
                    existingOrder.TotalPrice = order.TotalPrice;
                    existingOrder.Status = order.Status;

                    // Preserve order date if not provided in update
                    if (order.OrderDate != default)
                    {
                        existingOrder.OrderDate = order.OrderDate;
                    }

                    await _tableService.UpdateOrderAsync(existingOrder);

                    _logger.LogInformation("Order updated successfully: {OrderId}", id);
                    return new OkObjectResult(existingOrder);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Invalid JSON in request body");
                    return new BadRequestObjectResult(new { error = "Invalid JSON format", message = ex.Message });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating order {OrderId}", id);
                    return new BadRequestObjectResult(new { error = "Failed to update order", message = ex.Message });
                }
            }

            /// <summary>
            /// HTTP Function to delete an order
            /// DELETE /api/orders/{id}
            /// </summary>
            [Function("DeleteOrder")]
            public async Task<IActionResult> DeleteOrder(
                [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "orders/{id}")] HttpRequest req,
                string id)
            {
                try
                {
                    _logger.LogInformation("DeleteOrder function triggered for ID: {OrderId}", id);

                    if (string.IsNullOrEmpty(id))
                    {
                        return new BadRequestObjectResult(new { error = "Order ID is required" });
                    }

                    // Check if order exists
                    var existingOrder = await _tableService.GetOrderByIdAsync("Order", id);
                    if (existingOrder == null)
                    {
                        return new NotFoundObjectResult(new { error = "Order not found" });
                    }

                    await _tableService.DeleteOrderAsync("Order", id);

                    _logger.LogInformation("Order deleted successfully: {OrderId}", id);
                    return new NoContentResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting order {OrderId}", id);
                    return new BadRequestObjectResult(new { error = "Failed to delete order", message = ex.Message });
                }
            }

            #endregion
        }
    }