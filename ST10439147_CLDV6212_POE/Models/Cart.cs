// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - Shopping Cart Models

namespace ST10439147_CLDV6212_POE.Models
{
    /// <summary>
    /// Represents a shopping cart stored in session
    /// </summary>
    public class Cart
    {
        public List<CartItem> Items { get; set; } = new List<CartItem>();

        /// <summary>
        /// Add item to cart or update quantity if already exists
        /// </summary>
        public void AddItem(Product product, int quantity)
        {
            var existingItem = Items.FirstOrDefault(i => i.ProductId == product.RowKey);

            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                Items.Add(new CartItem
                {
                    ProductId = product.RowKey,
                    ProductName = product.Name,
                    Price = product.Price,
                    Quantity = quantity,
                    ImageUrl = product.ImageUrl
                });
            }
        }

        /// <summary>
        /// Remove item from cart
        /// </summary>
        public void RemoveItem(string productId)
        {
            Items.RemoveAll(i => i.ProductId == productId);
        }

        /// <summary>
        /// Update item quantity
        /// </summary>
        public void UpdateQuantity(string productId, int quantity)
        {
            var item = Items.FirstOrDefault(i => i.ProductId == productId);
            if (item != null)
            {
                if (quantity <= 0)
                {
                    RemoveItem(productId);
                }
                else
                {
                    item.Quantity = quantity;
                }
            }
        }

        /// <summary>
        /// Clear all items from cart
        /// </summary>
        public void Clear()
        {
            Items.Clear();
        }

        /// <summary>
        /// Calculate total price of all items in cart
        /// </summary>
        public double GetTotal()
        {
            return Items.Sum(i => i.Price * i.Quantity);
        }

        /// <summary>
        /// Get total number of items in cart
        /// </summary>
        public int GetItemCount()
        {
            return Items.Sum(i => i.Quantity);
        }
    }

    /// <summary>
    /// Represents a single item in the shopping cart
    /// </summary>
    public class CartItem
    {
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public double Price { get; set; }
        public int Quantity { get; set; }
        public string? ImageUrl { get; set; }

        /// <summary>
        /// Calculate subtotal for this line item
        /// </summary>
        public double GetSubtotal()
        {
            return Price * Quantity;
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//