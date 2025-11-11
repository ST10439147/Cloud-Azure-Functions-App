using Microsoft.Data.SqlClient;
using ST10439147_CLDV6212_POE.Models;

namespace ST10439147_CLDV6212_POE.Services
{
    /* public class CartService
     {
         private readonly string _connectionString;
         private readonly ILogger<CartService> _logger;
         private readonly TableService _tableService;

         public CartService(
             IConfiguration configuration,
             ILogger<CartService> logger,
             TableService tableService)
         {
             _connectionString = configuration["AzureSQL:ConnectionString"]
                 ?? throw new ArgumentNullException("Azure SQL connection string not configured");
             _logger = logger;
             _tableService = tableService;
         }

         // Get or create cart for user
         public async Task<Cart> GetOrCreateCartAsync(int userId, string customerId)
         {
             try
             {
                 using (var connection = new SqlConnection(_connectionString))
                 {
                     await connection.OpenAsync();

                     // Check if cart exists
                     var query = "SELECT CartId, UserId, CustomerId, CreatedDate, LastModified FROM Cart WHERE UserId = @UserId";

                     using (var cmd = new SqlCommand(query, connection))
                     {
                         cmd.Parameters.AddWithValue("@UserId", userId);

                         using (var reader = await cmd.ExecuteReaderAsync())
                         {
                             if (await reader.ReadAsync())
                             {
                                 return new Cart
                                 {
                                     CartId = reader.GetInt32(0),
                                     UserId = reader.GetInt32(1),
                                     CustomerId = reader.GetString(2),
                                     CreatedDate = reader.GetDateTime(3),
                                     LastModified = reader.GetDateTime(4)
                                 };
                             }
                         }
                     }

                     // Create new cart if doesn't exist
                     var insertQuery = @"
                         INSERT INTO Cart (UserId, CustomerId, CreatedDate, LastModified)
                         OUTPUT INSERTED.CartId
                         VALUES (@UserId, @CustomerId, GETDATE(), GETDATE())";

                     using (var insertCmd = new SqlCommand(insertQuery, connection))
                     {
                         insertCmd.Parameters.AddWithValue("@UserId", userId);
                         insertCmd.Parameters.AddWithValue("@CustomerId", customerId);

                         var cartId = (int)await insertCmd.ExecuteScalarAsync();

                         return new Cart
                         {
                             CartId = cartId,
                             UserId = userId,
                             CustomerId = customerId,
                             CreatedDate = DateTime.UtcNow,
                             LastModified = DateTime.UtcNow
                         };
                     }
                 }
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Error getting or creating cart for user: {UserId}", userId);
                 throw;
             }
         }

         // Add item to cart
         public async Task<bool> AddItemToCartAsync(int cartId, string productId, int quantity)
         {
             try
             {
                 // Get product details from Azure Table Storage
                 var product = await _tableService.GetProductByIdAsync("Product", productId);

                 if (product == null)
                 {
                     throw new InvalidOperationException("Product not found");
                 }

                 if (product.StockQuantity < quantity)
                 {
                     throw new InvalidOperationException($"Insufficient stock. Available: {product.StockQuantity}");
                 }

                 using (var connection = new SqlConnection(_connectionString))
                 {
                     await connection.OpenAsync();

                     // Check if item already exists in cart
                     var checkQuery = "SELECT CartItemId, Quantity FROM CartItems WHERE CartId = @CartId AND ProductId = @ProductId";

                     using (var checkCmd = new SqlCommand(checkQuery, connection))
                     {
                         checkCmd.Parameters.AddWithValue("@CartId", cartId);
                         checkCmd.Parameters.AddWithValue("@ProductId", productId);

                         using (var reader = await checkCmd.ExecuteReaderAsync())
                         {
                             if (await reader.ReadAsync())
                             {
                                 var cartItemId = reader.GetInt32(0);
                                 var currentQuantity = reader.GetInt32(1);
                                 reader.Close();

                                 // Update existing item
                                 var updateQuery = @"
                                     UPDATE CartItems 
                                     SET Quantity = @Quantity 
                                     WHERE CartItemId = @CartItemId";

                                 using (var updateCmd = new SqlCommand(updateQuery, connection))
                                 {
                                     updateCmd.Parameters.AddWithValue("@Quantity", currentQuantity + quantity);
                                     updateCmd.Parameters.AddWithValue("@CartItemId", cartItemId);
                                     await updateCmd.ExecuteNonQueryAsync();
                                 }

                                 // Update cart last modified
                                 await UpdateCartLastModifiedAsync(connection, cartId);
                                 return true;
                             }
                         }
                     }

                     // Insert new item
                     var insertQuery = @"
                         INSERT INTO CartItems (CartId, ProductId, ProductName, Quantity, UnitPrice, ImageUrl, AddedDate)
                         VALUES (@CartId, @ProductId, @ProductName, @Quantity, @UnitPrice, @ImageUrl, GETDATE())";

                     using (var insertCmd = new SqlCommand(insertQuery, connection))
                     {
                         insertCmd.Parameters.AddWithValue("@CartId", cartId);
                         insertCmd.Parameters.AddWithValue("@ProductId", productId);
                         insertCmd.Parameters.AddWithValue("@ProductName", product.Name);
                         insertCmd.Parameters.AddWithValue("@Quantity", quantity);
                         insertCmd.Parameters.AddWithValue("@UnitPrice", product.Price);
                         insertCmd.Parameters.AddWithValue("@ImageUrl", product.ImageUrl ?? (object)DBNull.Value);

                         await insertCmd.ExecuteNonQueryAsync();
                     }

                     // Update cart last modified
                     await UpdateCartLastModifiedAsync(connection, cartId);
                     return true;
                 }
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Error adding item to cart: {CartId}, Product: {ProductId}", cartId, productId);
                 throw;
             }
         }

         // Get cart items
         public async Task<List<CartItem>> GetCartItemsAsync(int cartId)
         {
             try
             {
                 var items = new List<CartItem>();

                 using (var connection = new SqlConnection(_connectionString))
                 {
                     await connection.OpenAsync();

                     var query = @"
                         SELECT CartItemId, CartId, ProductId, ProductName, Quantity, UnitPrice, ImageUrl, AddedDate
                         FROM CartItems
                         WHERE CartId = @CartId
                         ORDER BY AddedDate DESC";

                     using (var cmd = new SqlCommand(query, connection))
                     {
                         cmd.Parameters.AddWithValue("@CartId", cartId);

                         using (var reader = await cmd.ExecuteReaderAsync())
                         {
                             while (await reader.ReadAsync())
                             {
                                 items.Add(new CartItem
                                 {
                                     CartItemId = reader.GetInt32(0),
                                     CartId = reader.GetInt32(1),
                                     ProductId = reader.GetString(2),
                                     ProductName = reader.GetString(3),
                                     Quantity = reader.GetInt32(4),
                                     UnitPrice = reader.GetDecimal(5),
                                     ImageUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
                                     AddedDate = reader.GetDateTime(7)
                                 });
                             }
                         }
                     }
                 }

                 return items;
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Error getting cart items: {CartId}", cartId);
                 throw;
             }
         }

         // Update item quantity
         public async Task<bool> UpdateCartItemQuantityAsync(int cartItemId, int quantity)
         {
             try
             {
                 if (quantity <= 0)
                 {
                     return await RemoveCartItemAsync(cartItemId);
                 }

                 using (var connection = new SqlConnection(_connectionString))
                 {
                     await connection.OpenAsync();

                     var query = @"
                         UPDATE CartItems 
                         SET Quantity = @Quantity 
                         WHERE CartItemId = @CartItemId";

                     using (var cmd = new SqlCommand(query, connection))
                     {
                         cmd.Parameters.AddWithValue("@Quantity", quantity);
                         cmd.Parameters.AddWithValue("@CartItemId", cartItemId);

                         var rowsAffected = await cmd.ExecuteNonQueryAsync();
                         return rowsAffected > 0;
                     }
                 }
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Error updating cart item quantity: {CartItemId}", cartItemId);
                 throw;
             }
         }

         // Remove item from cart
         public async Task<bool> RemoveCartItemAsync(int cartItemId)
         {
             try
             {
                 using (var connection = new SqlConnection(_connectionString))
                 {
                     await connection.OpenAsync();

                     var query = "DELETE FROM CartItems WHERE CartItemId = @CartItemId";

                     using (var cmd = new SqlCommand(query, connection))
                     {
                         cmd.Parameters.AddWithValue("@CartItemId", cartItemId);

                         var rowsAffected = await cmd.ExecuteNonQueryAsync();
                         return rowsAffected > 0;
                     }
                 }
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Error removing cart item: {CartItemId}", cartItemId);
                 throw;
             }
         }

         // Clear cart
         public async Task<bool> ClearCartAsync(int cartId)
         {
             try
             {
                 using (var connection = new SqlConnection(_connectionString))
                 {
                     await connection.OpenAsync();

                     var query = "DELETE FROM CartItems WHERE CartId = @CartId";

                     using (var cmd = new SqlCommand(query, connection))
                     {
                         cmd.Parameters.AddWithValue("@CartId", cartId);

                         await cmd.ExecuteNonQueryAsync();
                         return true;
                     }
                 }
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Error clearing cart: {CartId}", cartId);
                 throw;
             }
         }

         // Process cart to order
         public async Task<string> ProcessCartToOrderAsync(int cartId, string customerId)
         {
             try
             {
                 var cartItems = await GetCartItemsAsync(cartId);

                 if (!cartItems.Any())
                 {
                     throw new InvalidOperationException("Cart is empty");
                 }

                 // Create orders for each item (or combine into single order)
                 foreach (var item in cartItems)
                 {
                     var order = new Order
                     {
                         PartitionKey = "Order",
                         RowKey = Guid.NewGuid().ToString(),
                         CustomerId = customerId,
                         ProductId = item.ProductId,
                         Quantity = item.Quantity,
                         TotalPrice = (double)item.UnitPrice * item.Quantity,
                         OrderDate = DateTime.UtcNow,
                         Status = "PROCESSED"
                     };

                     // Use existing order processing logic
                     await _tableService.InsertOrderAsync(order);

                     // Update product stock
                     var product = await _tableService.GetProductByIdAsync("Product", item.ProductId);
                     if (product != null)
                     {
                         product.StockQuantity -= item.Quantity;
                         await _tableService.UpdateProductAsync(product);
                     }
                 }

                 // Clear cart after processing
                 await ClearCartAsync(cartId);

                 return "Order processed successfully";
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Error processing cart to order: {CartId}", cartId);
                 throw;
             }
         }

         // Helper method
         private async Task UpdateCartLastModifiedAsync(SqlConnection connection, int cartId)
         {
             var updateQuery = "UPDATE Cart SET LastModified = GETDATE() WHERE CartId = @CartId";
             using (var cmd = new SqlCommand(updateQuery, connection))
             {
                 cmd.Parameters.AddWithValue("@CartId", cartId);
                 await cmd.ExecuteNonQueryAsync();
             }
         }
     }*/
}