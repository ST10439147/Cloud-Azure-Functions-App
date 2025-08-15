using Azure;
using Azure.Data.Tables;

namespace ST10439147_CLDV6212_POE.Models
{
    public class Product : ITableEntity
    {
        public string PartitionKey { get; set; } = "Product";
        public string RowKey { get; set; } // Unique ProductId (GUID)
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public string ImageUrl { get; set; } // Stored in Azure Blob

        // Audit fields
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
