using Azure;
using Azure.Data.Tables;

namespace ST10439147_CLDV6212_POE.Models
{
    public class Order : ITableEntity
    {
        public string PartitionKey { get; set; } = "Order";
        public string RowKey { get; set; } // Unique OrderId (GUID)
        public string CustomerId { get; set; } // Links to Customer.RowKey
        public string ProductId { get; set; }  // Links to Product.RowKey
        public int Quantity { get; set; }
        public decimal TotalPrice { get; set; }
        public DateTime OrderDate { get; set; }
        public string Status { get; set; } // e.g. Pending, Shipped, Completed

        // Audit fields
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
