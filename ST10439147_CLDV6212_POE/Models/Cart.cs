namespace ST10439147_CLDV6212_POE.Models
{
    public class Cart
    {
        public int CartId { get; set; }
        public int UserId { get; set; }
        public string CustomerId { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime LastModified { get; set; }
        public List<CartItem> Items { get; set; } = new List<CartItem>();
    }

    public class CartItem
    {
        public int CartItemId { get; set; }
        public int CartId { get; set; }
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public string? ImageUrl { get; set; }
        public DateTime AddedDate { get; set; }

        // Calculated property
        public decimal TotalPrice => Quantity * UnitPrice;
    }
}
