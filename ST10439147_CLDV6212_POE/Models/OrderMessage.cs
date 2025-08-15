namespace ST10439147_CLDV6212_POE.Models
{
    public class OrderMessage
    {
        public string OrderId { get; set; }
        public string ProductId { get; set; }
        public string CustomerId { get; set; }
        public int Quantity { get; set; }
        public DateTime OrderDate { get; set; }
        public string Status { get; set; } // e.g., "Processing", "Shipped"
    }
}
